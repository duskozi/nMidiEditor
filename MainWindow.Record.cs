using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using Microsoft.Win32;
using nMIDI.Model;
using nMIDI.Playback;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace nMIDI
{
    /// <summary>
    /// MIDI input: recording into the edited track, thru, step input, the metronome, plus recent files,
    /// autosave, export and the event list.
    /// </summary>
    public partial class MainWindow
    {
        private enum RecordPhase { Off, CountIn, Recording }

        private const string NoInput = "(no input)";

        private readonly AppSettings _settings = AppSettings.Load();
        private readonly MidiInput _input = new();
        private bool _fillingInputs;

        // Read on the input device's thread.
        private volatile OutputDevice? _thruDevice;
        private volatile int _thruChannel;
        private volatile bool _thruOn = true;
        private volatile RecordPhase _recordPhase;
        private string? _thruPortName = "\0";

        // Recording
        private CancellationTokenSource? _countIn;
        private SongTrack? _recordTrack;
        private IReadOnlyList<SongNote> _recordBaseNotes = Array.Empty<SongNote>();
        private IReadOnlyList<SongCtrl> _recordBaseCtrls = Array.Empty<SongCtrl>();
        private readonly List<SongNote> _recNotes = new();
        private readonly List<SongCtrl> _recCtrls = new();
        private readonly Dictionary<int, (long Start, int Velocity)> _recHeld = new();
        private long _recFrom, _recTo;
        private bool _recShown = true;
        private DateTime _recLastShown;

        // Step input
        private readonly HashSet<int> _stepHeld = new();
        private readonly List<(int Note, int Velocity)> _stepChord = new();
        private readonly Stack<(List<SongNote> Notes, long From)> _steps = new();

        // Metronome
        private OutputDevice? _metronomeDevice;

        // Autosave
        private readonly DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromSeconds(60) };
        private int _editVersion, _autosavedVersion;

        private EventListWindow? _eventList;

        private void InitRecord()
        {
            tglThru.IsChecked = _settings.Thru;
            _thruOn = _settings.Thru;
            FillInputs();
            if (_settings.InputDevice != null && cmbInput.Items.Contains(_settings.InputDevice))
            {
                if (!_input.Open(_settings.InputDevice)) txtStatus.Text = _input.LastError;
            }
            cmbInput.DropDownOpened += (_, _) => FillInputs();
            _input.Received += OnInputReceived;

            keyboard.NoteDown += note => StepDown(note, _velocity);
            keyboard.NoteUp += StepUp;

            _history.Changed += (_, _) => _editVersion++;
            _autosaveTimer.Tick += (_, _) => Autosave();
            _autosaveTimer.Start();
            Closed += (_, _) =>
            {
                _autosaveTimer.Stop();
                _countIn?.Cancel();
                _input.Dispose();
                _settings.Save();
            };
        }

        // --- Input port and thru ---

        private void FillInputs()
        {
            _fillingInputs = true;
            try
            {
                cmbInput.Items.Clear();
                cmbInput.Items.Add(NoInput);
                foreach (var name in MidiInput.GetNames()) cmbInput.Items.Add(name);
                cmbInput.SelectedItem = _input.Name != null && cmbInput.Items.Contains(_input.Name) ? _input.Name : NoInput;
            }
            finally
            {
                _fillingInputs = false;
            }
        }

        private void OnInputChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_fillingInputs) return;
            string? name = cmbInput.SelectedItem as string;
            if (name == NoInput) name = null;
            if (name == _input.Name) return;
            _inputNotes.Clear();
            falling.LiveAllOff();
            if (_input.Open(name))
                txtStatus.Text = name == null
                    ? "MIDI input closed."
                    : $"MIDI input: {name}. Play to hear the edited track; press R to record, S for step input.";
            else
            {
                txtStatus.Text = _input.LastError;
                FillInputs();
                return;
            }
            _settings.InputDevice = name;
            _settings.Save();
            _monitorWindow?.UpdatePorts();
        }

        private void OnThruClick(object sender, RoutedEventArgs e)
        {
            _thruOn = tglThru.IsChecked == true;
            _settings.Thru = _thruOn;
            _settings.Save();
        }

        /// <summary>On the input device's thread: play the event through at once, then hand it to the UI.</summary>
        private void OnInputReceived(ChannelEvent e)
        {
            _monitorWindow?.ShowIn(e);
            if (_thruOn && _thruDevice is { } device)
            {
                try
                {
                    var copy = (ChannelEvent)e.Clone();
                    copy.Channel = (FourBitNumber)_thruChannel;
                    device.SendEvent(copy);
                }
                catch (Exception ex) when (ex is MidiDeviceException or ObjectDisposedException or InvalidOperationException) { }
            }
            double tick = _recordPhase == RecordPhase.Recording ? _player.PositionTicks : -1;
            Dispatcher.BeginInvoke(new Action(() => OnInputEvent(e, tick)));
        }

        private void OnInputEvent(ChannelEvent e, double tick)
        {
            ShowInputOnWheels(e);
            ShowInputOnPedals(e);
            if (e is NoteEvent held)
            {
                bool isDown = e is NoteOnEvent && held.Velocity > 0;
                TrackInputNote(held.NoteNumber, isDown);
                FallingLiveNote(held.NoteNumber, isDown, held.Velocity);
            }
            else if (e is ControlChangeEvent pedalCc) FallingLivePedal(pedalCc.ControlNumber, pedalCc.ControlValue);
            if (_song == null) return;
            if (_practiceMode)
            {
                if (e is NoteOnEvent press && press.Velocity > 0) PracticePress(press.NoteNumber);
                return;
            }
            if (e is NoteEvent note)
            {
                bool down = e is NoteOnEvent && note.Velocity > 0;
                if (tick >= 0 && _recordPhase == RecordPhase.Recording)
                {
                    if (down) RecordNoteDown(note.NoteNumber, note.Velocity, (long)Math.Round(tick));
                    else RecordNoteUp(note.NoteNumber, (long)Math.Round(tick));
                }
                else if (tglStep.IsChecked == true && _recordPhase == RecordPhase.Off)
                {
                    if (down) StepDown(note.NoteNumber, note.Velocity);
                    else StepUp(note.NoteNumber);
                }
            }
            else if (tick >= 0 && _recordPhase == RecordPhase.Recording && _recordTrack != null)
            {
                var copy = (ChannelEvent)e.Clone();
                copy.Channel = (FourBitNumber)_recordTrack.Channel;
                var ctrl = Song.ControlFrom((long)Math.Round(tick), copy);
                if (ctrl != null)
                {
                    _recCtrls.Add(ctrl);
                    _recTo = Math.Max(_recTo, ctrl.Tick);
                }
            }
        }

        /// <summary>
        /// Called every frame: keeps the thru and metronome ports on the edited track, ends a take when
        /// playback stops, and shows the notes being recorded.
        /// </summary>
        private void RecordFrame()
        {
            EnsureKeyboardSound();
            var vm = _activeVm ?? Tracks.FirstOrDefault();
            string? port = vm?.OutputDevice ?? NoSongPort;
            if (port != _thruPortName)
            {
                _thruPortName = port;
                _thruDevice = _outputs.Get(port);
            }
            _thruChannel = vm?.Track.Channel ?? 0;

            if (btnMetronome.IsChecked == true && _metronomeDevice != _thruDevice) ApplyMetronome();

            if (_recordPhase != RecordPhase.Recording) return;
            if (_player.State != PlayerState.Playing)
            {
                StopRecording();
                return;
            }
            long now = (long)Math.Round(_player.PositionTicks);
            _recFrom = Math.Min(_recFrom, now);
            _recTo = Math.Max(_recTo, now);
            if ((_recHeld.Count > 0 || !_recShown) && DateTime.UtcNow - _recLastShown > TimeSpan.FromMilliseconds(60))
                ShowRecorded(now);
        }

        // --- Recording ---

        private void OnRecordClick(object sender, RoutedEventArgs e) => ToggleRecord();

        private void ToggleRecord()
        {
            if (_recordPhase == RecordPhase.Off) StartRecording();
            else
            {
                StopRecording();
                Pause();
            }
        }

        private void OnRecordRightClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var menu = new ContextMenu();
            bool replace = _settings.ReplaceWhenRecording;
            menu.Items.Add(Check("Overdub: mix with the notes already there", !replace, () => _settings.ReplaceWhenRecording = false));
            menu.Items.Add(Check("Replace: remove the notes in the recorded range", replace, () => _settings.ReplaceWhenRecording = true));
            menu.Items.Add(new Separator());
            menu.Items.Add(Check("Count in one bar", _settings.CountIn, () => _settings.CountIn = !_settings.CountIn));
            menu.Items.Add(Check("Quantize the take to the grid", _settings.QuantizeRecording,
                                 () => _settings.QuantizeRecording = !_settings.QuantizeRecording));
            menu.PlacementTarget = btnRecord;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private MenuItem Check(string header, bool on, Action action)
        {
            var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = on };
            item.Click += (_, _) =>
            {
                action();
                _settings.Save();
            };
            return item;
        }

        private void StartRecording()
        {
            btnRecord.IsChecked = false;
            if (_song == null || _activeVm == null)
            {
                txtStatus.Text = "Open or create a song and pick a track to record into.";
                return;
            }
            if (_input.Name == null)
            {
                txtStatus.Text = "Choose a MIDI input (INPUT, above the track list) to record from.";
                return;
            }
            _editor.CancelDrag();
            _recordTrack = _activeVm.Track;
            _recordBaseNotes = _recordTrack.Notes;
            _recordBaseCtrls = _recordTrack.Controls;
            _recNotes.Clear();
            _recCtrls.Clear();
            _recHeld.Clear();
            _player.KeepRunning = true;
            btnRecord.IsChecked = true;

            if (_player.State == PlayerState.Playing)
            {
                BeginTake();   // punch in while playing
                return;
            }
            if (!_settings.CountIn)
            {
                BeginTake();
                Play();
                return;
            }

            _recordPhase = RecordPhase.CountIn;
            long tick = (long)Math.Round(_player.PositionTicks);
            double bpm = _song.Timeline.BpmAt(tick, _song.TicksPerQuarter) * _player.Speed;
            double beatMs = 60_000.0 / Math.Max(1, bpm) * _song.TicksPerBeat / _song.TicksPerQuarter;
            int beats = _song.BeatsPerBar;
            var device = _thruDevice;
            var cts = new CancellationTokenSource();
            _countIn = cts;
            txtStatus.Text = $"Count-in: {beats} beats…";
            Task.Run(() =>
            {
                var clock = Stopwatch.StartNew();
                for (int i = 0; i <= beats; i++)
                {
                    while (clock.Elapsed.TotalMilliseconds < i * beatMs)
                    {
                        if (cts.IsCancellationRequested) return;
                        Thread.Sleep(1);
                    }
                    if (i < beats) MidiPlayer.Click(device, i == 0);
                }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (cts.IsCancellationRequested || _recordPhase != RecordPhase.CountIn) return;
                    BeginTake();
                    Play();
                }));
            });
        }

        private void BeginTake()
        {
            _recordPhase = RecordPhase.Recording;
            _recFrom = _recTo = (long)Math.Round(_player.PositionTicks);
            _recShown = true;
            string mode = _settings.ReplaceWhenRecording ? "replace" : "overdub";
            txtStatus.Text = $"Recording into {_recordTrack?.Name} ({mode}). Space, R or Stop ends the take.";
            UpdateTransport();
        }

        private void RecordNoteDown(int note, int velocity, long tick)
        {
            int key = note;
            if (_recHeld.ContainsKey(key)) RecordNoteUp(note, tick);
            _recHeld[key] = (tick, velocity);
            _recShown = false;
        }

        private void RecordNoteUp(int note, long tick)
        {
            if (_recordTrack == null || !_recHeld.Remove(note, out var held)) return;
            long end = tick;
            // The loop jumped back while the key was down: the note ends at the end of the loop.
            if (end <= held.Start) end = HasLoop && tglLoop.IsChecked == true && held.Start < _loopEnd ? _loopEnd : held.Start + 1;
            _recNotes.Add(new SongNote(note, _recordTrack.Channel, held.Start, Math.Max(held.Start + 1, end), Math.Clamp(held.Velocity, 1, 127)));
            _recTo = Math.Max(_recTo, end);
            _recShown = false;
        }

        /// <summary>Show the take in the roll as it grows (held keys reach the playhead). Not an edit yet.</summary>
        private void ShowRecorded(long now)
        {
            if (_recordTrack == null) return;
            int channel = _recordTrack.Channel;
            var held = _recHeld.Select(h =>
                new SongNote(h.Key, channel, h.Value.Start, Math.Max(h.Value.Start + 1, now), Math.Clamp(h.Value.Velocity, 1, 127)));
            _recordTrack.Notes = SongTrack.Sort(_recordBaseNotes.Concat(_recNotes).Concat(held));
            _recShown = true;
            _recLastShown = DateTime.UtcNow;
            BuildElements();
        }

        /// <summary>End the take and keep it as one undo step (or end a count-in).</summary>
        private void StopRecording()
        {
            var phase = _recordPhase;
            _recordPhase = RecordPhase.Off;
            _countIn?.Cancel();
            _countIn = null;
            _player.KeepRunning = false;
            btnRecord.IsChecked = false;
            var track = _recordTrack;
            _recordTrack = null;
            if (phase != RecordPhase.Recording || track == null || _song == null)
            {
                if (phase == RecordPhase.CountIn) txtStatus.Text = "Recording cancelled.";
                return;
            }

            long now = (long)Math.Round(_player.PositionTicks);
            foreach (int note in _recHeld.Keys.ToList()) RecordNoteUp(note, now);
            track.Notes = _recordBaseNotes;   // the undo step starts from the track as it was
            track.Controls = _recordBaseCtrls;

            if (_recNotes.Count == 0 && _recCtrls.Count == 0)
            {
                BuildElements();
                txtStatus.Text = "Nothing was recorded.";
                return;
            }
            if (!_song.Tracks.Contains(track))
            {
                txtStatus.Text = "The track was removed while recording; the take is lost.";
                return;
            }

            var notes = _recNotes.ToList();
            if (_settings.QuantizeRecording && SnapTicks > 0)
                notes = NoteProcessing.Quantize(notes, SnapTicks, QuantizeMode.StartKeepLength);

            var baseNotes = _recordBaseNotes.AsEnumerable();
            var baseCtrls = _recordBaseCtrls.AsEnumerable();
            if (_settings.ReplaceWhenRecording)
            {
                long from = _recFrom, to = Math.Max(_recTo, from + 1);
                baseNotes = baseNotes.Where(n => n.Start < from || n.Start >= to);
                var kinds = _recCtrls.Select(c => (c.Kind, c.Number)).ToHashSet();
                baseCtrls = baseCtrls.Where(c => c.Tick < from || c.Tick >= to || !kinds.Contains((c.Kind, c.Number)));
            }
            var newNotes = baseNotes.Concat(notes).ToList();
            var newCtrls = baseCtrls.Concat(_recCtrls).ToList();
            Commit(notes.Count == 1 ? "Record note" : "Record", () =>
            {
                track.SetNotes(newNotes);
                track.SetControls(newCtrls);
            });
            if (track == ActiveTrack) _editor.SetSelection(notes);
            txtStatus.Text = $"Recorded {notes.Count:N0} note{(notes.Count == 1 ? "" : "s")}" +
                             (_recCtrls.Count > 0 ? $" and {_recCtrls.Count:N0} controller events" : "") +
                             (_settings.QuantizeRecording && SnapTicks > 0 ? ", quantized." : ".");
            _recNotes.Clear();
            _recCtrls.Clear();
        }

        // --- Step input ---

        private void OnStepClick(object sender, RoutedEventArgs e) => SetStepInput(tglStep.IsChecked == true);

        private void SetStepInput(bool on)
        {
            tglStep.IsChecked = on;
            _stepHeld.Clear();
            _stepChord.Clear();
            _steps.Clear();
            if (on)
            {
                _editor.ClearSelection();
                txtStatus.Text = "Step input: play a note or chord (MIDI input or the keyboard below). "
                               + "Length comes from the Length box; ← → move, Backspace takes a step back.";
            }
            else txtStatus.Text = "Step input off.";
        }

        private void StepDown(int note, int velocity)
        {
            if (tglStep.IsChecked != true || !CanEdit || _recordPhase != RecordPhase.Off) return;
            _stepHeld.Add(note);
            if (!_stepChord.Any(c => c.Note == note)) _stepChord.Add((note, Math.Clamp(velocity, 1, 127)));
        }

        private void StepUp(int note)
        {
            if (!_stepHeld.Remove(note) || _stepHeld.Count > 0 || _stepChord.Count == 0) return;
            var chord = _stepChord.ToList();
            _stepChord.Clear();
            var track = ActiveTrack;
            if (track == null || tglStep.IsChecked != true) return;

            long at = Math.Max(0, NoteOps.Snap((long)Math.Round(_player.PositionTicks), SnapTicks));
            long length = Math.Max(1, NewNoteLength);
            var notes = chord.Select(c => new SongNote(c.Note, track.Channel, at, at + length, c.Velocity)).ToList();
            Commit(notes.Count == 1 ? "Step input" : "Step input chord", () => track.Notes = NoteOps.Replace(track.Notes, Array.Empty<SongNote>(), notes));
            _steps.Push((notes, at));
            SeekTo(at + length);
            if (btnFollow.IsChecked == true) Follow(_player.PositionTicks);
        }

        /// <summary>Backspace in step input: remove the last step and move the playhead back to it.</summary>
        private bool StepBack()
        {
            var track = ActiveTrack;
            if (track == null || _steps.Count == 0) return false;
            var (notes, from) = _steps.Pop();
            var present = NoteOps.NewSet(track.Notes);
            var gone = notes.Where(present.Contains).ToList();
            if (gone.Count > 0) Commit("Step back", () => track.Notes = NoteOps.Replace(track.Notes, gone, Array.Empty<SongNote>()));
            SeekTo(from);
            return true;
        }

        // --- Metronome ---

        private void OnMetronomeClick(object sender, RoutedEventArgs e) => ApplyMetronome();

        private void ApplyMetronome()
        {
            bool on = btnMetronome.IsChecked == true;
            _metronomeDevice = on ? _thruDevice : null;
            _player.SetMetronome(on, _metronomeDevice);
        }

        // --- Keys ---

        /// <summary>Recording, step input, metronome and event list keys. Returns true if the key was used.</summary>
        private bool HandleRecordKey(Key key, bool ctrl, bool shift, bool alt)
        {
            switch (key)
            {
                case Key.R when !ctrl && !alt: ToggleRecord(); return true;
                case Key.S when !ctrl && !alt: SetStepInput(tglStep.IsChecked != true); return true;
                case Key.C when !ctrl && !alt:
                    btnMetronome.IsChecked = btnMetronome.IsChecked != true;
                    ApplyMetronome();
                    txtStatus.Text = btnMetronome.IsChecked == true ? "Metronome on." : "Metronome off.";
                    return true;
                case Key.E when ctrl: ShowEventList(); return true;
                case Key.Back when tglStep.IsChecked == true && !ctrl: return StepBack();
                case Key.Space when _recordPhase != RecordPhase.Off:
                    ToggleRecord();
                    return true;
            }
            return false;
        }

        // --- Recent files ---

        private void OnRecentClick(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            foreach (var path in _settings.RecentFiles)
            {
                bool exists = File.Exists(path);
                var item = Item(Path.GetFileName(path), "", exists, () => Open(path));
                item.ToolTip = exists ? path : path + " (not found)";
                menu.Items.Add(item);
            }
            if (_settings.RecentFiles.Count == 0) menu.Items.Add(Item("No recent files", "", false, () => { }));
            else
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Clear the list", "", true, () =>
                {
                    _settings.RecentFiles.Clear();
                    _settings.Save();
                }));
            }
            menu.PlacementTarget = btnRecent;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void RememberFile(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            _settings.AddRecent(Path.GetFullPath(path));
        }

        // --- Autosave ---

        private static string AutosaveFolder => Path.Combine(AppSettings.Folder, "autosave");
        private static string AutosaveBase => Path.Combine(AutosaveFolder, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        private sealed record AutosaveInfo(string? Path, int Format, DateTime Saved);

        /// <summary>Every minute, save unsaved changes to a side file, so a crash loses at most a minute of work.</summary>
        private void Autosave()
        {
            if (_song == null || _recordPhase != RecordPhase.Off || _editor.IsDragging) return;
            if (!_history.IsDirty)
            {
                DeleteAutosave();
                return;
            }
            if (_editVersion == _autosavedVersion) return;
            WriteAutosave();
        }

        /// <summary>
        /// After an unexpected error: save unsaved changes to the autosave file now, whatever the editor is doing.
        /// Returns true if there was something to save and it was saved.
        /// </summary>
        internal bool EmergencyAutosave()
        {
            try
            {
                return _song != null && _history.IsDirty && WriteAutosave();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                AppLog.Error("Emergency autosave", ex);
                return false;
            }
        }

        private bool WriteAutosave()
        {
            if (_song == null) return false;
            try
            {
                Directory.CreateDirectory(AutosaveFolder);
                _song.Save(AutosaveBase + ".mid", 1);
                AtomicFile.WriteAllText(AutosaveBase + ".json",
                    System.Text.Json.JsonSerializer.Serialize(new AutosaveInfo(_song.Path, _song.SourceFormat, DateTime.Now)));
                _autosavedVersion = _editVersion;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                AppLog.Error("Autosave", ex);
                return false;
            }
        }

        private void DeleteAutosave()
        {
            _autosavedVersion = -1;
            try
            {
                File.Delete(AutosaveBase + ".mid");
                File.Delete(AutosaveBase + ".json");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        /// <summary>At start-up: offer the work an earlier session left unsaved when it ended without closing.</summary>
        private bool TryRecover()
        {
            if (!Directory.Exists(AutosaveFolder)) return false;
            foreach (var info in Directory.GetFiles(AutosaveFolder, "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
            {
                string name = Path.GetFileNameWithoutExtension(info);
                if (!int.TryParse(name, out int pid) || pid == Environment.ProcessId) continue;
                if (IsRunning(pid)) continue;   // another editor window still has it
                string mid = Path.ChangeExtension(info, ".mid");
                try
                {
                    var data = System.Text.Json.JsonSerializer.Deserialize<AutosaveInfo>(File.ReadAllText(info));
                    if (data == null || !File.Exists(mid)) continue;
                    string title = data.Path != null ? Path.GetFileName(data.Path) : "an untitled song";
                    var answer = MessageBox.Show(this,
                        $"nMIDI Editor closed without saving changes to {title}.\nRecover the work autosaved at {data.Saved:g}?\n\nNo deletes the autosaved copy.",
                        "Recover unsaved work", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                    if (answer == MessageBoxResult.Cancel) return false;
                    if (answer == MessageBoxResult.Yes)
                    {
                        var song = Song.Load(mid);
                        song.Path = data.Path;
                        song.SourceFormat = data.Format;
                        DeleteFile(info);
                        DeleteFile(mid);
                        ShowSong(song);
                        Commit("Recover autosave", () => { });   // unsaved until the user saves
                        txtStatus.Text = $"Recovered {title} from {data.Saved:g}. Save it to keep it.";
                        return true;
                    }
                    DeleteFile(info);
                    DeleteFile(mid);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    txtStatus.Text = $"Could not recover an autosaved file: {ex.Message}";
                }
            }
            return false;
        }

        private static bool IsRunning(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return !process.HasExited && process.ProcessName.Contains("nMIDI", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        private static void DeleteFile(string path)
        {
            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        // --- Export ---

        /// <summary>Save the edited track (or its selected notes) as a MIDI file of its own, with the song's tempo map.</summary>
        private void ExportTrack(bool selectionOnly)
        {
            var track = ActiveTrack;
            if (_song == null || track == null) return;
            var notes = selectionOnly ? _editor.SelectedNotes() : track.Notes.ToList();
            if (selectionOnly && notes.Count == 0)
            {
                txtStatus.Text = "Select some notes to export.";
                return;
            }

            IEnumerable<SongCtrl> controls = track.Controls;
            if (selectionOnly)
            {
                long from = notes.Min(n => n.Start), to = notes.Max(n => n.End);
                // The controller values in force where the selection starts, then the changes inside it.
                var initial = track.Controls.Where(c => c.Tick < from && c.Kind != CtrlKind.PolyPressure)
                    .GroupBy(c => (c.Channel, c.Kind, c.Number))
                    .Select(g => g.Last() with { Tick = from });
                controls = initial.Concat(track.Controls.Where(c => c.Tick >= from && c.Tick <= to));
            }

            string safe = string.Concat(track.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
            if (safe.Length == 0) safe = "Track";
            var dialog = new SaveFileDialog
            {
                Title = selectionOnly ? "Export the selected notes" : "Export the track",
                Filter = "MIDI file (*.mid)|*.mid",
                FileName = $"{Path.GetFileNameWithoutExtension(_song.Name)} - {safe}{(selectionOnly ? " (selection)" : "")}.mid",
                DefaultExt = ".mid",
                AddExtension = true,
            };
            if (_song.Path != null) dialog.InitialDirectory = Path.GetDirectoryName(_song.Path);
            if (dialog.ShowDialog(this) != true) return;

            var copy = new SongTrack(track.Name, track.Channel) { Meta = track.Meta };
            copy.SetNotes(notes);
            copy.SetControls(controls);
            var export = new Song((int)_song.TicksPerQuarter)
            {
                Tempos = _song.Tempos,
                TimeSignatures = _song.TimeSignatures,
                Conductor = _song.Conductor,
            };
            export.SetTracks(new[] { copy });
            try
            {
                export.Rebuild();
                export.Save(dialog.FileName, 1);
                txtStatus.Text = $"Exported {notes.Count:N0} notes to {Path.GetFileName(dialog.FileName)}.";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                MessageBox.Show(this, $"Could not export:\n{ex.Message}", "nMIDI Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // --- Event list ---

        private void ShowEventList()
        {
            if (_song == null) return;
            if (_eventList == null)
            {
                _eventList = new EventListWindow(this);
                _eventList.Closed += (_, _) => _eventList = null;
                _eventList.Show();
            }
            else
            {
                if (_eventList.WindowState == WindowState.Minimized) _eventList.WindowState = WindowState.Normal;
                _eventList.Activate();
            }
        }

        // What the event list uses.
        internal void SelectNotes(IEnumerable<SongNote> notes) => _editor.SetSelection(notes);
        internal IReadOnlyCollection<SongNote> SelectedNoteSet => _editor.Selection;
        internal long? ParseBars(string text) => ParsePosition(text);
        internal long NewNoteTicks => NewNoteLength;
        internal long PlayheadTick => (long)Math.Round(_player.PositionTicks);
        internal void EditTempo(long tick) => ShowTempoDialog(null, tick);
        internal void EditTimeSignature(long tick) => ShowTimeSignatureDialog(null, tick);
        internal void EditMarker(long tick) => ShowMarkerDialog(null, tick);
        internal void EditKeySignature(long tick) => ShowKeySignatureDialog(null, tick);
        internal static string PitchName(int note) => NoteName(note);
    }
}
