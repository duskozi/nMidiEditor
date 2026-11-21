using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using nMIDI.Model;
using nMIDI.Playback;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace nMIDI
{
    /// <summary>
    /// Choosing sounds by bank and name: the instrument picker for track settings, the program lane and the
    /// event list, trying a sound on the track's output, and the SoundFont's preset names.
    /// </summary>
    public partial class MainWindow
    {
        private DispatcherTimer? _tryTimer;
        private (OutputDevice Device, int Channel, int Note)? _trying;

        /// <summary>Read the SoundFont's preset names in the background, so the track list can name its sounds.</summary>
        private void InitInstruments()
        {
            string? path = _settings.SoundFontPath;
            if (string.IsNullOrWhiteSpace(path)) return;
            Task.Run(() => SoundFontPresets.Use(path)).ContinueWith(_ =>
            {
                foreach (var vm in Tracks) vm.Refresh();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>
        /// Show the picker for a track's channel. Null when cancelled. Trying a sound changes the channel on the
        /// device; when the dialog closes the sound at the playhead is sent again.
        /// </summary>
        private InstrumentChoice? AskInstrument(SongTrack track, int channel, InstrumentChoice? current, string title)
        {
            var old = Cursor;
            Cursor = Cursors.Wait;
            try { SoundFontPresets.Use(_settings.SoundFontPath); }
            finally { Cursor = old; }

            var vm = Tracks.FirstOrDefault(v => v.Track == track);
            var device = _outputs.Get(vm?.OutputDevice ?? NoSongPort);
            bool drums = channel == 9;
            var dialog = new InstrumentDialog(this, title, drums, current,
                                              device == null ? null : c => TryInstrument(device, channel, c, drums),
                                              ChooseSoundFont);
            bool ok = dialog.Ask();
            StopTrying();
            if (device != null && _player.State != PlayerState.Playing)
            {
                var back = SongOps.InstrumentAt(track, channel, (long)_player.PositionTicks);
                if (back is { } b) SendInstrument(device, channel, b);
            }
            foreach (var v in Tracks) v.Refresh();
            return ok ? dialog.Result : null;
        }

        /// <summary>Pick a SoundFont for the picker (the same one the audio export uses). Returns an error, or null.</summary>
        private string? ChooseSoundFont()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "SoundFont (*.sf2)|*.sf2", Title = "SoundFont" };
            if (dialog.ShowDialog(this) != true) return null;
            var old = Cursor;
            Cursor = Cursors.Wait;
            try
            {
                string? error = SoundFontPresets.Use(dialog.FileName);
                if (error == null) _settings.SoundFontPath = dialog.FileName;
                return error;
            }
            finally { Cursor = old; }
        }

        private static void SendInstrument(OutputDevice device, int channel, InstrumentChoice c)
        {
            var ch = (FourBitNumber)Math.Clamp(channel, 0, 15);
            try
            {
                if (c.Msb is int msb) device.SendEvent(new ControlChangeEvent((SevenBitNumber)0, (SevenBitNumber)msb) { Channel = ch });
                if (c.Lsb is int lsb) device.SendEvent(new ControlChangeEvent((SevenBitNumber)32, (SevenBitNumber)lsb) { Channel = ch });
                device.SendEvent(new ProgramChangeEvent((SevenBitNumber)c.Program) { Channel = ch });
            }
            catch (MidiDeviceException) { }
        }

        /// <summary>Play a note (a snare on drums) with the sound for a moment.</summary>
        private void TryInstrument(OutputDevice device, int channel, InstrumentChoice c, bool drums)
        {
            StopTrying();
            SendInstrument(device, channel, c);
            int note = drums ? 38 : 60;
            try { device.SendEvent(new NoteOnEvent((SevenBitNumber)note, (SevenBitNumber)Math.Max(1, _velocity)) { Channel = (FourBitNumber)channel }); }
            catch (MidiDeviceException) { return; }
            _trying = (device, channel, note);
            _tryTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _tryTimer.Tick -= OnTryTimer;
            _tryTimer.Tick += OnTryTimer;
            _tryTimer.Start();
        }

        private void OnTryTimer(object? sender, EventArgs e) => StopTrying();

        private void StopTrying()
        {
            _tryTimer?.Stop();
            if (_trying is not { } t) return;
            _trying = null;
            try { t.Device.SendEvent(new NoteOffEvent((SevenBitNumber)t.Note, (SevenBitNumber)0) { Channel = (FourBitNumber)t.Channel }); }
            catch (MidiDeviceException) { }
        }

        /// <summary>
        /// Put a program change (with its bank select) on the track's channel at the tick, from the picker. A program
        /// change already there is edited; otherwise the picker starts at the sound in effect.
        /// </summary>
        public void PickInstrument(SongTrack track, long tick)
        {
            if (_song == null) return;
            int channel = track.Channel;
            var current = SongOps.InstrumentAt(track, channel, tick);
            bool editing = track.Controls.Any(c => c.Kind == CtrlKind.Program && c.Channel == channel && c.Tick == tick);
            string where = _song.FormatBars(tick).Trim();
            var choice = AskInstrument(track, channel, current, $"Instrument at {where}, channel {channel + 1}");
            if (choice is not { } c) return;
            Commit(editing ? "Change program" : "Add program change", () => SongOps.SetInstrument(track, channel, tick, c));
        }

        /// <summary>The program lane asks for a sound at a grid line.</summary>
        void PianoRoll.ILaneHost.PickInstrument(long tick)
        {
            if (ActiveTrack is { } track) PickInstrument(track, tick);
        }

        /// <summary>Lane text for a program change: the sound with the bank in effect at it.</summary>
        string PianoRoll.ILaneHost.ProgramLabel(long tick, int program)
        {
            var track = ActiveTrack;
            if (track == null) return $"{program + 1} {GeneralMidi.Instruments[Math.Clamp(program, 0, 127)]}";
            var at = SongOps.InstrumentAt(track, track.Channel, tick);
            var c = at is { } a && a.Program == program ? a : new InstrumentChoice(null, null, program);
            return InstrumentCatalog.Label(c, track.Channel == 9);
        }
    }
}
