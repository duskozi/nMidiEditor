using Microsoft.Win32;
using NinePatchWpf;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using nMIDI.Piano;
using nMIDI.Model;
using nMIDI.Playback;
using nMIDI.PianoRoll;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MidiDeviceException = Melanchall.DryWetMidi.Multimedia.MidiDeviceException;

namespace nMIDI
{
    public partial class MainWindow : Window
    {
        private const int MaxNote = PianoRollStyle.MaxNote;

        private readonly MidiOutputs _outputs = new();
        private readonly MidiPlayer _player = new();
        private readonly NoteSkins _skins = new();
        private readonly List<NinePatchElement> _elements = new();
        private readonly string? _startupFile;

        private Song? _song;
        private bool _velocityColors;
        private bool _updatingAllOutputs;
        private long _lastShownTick = -1;
        private long _keyboardSignature;
        private GridLength _keyboardHeight = new(160);
        private int _auditionNote = -1;
        private bool _batchTrackChanges;
        private TracksWindow? _tracksWindow;

        public MainWindow(string? fileToOpen = null)
        {
            _startupFile = fileToOpen;
            InitializeComponent();
            DataContext = this;

            pianoKeys.Viewer = viewer;
            keyboard.NoteDown += OnAuditionDown;
            keyboard.NoteUp += OnAuditionUp;           
            ruler.Viewer = viewer;
            ruler.Seek += (_, tick) => SeekTo(tick);

            viewer.Elements = _elements;
            viewer.ViewChanged += (_, _) => OnViewChanged();
            viewer.Rendered += (_, _) => ShowRenderInfo();
            viewer.RenderBackground += (_, e) =>
                PianoRollStyle.DrawGrid(e.Context, viewer, e.ViewportSize, _song?.TicksPerBeat ?? 480, _song?.BeatsPerBar ?? 4, _song != null ? SnapTicks : 0,
                    CurrentScale is { } scale ? n => scale.Contains(CurrentKey, n) : null, CurrentKey, GridBars);

            _player.Finished += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateTransport();
                ShowPosition(force: true);
                OnPracticeSongEnd();
            }));

            OutputNames = new ObservableCollection<string>(_outputs.Names);
            Loaded += OnLoaded;
            Closed += OnClosed;
            PreviewKeyDown += OnPreviewKeyDown;
            Drop += OnDrop;
            DragOver += OnDragOver;
            SourceInitialized += (_, _) => UseDarkTitleBar();
            CompositionTarget.Rendering += OnFrame;
            _outputs.Sent += e => _monitorWindow?.ShowOut(e);
            InitEditing();
            InitBarLayout();
            InitWheels();
            InitPedals();
            InitChords();
            InitFalling();
            InitInstruments();
        }

        /// <summary>The track list.</summary>
        public ObservableCollection<TrackViewModel> Tracks { get; } = new();

        /// <summary>The MIDI output ports, for the combo boxes.</summary>
        public ObservableCollection<string> OutputNames { get; }

        /// <summary>The loaded song, or null.</summary>
        internal Song? CurrentSong => _song;

        internal MidiPlayer Player => _player;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            viewer.SetView(0, 0, 12, double.NaN);
            viewer.ScrollToY(MaxNote - 60, 0.5);
            AttachEditor();
            ApplySavedSettings();
            SetAllOutputsSelection();   // show the default output right away
            UpdateTransport();
            bool recovered = TryRecover();
            if (!string.IsNullOrEmpty(_startupFile)) Open(_startupFile);
            else if (!recovered) ReopenLastFile();
        }

        // --- Loading ---

        private void OnOpen(object sender, RoutedEventArgs e) => ShowOpenDialog();

        private void ShowOpenDialog()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open MIDI file",
                Filter = "MIDI files (*.mid;*.midi;*.kar;*.rmi)|*.mid;*.midi;*.kar;*.rmi|All files|*.*",
            };
            if (dialog.ShowDialog(this) == true) Open(dialog.FileName);
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetData(DataFormats.FileDrop) is string[] files && Song.FilesIn(files).Any()
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && Song.FilesIn(files).FirstOrDefault() is { } file)
                Open(file);
        }

        private void Open(string path)
        {
            if (!ConfirmDiscard()) return;
            Song song;
            try
            {
                Cursor = Cursors.Wait;
                song = Song.Load(path);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                MessageBox.Show(this, $"Could not open the file:\n{ex.Message}", "nMIDI Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally
            {
                Cursor = null;
            }
            ShowSong(song);
            RememberFile(path);
        }

        /// <summary>Show a song that was just loaded or created, with a fresh undo history.</summary>
        private void ShowSong(Song song)
        {
            _editor.CancelDrag();
            _editor.Selection.Clear();
            ClearCoEdited();
            _activeVm = null;
            _steps.Clear();
            _stepChord.Clear();
            _stepHeld.Clear();
            _history.Reset(song);
            _lastLength = song.TicksPerQuarter;
            _player.Load(song);
            ResetOutputs();
            _song = song;
            _lastShownTick = -1;
            AttachFingering(song);

            // Keep the output ports of the previous song: track N keeps track N's port, and tracks
            // beyond the old count use the first old track's port. The first song uses the default port.
            var oldPorts = Tracks.Select(t => t.OutputDevice)
                                 .Select(p => p != null && OutputNames.Contains(p) ? p : null)
                                 .ToList();
            string? fallbackPort = oldPorts.FirstOrDefault(p => p != null) ?? NoSongPort;

            foreach (var t in Tracks) t.PropertyChanged -= OnTrackChanged;
            Tracks.Clear();
            foreach (var track in song.Tracks)
            {
                string? port = track.Index < oldPorts.Count ? oldPorts[track.Index] ?? fallbackPort : fallbackPort;
                var vm = new TrackViewModel(track, NoteSkins.TrackColors[track.Index % NoteSkins.TrackColors.Length]) { OutputDevice = port };
                vm.PropertyChanged += OnTrackChanged;
                Tracks.Add(vm);
                _player.SetTrackOutput(track.Index, _outputs.Get(port));
                PushMix(vm);
            }
            _fallbackPort = fallbackPort;
            SetAllOutputsSelection();

            UpdateFileInfo();
            UpdateRuler();
            tglLoop.IsChecked = false;
            SetLoopRange(0, 0, enable: false);
            pnlEmpty.Visibility = Visibility.Collapsed;
            if (_outputs.LastError != null) txtStatus.Text = _outputs.LastError;

            SetActiveTrack(Tracks.FirstOrDefault(t => t.Track.Notes.Count > 0) ?? Tracks.FirstOrDefault());
            BuildElements();
            ApplyKeyRange();
            _keyboardSignature = 0;
            viewer.ContentWidth = Math.Max(song.LengthTicks + song.TicksPerBar * 8, song.TicksPerBar * 16);
            _tracksWindow?.ShowSong(song);
            ApplyDefaultZoom();
            UpdateTrackSummary();
            UpdateTransport();
            UpdateEditState();
            ShowPosition(force: true);
            if (_practiceMode) RebuildPractice();
            ApplyFalling();
            ResetLiveChord();
        }

        private string? _fallbackPort;

        /// <summary>Port chosen in "All outputs" before any song was open.</summary>
        private string? _noSongPort;
        private string? NoSongPort => _noSongPort != null && OutputNames.Contains(_noSongPort) ? _noSongPort : _outputs.DefaultName;

        private void UpdateFileInfo()
        {
            if (_song is not { } song) return;
            string length = Song.FormatTime(song.LengthTime)[..5];
            txtFileInfo.Text = $"{song.Tracks.Count} tracks  ·  {song.NoteCount:N0} notes  ·  {length}  ·  {song.TicksPerQuarter} PPQ";
            txtSignature.Text = $"{song.BeatsPerBar}/{song.TimeSignatures[0].Denominator}";
            UpdateEditState();
        }

        /// <summary>
        /// Make the track list match the song's tracks after tracks were added, removed or reordered
        /// (or an undo brought them back). Existing rows keep their settings; every track's port and
        /// mute state is sent to the player again, since track numbers may have changed.
        /// </summary>
        private void SyncTrackList()
        {
            if (_song == null) return;
            var byTrack = Tracks.ToDictionary(t => t.Track);
            bool same = Tracks.Count == _song.Tracks.Count && Tracks.Select(t => t.Track).SequenceEqual(_song.Tracks);
            if (!same)
            {
                foreach (var t in Tracks) t.PropertyChanged -= OnTrackChanged;
                Tracks.Clear();
                foreach (var track in _song.Tracks)
                {
                    if (!byTrack.TryGetValue(track, out var vm))
                        vm = new TrackViewModel(track, NoteSkins.TrackColors[track.Index % NoteSkins.TrackColors.Length])
                        { OutputDevice = _fallbackPort ?? _outputs.DefaultName };
                    vm.PropertyChanged += OnTrackChanged;
                    Tracks.Add(vm);
                }
                _coEdited.RemoveWhere(t => !Tracks.Contains(t));
                if (_activeVm != null && !Tracks.Contains(_activeVm)) { _activeVm.IsActive = false; _activeVm = null; }
                if (_activeVm == null) SetActiveTrack(Tracks.FirstOrDefault());
            }

            TrackViewModel.UpdateAudible(Tracks);
            foreach (var t in Tracks)
            {
                _player.SetTrackOutput(t.Index, _outputs.Get(t.OutputDevice));
                _player.SetTrackAudible(t.Index, t.IsAudible);
                PushMix(t);
            }
            UpdateActiveTrackLabel();
            SetAllOutputsSelection();
        }

        /// <summary>
        /// Zoom so that notes are comfortable to read: rows of 9–16 px fitted to the notes in use,
        /// and about 32 px per beat (or the whole song, if it is shorter than the view).
        /// </summary>
        private void ApplyDefaultZoom()
        {
            if (_song == null) return;
            var size = viewer.ViewportSize;
            var notes = _song.Tracks.SelectMany(t => t.Notes).ToList();
            int low = notes.Count > 0 ? notes.Min(n => n.Number) : 48;
            int high = notes.Count > 0 ? notes.Max(n => n.Number) : 72;

            int rows = high - low + 1 + 6;
            double rowHeight = size.Height > 0 ? Math.Clamp(size.Height / rows, 9, 16) : 12;
            double pixelsPerTick = 32.0 / _song.TicksPerBeat;

            viewer.SetView(pixelsPerTick, 0, rowHeight, double.NaN);
            viewer.ScrollToY(MaxNote - (low + high) / 2.0, 0.5);
        }

        // --- Notes ---

        private void BuildElements()
        {
            if (_noteViewPaused)
            {
                _elementsDirty = true;   // rebuilt when the view resumes
                return;
            }
            _elements.Clear();
            if (_song == null)
            {
                viewer.InvalidateElements();
                return;
            }

            // One bucket per row keeps the list sorted by Y without a sort; within a row, later tracks
            // draw on top, and the edited track is drawn last so it is always on top.
            var rows = _rowBuckets;
            foreach (var row in rows) row.Clear();
            bool dim = tglDimOthers.IsChecked == true;

            void Add(SongNote note, NinePatchWpf.NinePatchImage skin) =>
                rows[MaxNote - Math.Clamp(note.Number, 0, MaxNote)].Add(new NinePatchElement
                {
                    Source = skin,
                    X = (int)note.Start,
                    Y = MaxNote - note.Number,
                    Width = (int)Math.Max(1, note.End - note.Start),
                    Height = 1,
                });

            foreach (var vm in Tracks)
            {
                if (vm == _activeVm || _coEdited.Contains(vm) || !vm.ShowNotes) continue;
                foreach (var note in vm.Track.Notes)
                {
                    var color = NoteColor(vm, note.Velocity);
                    Add(note, _skins.Get(dim && _activeVm != null ? NoteSkins.Faded(color) : color));
                }
            }

            if (_activeVm is { } active)
            {
                var hidden = _editor.Hidden;
                var selection = _editor.Selection;
                // Tracks edited together with the active one: drawn like it, below it.
                foreach (var co in Tracks)
                {
                    if (co == active || !_coEdited.Contains(co)) continue;
                    foreach (var note in co.Track.Notes)
                    {
                        if (hidden.Count > 0 && hidden.Contains(note)) continue;
                        Add(note, _skins.Get(NoteColor(co, note.Velocity), selection.Contains(note)));
                    }
                }
                foreach (var note in active.Track.Notes)
                {
                    if (hidden.Count > 0 && hidden.Contains(note)) continue;
                    Add(note, _skins.Get(NoteColor(active, note.Velocity), selection.Contains(note)));
                }
                foreach (var note in _editor.Preview)
                    Add(note, _skins.Get(NoteColor(active, note.Velocity), selected: true));
            }

            foreach (var row in rows) _elements.AddRange(row);
            viewer.InvalidateElements();
            lane.InvalidateVisual();
            fingerOverlay.InvalidateVisual();
            RefreshPedals();
            RefreshChords(force: false);
        }

        private readonly List<NinePatchElement>[] _rowBuckets = Enumerable.Range(0, MaxNote + 1).Select(_ => new List<NinePatchElement>()).ToArray();

        /// <summary>Colour of a note in the piano roll; the keyboard uses the same colour for the key.</summary>
        private Color NoteColor(TrackViewModel vm, int velocity)
        {
            var color = vm.IsAudible ? vm.Color : NoteSkins.Dimmed(vm.Color);
            return _velocityColors && vm.IsAudible ? NoteSkins.ByVelocity(color, velocity) : color;
        }

        private void OnColorModeChanged(object sender, SelectionChangedEventArgs e)
        {
            _velocityColors = cmbColorMode.SelectedIndex == 1;
            _keyboardSignature = 0;
            if (_song != null) BuildElements();
        }

        // --- Tracks ---

        private void OnTrackChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not TrackViewModel vm) return;
            switch (e.PropertyName)
            {
                case nameof(TrackViewModel.IsMuted) when !_batchTrackChanges:
                case nameof(TrackViewModel.IsSolo) when !_batchTrackChanges:
                    ApplyMuteSolo();
                    break;
                case nameof(TrackViewModel.ShowNotes) when _batchTrackChanges:
                    break;
                case nameof(TrackViewModel.ShowNotes):
                    BuildElements();
                    UpdateTrackSummary();
                    break;
                case nameof(TrackViewModel.OutputDevice):
                    var device = _outputs.Get(vm.OutputDevice);
                    _player.SetTrackOutput(vm.Index, device);
                    if (device == null && vm.OutputDevice != null) txtStatus.Text = _outputs.LastError;
                    SetAllOutputsSelection();
                    break;
            }
        }

        private void ApplyMuteSolo()
        {
            TrackViewModel.UpdateAudible(Tracks);
            foreach (var t in Tracks) _player.SetTrackAudible(t.Index, t.IsAudible);
            BuildElements();
            UpdateTrackSummary();
            _keyboardSignature = 0;
        }

        // Right click on a track's M / S / eye button: make that track the only one with the button on.
        // Right-clicking it again when it already is the only one turns the button back for every track.
        internal void OnMuteRightClick(object sender, MouseButtonEventArgs e) =>
            SetExclusive(sender, e, t => t.IsMuted, (t, v) => t.IsMuted = v, restore: false);

        internal void OnSoloRightClick(object sender, MouseButtonEventArgs e) =>
            SetExclusive(sender, e, t => t.IsSolo, (t, v) => t.IsSolo = v, restore: false);

        internal void OnShowRightClick(object sender, MouseButtonEventArgs e) =>
            SetExclusive(sender, e, t => t.ShowNotes, (t, v) => t.ShowNotes = v, restore: true);

        /// <param name="restore">The value every track gets when the exclusive state is undone.</param>
        private void SetExclusive(object sender, MouseButtonEventArgs e, Func<TrackViewModel, bool> get,
                                  Action<TrackViewModel, bool> set, bool restore)
        {
            e.Handled = true;
            if ((sender as FrameworkElement)?.DataContext is not TrackViewModel vm) return;

            bool alreadyOnly = get(vm) && Tracks.All(t => t == vm || !get(t));
            _batchTrackChanges = true;
            try
            {
                foreach (var t in Tracks) set(t, alreadyOnly ? restore : t == vm);
            }
            finally
            {
                _batchTrackChanges = false;
            }
            ApplyMuteSolo();   // rebuilds the piano roll once instead of once per track
        }

        private void UpdateTrackSummary()
        {
            int muted = Tracks.Count(t => t.IsMuted), solo = Tracks.Count(t => t.IsSolo), hidden = Tracks.Count(t => !t.ShowNotes);
            var parts = new List<string> { Tracks.Count == 1 ? "1 track" : $"{Tracks.Count} tracks" };
            if (muted > 0) parts.Add($"{muted} muted");
            if (solo > 0) parts.Add($"{solo} solo");
            if (hidden > 0) parts.Add($"{hidden} hidden");
            txtTrackSummary.Text = string.Join("  ·  ", parts);
        }

        private void OnAllOutputsChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingAllOutputs || cmbAllOutputs.SelectedItem is not string port) return;
            if (Tracks.Count == 0) { _noSongPort = port; return; }   // used by the next song
            foreach (var t in Tracks) t.OutputDevice = port;
        }

        /// <summary>Show the common port in "All outputs", or nothing when tracks use different ports.</summary>
        private void SetAllOutputsSelection()
        {
            _updatingAllOutputs = true;
            if (Tracks.Count == 0)
                cmbAllOutputs.SelectedItem = NoSongPort;   // no song yet: the port the first song will use
            else
            {
                var ports = Tracks.Select(t => t.OutputDevice).Distinct().ToList();
                cmbAllOutputs.SelectedItem = ports.Count == 1 ? ports[0] : null;
            }
            _updatingAllOutputs = false;
        }

        private void OnRefreshOutputs(object sender, RoutedEventArgs e)
        {
            _outputs.Refresh();
            var keep = Tracks.Select(t => t.OutputDevice).ToList();
            OutputNames.Clear();
            foreach (var name in _outputs.Names) OutputNames.Add(name);
            for (int i = 0; i < Tracks.Count; i++)
                Tracks[i].OutputDevice = keep[i] != null && OutputNames.Contains(keep[i]!) ? keep[i] : _outputs.DefaultName;
            SetAllOutputsSelection();
            txtStatus.Text = OutputNames.Count == 0 ? "No MIDI output ports found." : $"{OutputNames.Count} MIDI output ports.";
        }

        // --- Transport ---

        private void OnPlay(object sender, RoutedEventArgs e) => Play();
        private void OnPause(object sender, RoutedEventArgs e) => Pause();

        internal void OnStop(object sender, RoutedEventArgs e)
        {
            _player.Stop();
            UpdateTransport();
            ShowPosition(force: true);
            if (btnFollow.IsChecked == true) viewer.ScrollToX(0);
        }

        internal void OnPanic(object sender, RoutedEventArgs e) => Panic();

        /// <summary>
        /// Pause playback and silence every channel of every MIDI output port, whether a track uses it or not.
        /// </summary>
        private void Panic()
        {
            _player.Panic();
            _auditionNote = -1;
            falling.LiveAllOff();

            int silenced = 0;
            foreach (var name in _outputs.Names)
            {
                var device = _outputs.Get(name);
                if (device == null) continue;
                MidiPlayer.SilencePort(device);
                silenced++;
            }

            UpdateTransport();
            ShowPosition(force: true);
            btnPanic.IsChecked = false;
            txtStatus.Text = silenced == _outputs.Names.Count
                ? $"Panic: all notes off on {silenced} MIDI output port{(silenced == 1 ? "" : "s")}."
                : $"Panic: all notes off on {silenced} of {_outputs.Names.Count} ports. {_outputs.LastError}";
        }

        internal void OnRewind(object sender, RoutedEventArgs e)
        {
            SeekTo(0);
            viewer.ScrollToX(0);
            UpdateTransport();
        }

        internal void Play()
        {
            if (_song == null) return;
            if (OutputNames.Count == 0)
                txtStatus.Text = "No MIDI output port: the song plays silently. Install a synth or a virtual MIDI port and press ↻.";
            _player.Play();
            UpdateTransport();
        }

        internal void Pause()
        {
            _player.Pause();
            UpdateTransport();
        }

        private void TogglePlay()
        {
            if (_player.State == PlayerState.Playing) Pause();
            else Play();
        }

        internal void SeekTo(double tick)
        {
            if (_song == null) return;
            _player.Seek(tick);
            ShowPosition(force: true);
        }

        private void UpdateTransport()
        {
            var state = _player.State;
            bool loaded = _song != null;
            btnPlay.IsChecked = state == PlayerState.Playing;
            btnPause.IsChecked = state == PlayerState.Paused;
            btnStop.IsChecked = false;
            btnRewind.IsChecked = false;
            btnPlay.IsEnabled = btnPause.IsEnabled = btnStop.IsEnabled = btnRewind.IsEnabled = loaded;
        }

        private void OnSpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            double percent = Math.Round(e.NewValue / 5) * 5;
            if (txtSpeed == null) return;   // during InitializeComponent
            _player.Speed = percent / 100.0;
            txtSpeed.Text = $"{percent:0} %";
            ShowPosition(force: true);
        }

        private void OnSpeedReset(object sender, MouseButtonEventArgs e) => sldSpeed.Value = 100;

        // --- Every frame: playhead, displays, meters, keys, follow ---

        private void OnFrame(object? sender, EventArgs e)
        {
            RecordFrame();
            PracticeFrame();
            RefreshFalling();
            if (_song == null)
            {
                // No song: still light the key clicked on the keyboard panel.
                UpdateKeyboard();
                return;
            }
            bool playing = _player.State == PlayerState.Playing;
            UpdateNoteViewPause();

            if (playing)
            {
                ShowPosition(force: false);
                if (btnFollow.IsChecked == true && !_noteViewPaused) Follow(_player.PositionTicks);
            }

            for (int i = 0; i < Tracks.Count; i++)
            {
                var t = Tracks[i];
                double peak = _player.TakePeak(i) / 127.0;
                double level = Math.Max(peak, t.Level * 0.86);
                t.Level = level < 0.01 ? 0 : level;
            }
            _mixerWindow?.UpdateFrame();
            if (_noteViewPaused) return;
            UpdateKeyboard();
            UpdateWheels();
            UpdatePedals();
            UpdateChordStrip();
            pianoKeys.RefreshKeys();
        }

        /// <summary>
        /// Page-turn follow: the notes stay still and the playhead moves across them. When the
        /// line is about to leave the view, the view jumps one page so the line starts again near the left.
        /// </summary>
        private void Follow(double tick)
        {
            double x = viewer.WorldToViewX(tick);
            double width = viewer.ViewportSize.Width;
            if (width <= 0) return;
            double edge = Math.Max(6, width * 0.015);
            if (x < 0 || x >= width - edge) viewer.ScrollToX(tick, 0.02);
        }

        // --- Keyboard panel ---

        /// <summary>Light the keys that sound, in their piano-roll colour, with hand and finger.</summary>
        private void UpdateKeyboard()
        {
            double tick = _player.PositionTicks;
            Array.Clear(pianoKeys.KeyColors);
            long signature = 17;
            List<KeyHighlight>? keys = null;

            foreach (var vm in Tracks)
            {
                if (!vm.ShowNotes) continue;
                for (int n = 0; n < 128; n++)
                {
                    if (!_player.IsKeyDown(vm.Index, n)) continue;
                    int index = vm.Track.FindNote(n, tick);
                    var note = index >= 0 ? vm.Track.Notes[index] : null;
                    var finger = index >= 0 ? vm.Track.Fingering[index] : default;
                    var color = NoteColor(vm, note?.Velocity ?? 100);
                    pianoKeys.KeyColors[n] ??= color;
                    keys ??= new List<KeyHighlight>();
                    keys.Add(new KeyHighlight(n, color, 1.0, (int)finger.Hand, finger.Finger));
                    signature = signature * 31 + (vm.Index * 128 + n) * 8 + finger.Finger;
                }
            }

            if (_auditionNote >= 0)
            {
                keys ??= new List<KeyHighlight>();
                keys.Add(new KeyHighlight(_auditionNote, ((SolidColorBrush)FindResource("Accent")).Color, 1.0));
                signature = signature * 31 + 100_000 + _auditionNote;
            }

            // Keys held on the MIDI input.
            foreach (int n in _inputNotes)
            {
                keys ??= new List<KeyHighlight>();
                keys.Add(new KeyHighlight(n, ((SolidColorBrush)FindResource("Accent")).Color, 1.0));
                signature = signature * 31 + 200_000 + n;
            }

            ShowLiveChord(keys?.Select(k => k.Note) ?? Enumerable.Empty<int>());
            if (_practiceMode) signature = AddPracticeKeys(ref keys, signature);

            if (signature == _keyboardSignature || tglKeyboard.IsChecked != true) return;
            _keyboardSignature = signature;
            keyboard.SetState(keys ?? (IEnumerable<KeyHighlight>)Array.Empty<KeyHighlight>());
        }

        private void OnKeyboardToggled(object sender, RoutedEventArgs e)
        {
            if (rowKeyboard == null || keyboard == null || splitKeyboard == null) return;   // during InitializeComponent
            bool open = tglKeyboard.IsChecked == true;
            if (open)
            {
                rowKeyboard.Height = _keyboardHeight;
                rowKeyboard.MinHeight = 80;
            }
            else
            {
                _keyboardHeight = rowKeyboard.Height;
                rowKeyboard.MinHeight = 0;
                rowKeyboard.Height = GridLength.Auto;
            }
            keyboard.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (wheels != null) wheels.Visibility = keyboard.Visibility;
            UpdatePedalPanelVisibility();
            splitKeyboard.Visibility = keyboard.Visibility;
            _keyboardSignature = 0;
        }

        private void OnKeyLabelsChanged(object sender, SelectionChangedEventArgs e)
        {
            if (keyboard == null) return;
            keyboard.LabelMode = cmbKeyLabels.SelectedIndex switch { 1 => KeyLabelMode.Notes, 2 => KeyLabelMode.None, _ => KeyLabelMode.Fingers };
        }

        private void OnKeyRangeChanged(object sender, SelectionChangedEventArgs e) => ApplyKeyRange();

        private void ApplyKeyRange()
        {
            if (keyboard == null) return;
            switch (cmbKeyRange.SelectedIndex)
            {
                case 1 when _song != null && _song.NoteCount > 0:
                    // The song's notes, widened to whole octaves (at least two).
                    var notes = _song.Tracks.Where(t => !t.IsDrums).SelectMany(t => t.Notes).ToList();
                    if (notes.Count == 0) notes = _song.Tracks.SelectMany(t => t.Notes).ToList();
                    int low = notes.Min(n => n.Number) / 12 * 12;
                    int high = notes.Max(n => n.Number) / 12 * 12 + 11;
                    if (high - low < 23) high = low + 23;
                    keyboard.SetRange(Math.Max(0, low), Math.Min(127, high));
                    break;
                case 2: keyboard.SetRange(36, 96); break;
                case 3: keyboard.SetRange(36, 84); break;
                default: keyboard.SetRange(21, 108); break;
            }
        }

        // Clicking the keyboard plays the note on the edited track's port and channel.
        private void OnAuditionDown(int note)
        {
            PracticePress(note);
            FallingLiveNote(note, down: true, _velocity);
            OnAuditionUp(_auditionNote);
            var vm = _activeVm ?? Tracks.FirstOrDefault();
            var device = _outputs.Get(vm?.OutputDevice ?? NoSongPort);
            int channel = vm?.Track.Channel ?? 0;
            EnsureKeyboardSound();
            try { device?.SendEvent(new NoteOnEvent((SevenBitNumber)note, (SevenBitNumber)_velocity) { Channel = (FourBitNumber)channel }); }
            catch (MidiDeviceException) { }
            _auditionNote = note;
            _auditionChannel = channel;
        }

        private int _auditionChannel;

        private void OnAuditionUp(int note)
        {
            if (note >= 0) FallingLiveNote(note, down: false);
            if (note < 0 || note != _auditionNote) return;
            var vm = _activeVm ?? Tracks.FirstOrDefault();
            var device = _outputs.Get(vm?.OutputDevice ?? NoSongPort);
            try { device?.SendEvent(new NoteOffEvent((SevenBitNumber)note, (SevenBitNumber)0) { Channel = (FourBitNumber)_auditionChannel }); }
            catch (MidiDeviceException) { }
            _auditionNote = -1;
        }

        private void ShowPosition(bool force)
        {
            if (_song == null) return;
            double tick = _player.PositionTicks;
            long whole = (long)tick;

            if (!_noteViewPaused) UpdatePlayhead(tick);
            if (!force && whole == _lastShownTick) return;
            _lastShownTick = whole;

            txtTime.Text = Song.FormatTime(_player.Position);
            txtBars.Text = _song.FormatBars(whole);
            txtTempo.Text = (_song.Timeline.BpmAt(tick, _song.TicksPerQuarter) * _player.Speed).ToString("0.0", CultureInfo.InvariantCulture);
        }

        private void UpdatePlayhead(double tick)
        {
            double ppd = VisualTreeHelper.GetDpi(viewer).PixelsPerDip;
            double x = PianoRollStyle.Snap(viewer.WorldToViewX(tick), ppd) + 0.5 / ppd;
            playheadLine.X1 = playheadLine.X2 = x;
            playheadGlow.X1 = playheadGlow.X2 = x;
            ruler.PlayheadTick = (long)tick;
            ruler.InvalidateVisual();
        }

        private void OnViewChanged()
        {
            UpdatePlayhead(_player.PositionTicks);
            UpdateLoopShade();
            pianoKeys.InvalidateVisual();
            fingerOverlay.InvalidateVisual();
            pedalOverlay.InvalidateVisual();
            chordOverlay.InvalidateVisual();
        }

        private void ShowRenderInfo()
        {
            double pixelsPerBeat = viewer.ZoomX * (_song?.TicksPerBeat ?? 480);
            txtRender.Text = string.Create(CultureInfo.InvariantCulture,
                $"{viewer.LastVisibleCount}/{viewer.LastTotalCount} notes  {viewer.LastRenderTime.TotalMilliseconds:0.00} ms"
                + $"  ·  zoom {pixelsPerBeat:0} px/beat × {viewer.ZoomY:0.#} px/key");
        }

        private void OnFit(object sender, RoutedEventArgs e) => viewer.SetView(0, 0, double.NaN, double.NaN);

        /// <summary>Open the track lanes window, or bring it to the front if it is already open.</summary>
        private void OnOpenLanes(object sender, RoutedEventArgs e)
        {
            if (_tracksWindow == null)
            {
                _tracksWindow = new TracksWindow(this);
                _tracksWindow.Closed += (_, _) => _tracksWindow = null;
                _tracksWindow.Loaded += (s, _) => StyleScrollBars((DependencyObject)s!);
                _tracksWindow.Show();
            }
            else
            {
                if (_tracksWindow.WindowState == WindowState.Minimized) _tracksWindow.WindowState = WindowState.Normal;
                _tracksWindow.Activate();
            }
        }

        // --- Keyboard ---

        internal void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox) return;
            if (Keyboard.FocusedElement is ComboBoxItem || Keyboard.FocusedElement is ComboBox { IsDropDownOpen: true }) return;
            if (e.Key == Key.Escape && CancelAudioExport())
            {
                e.Handled = true;
                return;
            }
            if (_practiceMode ? HandlePracticeKey(e) : HandleEditKey(e))
            {
                e.Handled = true;
                return;
            }
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

            switch (e.Key)
            {
                case Key.Space:
                    TogglePlay();
                    break;
                case Key.Home:
                    OnRewind(this, e);
                    break;
                case Key.Escape:
                    Panic();
                    break;
                case Key.F9:
                    SetPracticeMode(!_practiceMode);
                    break;
                case Key.O when ctrl:
                    ShowOpenDialog();
                    break;
                case Key.F when !ctrl:
                    btnFollow.IsChecked = btnFollow.IsChecked != true;
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        // --- Window ---

        private void OnClosed(object? sender, EventArgs e)
        {
            CompositionTarget.Rendering -= OnFrame;
            StopAudition();
            _player.Dispose();
            _outputs.Dispose();
            viewer.Elements = null;
            _skins.Dispose();
        }


        /// <summary>Title bar to match the theme.</summary>
        private void UseDarkTitleBar() => ThemeManager.TitleBar(this);
    }
}
