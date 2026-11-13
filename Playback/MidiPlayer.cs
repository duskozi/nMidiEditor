using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using nMIDI.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace nMIDI.Playback
{
    public enum PlayerState { Stopped, Playing, Paused }

    /// <summary>
    /// Plays a <see cref="Song"/> on a background thread, routing every track to its own output port.
    ///
    /// The clock is a Stopwatch: song time = anchor + elapsed × speed. Events are sent when the
    /// clock passes them, and <see cref="Position"/> reads the same clock, so the playhead and the
    /// sound cannot drift apart. Changing the speed re-anchors the clock without a jump.
    ///
    /// Muting a track stops its note-ons and releases its sounding notes; controllers and program
    /// changes still go through so the track sounds right when it is unmuted. Seeking and starting
    /// "chase" the last program, controller and pitch-bend values before the new position.
    /// </summary>
    public sealed class MidiPlayer : IDisposable
    {
        private readonly object _gate = new();
        private readonly Stopwatch _clock = new();

        private PlaybackData? _song;
        private SongEvent[] _events = Array.Empty<SongEvent>();
        private TrackState[] _tracks = Array.Empty<TrackState>();
        private readonly int[] _keys = new int[128];   // sounding notes per key, all tracks
        private readonly HashSet<OutputDevice> _usedDevices = new();   // every port a track has played on

        private int _next;                 // index of the next event to send
        private double _anchorTime;        // song time (µs) at _anchorClock
        private TimeSpan _anchorClock;
        private double _speed = 1.0;
        private PlayerState _state;

        private Thread? _thread;
        private volatile bool _stopThread;

        private bool _metronome;
        private OutputDevice? _clickDevice;
        private int _nextClick;

        private double _loopStartTick, _loopEndTick;
        private bool _loopEnabled;
        private bool _loopArmed;           // the play position was before the loop end, so the loop applies

        private sealed class TrackState
        {
            public volatile bool Audible = true;
            public OutputDevice? Device;
            public readonly int[] Held = new int[16 * 128];   // sounding notes per channel × key
            public readonly int[] Keys = new int[128];        // sounding notes per key
            public int Peak;                                   // loudest note-on since the UI last asked
            public readonly int[] Mix = NewMix();              // mixer value per controller, -1 = the song's
            public readonly int[] SongVolume = NewVolumes();   // last CC 7 of the song per channel, -1 = none yet
            public int[] Channels = { 0 };                     // channels the mixer sends on
        }

        private static int[] NewMix() { var a = new int[128]; Array.Fill(a, -1); return a; }
        private static int[] NewVolumes() { var a = new int[16]; Array.Fill(a, -1); return a; }

        private double _master = 1.0;   // master volume, scales every CC 7 (0–1)

        /// <summary>Raised on the player thread when the end of the song is reached.</summary>
        public event EventHandler? Finished;

        public PlayerState State { get { lock (_gate) return _state; } }

        /// <summary>Playback speed, 1 = as written. Can change while playing.</summary>
        public double Speed
        {
            get { lock (_gate) return _speed; }
            set
            {
                lock (_gate)
                {
                    _anchorTime = TimeUnlocked;
                    _anchorClock = _clock.Elapsed;
                    _speed = Math.Clamp(value, 0.05, 8);
                }
            }
        }

        /// <summary>Current position in microseconds of song time (normal speed).</summary>
        public double Position { get { lock (_gate) return TimeUnlocked; } }

        /// <summary>Current position in ticks.</summary>
        public double PositionTicks
        {
            get
            {
                lock (_gate) return _song?.Timeline.TimeToTick(TimeUnlocked) ?? 0;
            }
        }

        private double TimeUnlocked => _state == PlayerState.Playing
            ? _anchorTime + (_clock.Elapsed - _anchorClock).TotalMicroseconds * _speed
            : _anchorTime;

        /// <summary>Use a new song. Stops playback; all tracks start audible with no output.</summary>
        public void Load(Song song)
        {
            Stop();
            lock (_gate)
            {
                _song = song.Playback;
                _events = _song.Events;
                _tracks = new TrackState[song.Tracks.Count];
                for (int i = 0; i < _tracks.Length; i++) _tracks[i] = new TrackState();
                Array.Clear(_keys);
                _anchorTime = 0;
                _next = 0;
            }
        }

        /// <summary>
        /// Use the song's new playback data after an edit, keeping the position in ticks. While playing,
        /// sounding notes are released (their note-offs may be gone) and playback goes on from the same
        /// place. Tracks keep their ports; a track added at the end starts silent until it gets a port.
        /// </summary>
        public void UpdateSong(Song song)
        {
            lock (_gate)
            {
                double tick = _song?.Timeline.TimeToTick(TimeUnlocked) ?? 0;
                foreach (var t in _tracks) ReleaseNotes(t);

                if (_tracks.Length != song.Tracks.Count)
                {
                    var tracks = new TrackState[song.Tracks.Count];
                    for (int i = 0; i < tracks.Length; i++) tracks[i] = i < _tracks.Length ? _tracks[i] : new TrackState();
                    _tracks = tracks;
                }

                _song = song.Playback;
                _events = _song.Events;
                _anchorTime = _song.Timeline.TickToTime(Math.Max(0, tick));
                _anchorClock = _clock.Elapsed;
                if (_state == PlayerState.Playing)
                {
                    _next = FirstEventAt(_anchorTime);
                    _nextClick = FirstClickAt(_anchorTime);
                    for (int i = 0; i < _tracks.Length; i++) Chase(i);
                }
            }
        }

        /// <summary>Turn the metronome on or off; it clicks on channel 10 of the given port.</summary>
        public void SetMetronome(bool on, OutputDevice? device)
        {
            lock (_gate)
            {
                _metronome = on;
                _clickDevice = device;
                _nextClick = FirstClickAt(TimeUnlocked);
            }
        }

        /// <summary>One metronome click (used for the count-in, from any thread).</summary>
        public static void Click(OutputDevice? device, bool accent)
        {
            var note = (SevenBitNumber)(accent ? 76 : 77);   // high and low wood block
            Send(device, new NoteOnEvent(note, (SevenBitNumber)(accent ? 120 : 90)) { Channel = (FourBitNumber)9 });
            Send(device, new NoteOffEvent(note, (SevenBitNumber)0) { Channel = (FourBitNumber)9 });
        }

        private int FirstClickAt(double time)
        {
            var clicks = _song?.Clicks;
            if (clicks == null) return 0;
            int lo = 0, hi = clicks.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (clicks[mid].Time < time) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// Loop between two ticks. When playback reaches the end of the range it jumps back to its start,
        /// if it started (or was moved) before the end.
        /// </summary>
        public void SetLoop(double startTick, double endTick, bool enabled)
        {
            lock (_gate)
            {
                _loopStartTick = Math.Max(0, Math.Min(startTick, endTick));
                _loopEndTick = Math.Max(startTick, endTick);
                _loopEnabled = enabled && _loopEndTick > _loopStartTick;
                ArmLoop();
            }
        }

        private void ArmLoop() =>
            _loopArmed = _loopEnabled && _song != null && TimeUnlocked < _song.Timeline.TickToTime(_loopEndTick) - 1;

        /// <summary>While true (recording), playback goes on after the last event instead of stopping.</summary>
        public bool KeepRunning { get; set; }

        public void SetTrackAudible(int track, bool audible)
        {
            lock (_gate)
            {
                if ((uint)track >= (uint)_tracks.Length) return;
                var t = _tracks[track];
                if (t.Audible == audible) return;
                t.Audible = audible;
                if (!audible) ReleaseNotes(t);
            }
        }

        /// <summary>Route a track to a port (null = silent). Sounding notes are released on the old port.</summary>
        public void SetTrackOutput(int track, OutputDevice? device)
        {
            lock (_gate)
            {
                if ((uint)track >= (uint)_tracks.Length) return;
                var t = _tracks[track];
                if (ReferenceEquals(t.Device, device)) return;
                ReleaseNotes(t);
                t.Device = device;
                if (device != null) _usedDevices.Add(device);
                if (_state == PlayerState.Playing) Chase(track);
            }
        }

        // --- Mixer ---

        /// <summary>GM default of a controller, sent when the mixer stops overriding it and the song has none.</summary>
        private static int DefaultControl(int cc) => cc switch { 7 => 100, 10 => 64, 74 => 64, 91 => 40, 11 => 127, _ => 0 };

        /// <summary>The channels a track's mixer settings are sent on.</summary>
        public void SetTrackChannels(int track, IReadOnlyList<int> channels)
        {
            lock (_gate)
            {
                if ((uint)track >= (uint)_tracks.Length) return;
                var list = new List<int>();
                foreach (int c in channels) if (c is >= 0 and < 16 && !list.Contains(c)) list.Add(c);
                _tracks[track].Channels = list.Count > 0 ? list.ToArray() : new[] { 0 };
            }
        }

        /// <summary>
        /// Override a controller of a track (volume 7, pan 10, reverb 91, chorus 93, ...) with a mixer value,
        /// or give it back to the song with -1. The song's own events for that controller are replaced by the
        /// mixer value while it is set. The new value is sent at once, playing or not.
        /// </summary>
        public void SetTrackControl(int track, int cc, int value)
        {
            lock (_gate)
            {
                if ((uint)track >= (uint)_tracks.Length || (uint)cc >= 120) return;
                var t = _tracks[track];
                value = value < 0 ? -1 : Math.Min(value, 127);
                if (t.Mix[cc] == value) return;
                t.Mix[cc] = value;
                if (t.Device == null) return;
                foreach (int ch in t.Channels)
                {
                    int v = cc == 7 ? EffectiveVolume(t, ch) : value >= 0 ? value : SongValueNow(track, ch, cc);
                    Send(t.Device, new ControlChangeEvent((SevenBitNumber)cc, (SevenBitNumber)v) { Channel = (FourBitNumber)ch });
                }
            }
        }

        /// <summary>Master volume, 0–1: every track's volume (CC 7) is scaled by it.</summary>
        public double MasterVolume
        {
            get { lock (_gate) return _master; }
            set
            {
                lock (_gate)
                {
                    value = Math.Clamp(value, 0, 1);
                    if (value == _master) return;
                    _master = value;
                    foreach (var t in _tracks)
                    {
                        if (t.Device == null) continue;
                        foreach (int ch in t.Channels)
                            Send(t.Device, new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)EffectiveVolume(t, ch)) { Channel = (FourBitNumber)ch });
                    }
                }
            }
        }

        private int EffectiveVolume(TrackState t, int channel)
        {
            int v = t.Mix[7] >= 0 ? t.Mix[7] : t.SongVolume[channel] >= 0 ? t.SongVolume[channel] : DefaultControl(7);
            return Math.Clamp((int)Math.Round(v * _master), 0, 127);
        }

        /// <summary>The song's last value of a controller before the play position, or its default.</summary>
        private int SongValueNow(int track, int channel, int cc)
        {
            int end = _state == PlayerState.Playing ? _next : FirstEventAt(TimeUnlocked);
            for (int i = Math.Min(end, _events.Length) - 1; i >= 0; i--)
            {
                var e = _events[i];
                if (e.Track == track && e.Event is ControlChangeEvent c && c.ControlNumber == cc && c.Channel == channel)
                    return c.ControlValue;
            }
            return DefaultControl(cc);
        }

        /// <summary>Send a song controller through the mixer: overridden values replace it, CC 7 is scaled.</summary>
        private void SendControl(TrackState t, ControlChangeEvent cc)
        {
            int number = cc.ControlNumber;
            if (number == 7)
            {
                t.SongVolume[cc.Channel] = cc.ControlValue;
                Send(t.Device, new ControlChangeEvent(cc.ControlNumber, (SevenBitNumber)EffectiveVolume(t, cc.Channel)) { Channel = cc.Channel });
            }
            else if (number < 120 && t.Mix[number] >= 0)
                Send(t.Device, new ControlChangeEvent(cc.ControlNumber, (SevenBitNumber)t.Mix[number]) { Channel = cc.Channel });
            else
                Send(t.Device, cc);
        }

        /// <summary>Send the mixer's overrides (and the master-scaled volume) on the track's channels.</summary>
        private void ApplyMix(TrackState t)
        {
            if (t.Device == null) return;
            foreach (int ch in t.Channels)
            {
                for (int cc = 0; cc < 120; cc++)
                {
                    if (cc == 7)
                    {
                        if (t.Mix[7] >= 0 || _master < 1)
                            Send(t.Device, new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)EffectiveVolume(t, ch)) { Channel = (FourBitNumber)ch });
                    }
                    else if (t.Mix[cc] >= 0)
                        Send(t.Device, new ControlChangeEvent((SevenBitNumber)cc, (SevenBitNumber)t.Mix[cc]) { Channel = (FourBitNumber)ch });
                }
            }
        }

        public void Play()
        {
            lock (_gate)
            {
                if (_song == null || _state == PlayerState.Playing) return;
                if (_anchorTime >= _song.LengthTime && !KeepRunning) _anchorTime = 0;

                _next = FirstEventAt(_anchorTime);
                _nextClick = FirstClickAt(_anchorTime);
                for (int i = 0; i < _tracks.Length; i++) Chase(i);

                _anchorClock = _clock.Elapsed;
                _clock.Start();
                _state = PlayerState.Playing;
                ArmLoop();
            }
            StartThread();
        }

        public void Pause()
        {
            lock (_gate)
            {
                if (_state != PlayerState.Playing) return;
                _anchorTime = TimeUnlocked;
                _state = PlayerState.Paused;
                Silence(soundOff: false);
            }
            StopThread();
        }

        public void Stop()
        {
            lock (_gate)
            {
                _anchorTime = 0;
                _next = 0;
                _state = PlayerState.Stopped;
                // Always silence, even when already stopped: it is cheap and Stop is the user's "make it quiet" button.
                Silence(soundOff: true);
            }
            StopThread();
        }

        /// <summary>Move to a tick. While playing, playback continues from there.</summary>
        public void Seek(double tick)
        {
            lock (_gate)
            {
                if (_song == null) return;
                // No upper limit: in the editor the playhead may sit after the last note (to paste or record there).
                double time = Math.Max(0, _song.Timeline.TickToTime(Math.Max(0, tick)));
                _anchorTime = time;
                _anchorClock = _clock.Elapsed;
                ArmLoop();
                if (_state == PlayerState.Stopped) _state = PlayerState.Paused;
                if (_state != PlayerState.Playing) return;

                foreach (var t in _tracks) ReleaseNotes(t);
                _next = FirstEventAt(time);
                _nextClick = FirstClickAt(time);
                for (int i = 0; i < _tracks.Length; i++) Chase(i);
            }
        }

        /// <summary>
        /// Panic: pause playback (keeping the position) and forget every sounding note. The caller then
        /// sends the reset messages to every port; on the next Play the controllers are chased again.
        /// </summary>
        public void Panic()
        {
            lock (_gate)
            {
                if (_state == PlayerState.Playing)
                {
                    _anchorTime = TimeUnlocked;
                    _state = PlayerState.Paused;
                }
                Silence(soundOff: true);
            }
            StopThread();
        }

        /// <summary>
        /// Send everything that silences a port: sustain off, All Sound Off, All Notes Off and Reset All
        /// Controllers on all 16 channels, then a note-off for every key, for synths that ignore the
        /// channel-mode messages.
        /// </summary>
        public static void SilencePort(OutputDevice device)
        {
            for (int ch = 0; ch < 16; ch++)
            {
                var channel = (FourBitNumber)ch;
                Send(device, new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)0) { Channel = channel });    // sustain off
                Send(device, new ControlChangeEvent((SevenBitNumber)120, (SevenBitNumber)0) { Channel = channel });   // all sound off
                Send(device, new ControlChangeEvent((SevenBitNumber)123, (SevenBitNumber)0) { Channel = channel });   // all notes off
                Send(device, new ControlChangeEvent((SevenBitNumber)121, (SevenBitNumber)0) { Channel = channel });   // reset all controllers
                for (int key = 0; key < 128; key++)
                    Send(device, new NoteOffEvent((SevenBitNumber)key, (SevenBitNumber)0) { Channel = channel });
            }
        }

        /// <summary>Loudest note-on velocity (0–127) of a track since the last call.</summary>
        public int TakePeak(int track)
        {
            var tracks = _tracks;   // Load can swap the array from another thread
            if ((uint)track >= (uint)tracks.Length) return 0;
            return Interlocked.Exchange(ref tracks[track].Peak, 0);
        }

        /// <summary>True while this track holds this key (only audible tracks play notes).</summary>
        public bool IsKeyDown(int track, int note)
        {
            var tracks = _tracks;
            return (uint)track < (uint)tracks.Length && (uint)note < 128 && Volatile.Read(ref tracks[track].Keys[note]) > 0;
        }

        /// <summary>True while any audible track holds this key.</summary>
        public bool IsKeyDown(int note) => (uint)note < 128 && Volatile.Read(ref _keys[note]) > 0;

        // --- Player thread ---

        private void StartThread()
        {
            StopThread();
            _stopThread = false;
            _thread = new Thread(Run) { IsBackground = true, Name = "nMIDI player", Priority = ThreadPriority.Highest };
            _thread.Start();
        }

        private void StopThread()
        {
            var thread = _thread;
            if (thread == null) return;
            _stopThread = true;
            if (thread != Thread.CurrentThread) thread.Join(500);
            _thread = null;
        }

        private void Run()
        {
            TimerResolution.Begin();
            try
            {
                while (!_stopThread)
                {
                    double wait;
                    bool finished = false;
                    lock (_gate)
                    {
                        if (_state != PlayerState.Playing || _song == null) break;
                        double now = TimeUnlocked;

                        if (_loopEnabled && _loopArmed)
                        {
                            double loopEnd = _song.Timeline.TickToTime(_loopEndTick);
                            if (now >= loopEnd)
                            {
                                // Play what is left before the loop end, then jump back to the loop start.
                                while (_next < _events.Length && _events[_next].Time < loopEnd)
                                    Dispatch(_events[_next++]);
                                foreach (var t in _tracks) ReleaseNotes(t);
                                _anchorTime = _song.Timeline.TickToTime(_loopStartTick);
                                _anchorClock = _clock.Elapsed;
                                _next = FirstEventAt(_anchorTime);
                                _nextClick = FirstClickAt(_anchorTime);
                                for (int i = 0; i < _tracks.Length; i++) Chase(i);
                                now = _anchorTime;
                            }
                        }
                        while (_next < _events.Length && _events[_next].Time <= now)
                            Dispatch(_events[_next++]);

                        var clicks = _song.Clicks;
                        if (_metronome)
                            while (_nextClick < clicks.Length && clicks[_nextClick].Time <= now)
                                Click(_clickDevice, clicks[_nextClick++].Accent);
                        else
                            while (_nextClick < clicks.Length && clicks[_nextClick].Time <= now) _nextClick++;

                        if (_next >= _events.Length && now >= _song.LengthTime && !KeepRunning)
                        {
                            finished = true;
                            Silence(soundOff: false);
                            _state = PlayerState.Stopped;
                            _anchorTime = 0;
                            _next = 0;
                        }
                        double nextTime = _next < _events.Length ? _events[_next].Time : double.MaxValue;
                        if (_metronome && _nextClick < clicks.Length) nextTime = Math.Min(nextTime, clicks[_nextClick].Time);
                        wait = nextTime < double.MaxValue ? (nextTime - now) / _speed / 1000.0 : 5;
                    }

                    if (finished)
                    {
                        // Only clear the field if it is still this thread: Play may already have started a new one.
                        Interlocked.CompareExchange(ref _thread, null, Thread.CurrentThread);
                        Finished?.Invoke(this, EventArgs.Empty);
                        break;
                    }

                    // Sleep(1) is ~1 ms with the raised timer resolution; spin for the last stretch.
                    if (wait > 1.5) Thread.Sleep(1);
                    else Thread.Yield();
                }
            }
            finally
            {
                TimerResolution.End();
            }
        }

        private void Dispatch(in SongEvent e)
        {
            var t = _tracks[e.Track];
            var ev = e.Event;

            if (ev is NoteEvent note)
            {
                int key = note.Channel * 128 + note.NoteNumber;
                if (Song.IsNoteOn(note))
                {
                    if (!t.Audible) return;
                    t.Held[key]++;
                    t.Keys[note.NoteNumber]++;
                    _keys[note.NoteNumber]++;
                    int velocity = ((NoteOnEvent)note).Velocity;
                    if (velocity > t.Peak) t.Peak = velocity;
                    Send(t.Device, ev);
                }
                else if (t.Held[key] > 0)
                {
                    // Only notes this track started; muted notes were never sent.
                    t.Held[key]--;
                    t.Keys[note.NoteNumber]--;
                    _keys[note.NoteNumber]--;
                    Send(t.Device, ev);
                }
                return;
            }

            if (ev is ControlChangeEvent control) SendControl(t, control);
            else Send(t.Device, ev);
        }

        private static void Send(OutputDevice? device, MidiEvent e)
        {
            if (device == null) return;
            try { device.SendEvent(e); }
            catch (MidiDeviceException) { }
        }

        private int FirstEventAt(double time)
        {
            int lo = 0, hi = _events.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_events[mid].Time < time) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// <summary>Send a track's last program, controller and pitch-bend values before the current event.</summary>
        private void Chase(int track)
        {
            var t = _tracks[track];
            if (t.Device == null) return;

            var programs = new Dictionary<int, ChannelEvent>();
            var controls = new Dictionary<int, ChannelEvent>();
            var bends = new Dictionary<int, ChannelEvent>();
            for (int i = 0; i < _next; i++)
            {
                var e = _events[i];
                if (e.Track != track) continue;
                switch (e.Event)
                {
                    case ProgramChangeEvent pc:
                        programs[pc.Channel] = pc;
                        break;
                    case ControlChangeEvent cc when cc.ControlNumber < 120:
                        controls[cc.Channel * 128 + cc.ControlNumber] = cc;
                        break;
                    case PitchBendEvent pb:
                        bends[pb.Channel] = pb;
                        break;
                }
            }

            Array.Fill(t.SongVolume, -1);
            foreach (var e in programs.Values) Send(t.Device, e);
            foreach (var e in controls.Values) SendControl(t, (ControlChangeEvent)e);
            foreach (var e in bends.Values) Send(t.Device, e);
            ApplyMix(t);
        }

        /// <summary>Note-off for every note the track is holding.</summary>
        private void ReleaseNotes(TrackState t)
        {
            for (int key = 0; key < t.Held.Length; key++)
            {
                int count = t.Held[key];
                if (count == 0) continue;
                _keys[key % 128] = Math.Max(0, _keys[key % 128] - count);
                t.Keys[key % 128] = Math.Max(0, t.Keys[key % 128] - count);
                t.Held[key] = 0;
                Send(t.Device, new NoteOffEvent((SevenBitNumber)(key % 128), (SevenBitNumber)0) { Channel = (FourBitNumber)(key / 128) });
            }
        }

        /// <summary>
        /// Release every note and reset sustain on all channels of every port any track has played on
        /// (not only the current ones, so notes left on a port a track was moved away from stop too).
        /// With <paramref name="soundOff"/>, also send All Sound Off, which cuts release and reverb tails
        /// that keep ringing after a plain note-off.
        /// </summary>
        private void Silence(bool soundOff)
        {
            foreach (var t in _tracks)
            {
                ReleaseNotes(t);
                if (t.Device != null) _usedDevices.Add(t.Device);
            }
            Array.Clear(_keys);

            foreach (var device in _usedDevices)
            {
                for (int ch = 0; ch < 16; ch++)
                {
                    var channel = (FourBitNumber)ch;
                    Send(device, new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)0) { Channel = channel });    // sustain off
                    if (soundOff)
                        Send(device, new ControlChangeEvent((SevenBitNumber)120, (SevenBitNumber)0) { Channel = channel });   // all sound off
                    Send(device, new ControlChangeEvent((SevenBitNumber)123, (SevenBitNumber)0) { Channel = channel });   // all notes off
                }
            }
        }

        public void Dispose()
        {
            Stop();
            Finished = null;
        }

        /// <summary>1 ms Windows timer resolution while playing, so Thread.Sleep(1) sleeps about 1 ms.</summary>
        private static class TimerResolution
        {
            [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
            private static extern uint TimeBeginPeriod(uint period);

            [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
            private static extern uint TimeEndPeriod(uint period);

            public static void Begin()
            {
                try { TimeBeginPeriod(1); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            }

            public static void End()
            {
                try { TimeEndPeriod(1); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            }
        }
    }
}
