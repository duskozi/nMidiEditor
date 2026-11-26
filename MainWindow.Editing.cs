using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Microsoft.Win32;
using nMIDI.Model;
using nMIDI.PianoRoll;
using nMIDI.Theory;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MidiDeviceException = Melanchall.DryWetMidi.Multimedia.MidiDeviceException;

namespace nMIDI
{
    /// <summary>
    /// The editing half of the main window: the active track, the piano roll tools, undo and redo,
    /// clipboard commands, keyboard shortcuts, and saving.
    /// </summary>
    public partial class MainWindow : IRollHost
    {
        /// <summary>A grid or note length choice: a fraction of a quarter note, or a whole bar.</summary>
        private sealed record GridChoice(string Label, double Quarters, bool IsBar = false, bool IsLast = false, bool IsGrid = false)
        {
            public override string ToString() => Label;
        }

        private static readonly GridChoice[] GridChoices =
        [
            new("Bar", 0, IsBar: true),
            new("1/1", 4), new("1/2", 2), new("1/4", 1), new("1/8", 0.5), new("1/16", 0.25), new("1/32", 0.125), new("1/64", 0.0625),
            new("1/4 triplet", 2 / 3.0), new("1/8 triplet", 1 / 3.0), new("1/16 triplet", 1 / 6.0), new("1/32 triplet", 1 / 12.0),
            new("1/2 dotted", 3), new("1/4 dotted", 1.5), new("1/8 dotted", 0.75), new("1/16 dotted", 0.375),
            new("Off", 0),
        ];

        private static readonly GridChoice[] LengthChoices =
        [
            new("Last", 0, IsLast: true), new("Grid", 0, IsGrid: true),
            new("1/1", 4), new("1/2", 2), new("1/4", 1), new("1/8", 0.5), new("1/16", 0.25), new("1/32", 0.125), new("1/64", 0.0625),
            new("1/4 triplet", 2 / 3.0), new("1/8 triplet", 1 / 3.0), new("1/16 triplet", 1 / 6.0),
            new("1/2 dotted", 3), new("1/4 dotted", 1.5), new("1/8 dotted", 0.75), new("1/16 dotted", 0.375),
        ];

        /// <summary>Notes on the clipboard, with starts relative to the first one.</summary>
        private static List<SongNote> _clipboard = new();

        private readonly EditHistory _history = new();
        private RollEditor _editor = null!;
        private TrackViewModel? _activeVm;
        private long _lastLength = 480;
        private int _velocity = 100;
        private readonly DispatcherTimer _auditionTimer = new() { Interval = TimeSpan.FromMilliseconds(320) };
        private (Melanchall.DryWetMidi.Multimedia.OutputDevice Device, int Note, int Channel)? _sounding;

        private void InitEditing()
        {
            _editor = new RollEditor(this);
            foreach (var g in GridChoices) cmbGrid.Items.Add(g);
            foreach (var g in LengthChoices) cmbLength.Items.Add(g);
            cmbGrid.SelectedItem = GridChoices.First(g => g.Label == "1/16");
            cmbLength.SelectedIndex = 0;

            InitProcess();
            InitSong();
            InitRecord();
            _auditionTimer.Tick += (_, _) => StopAudition();
            _history.Changed += (_, _) => UpdateEditState();
            Closing += OnClosing;
            UpdateEditState();
        }

        /// <summary>Hook the editor to the viewer once its template (and the drawing surface) exists.</summary>
        private void AttachEditor()
        {
            viewer.ApplyTemplate();
            if (viewer.Template?.FindName("PART_Surface", viewer) is FrameworkElement surface)
            {
                _editor.Attach(viewer, surface, rubberBand);
                AttachLane(surface);
            }
            pianoKeys.KeyClicked += OnPianoKeyClicked;
        }

        // --- IRollHost ---

        Song? IRollHost.Song => _song;

        public SongTrack? ActiveTrack => _activeVm?.Track;

        public IEnumerable<SongTrack> ShownTracks => Tracks.Where(t => t.ShowNotes).Select(t => t.Track);

        /// <summary>Tracks edited together with the active one (Ctrl+click in the track list).</summary>
        private readonly HashSet<TrackViewModel> _coEdited = new();

        /// <summary>Every edited track: the active one first, then the others in track order.</summary>
        public IReadOnlyList<SongTrack> EditTracks
        {
            get
            {
                if (_activeVm == null) return Array.Empty<SongTrack>();
                if (_coEdited.Count == 0) return new[] { _activeVm.Track };
                var list = new List<SongTrack> { _activeVm.Track };
                list.AddRange(Tracks.Where(t => t != _activeVm && _coEdited.Contains(t)).Select(t => t.Track));
                return list;
            }
        }

        /// <summary>Ctrl+click on a track row: edit it together with the active track, or stop.</summary>
        private void ToggleCoEdit(TrackViewModel vm)
        {
            if (_activeVm == null) { SetActiveTrack(vm); return; }
            if (vm == _activeVm)
            {
                // The active track can't leave; the next edited track takes over.
                var next = Tracks.FirstOrDefault(t => _coEdited.Contains(t));
                if (next == null) return;
                _coEdited.Remove(next);
                next.IsCoEdited = false;
                var keep = _editor.Selection.ToList();
                SetActiveTrack(next, keepCoEdited: true);
                _editor.SetSelection(keep);
                _editor.ValidateSelection();
            }
            else if (_coEdited.Remove(vm))
            {
                vm.IsCoEdited = false;
                _editor.ValidateSelection();
            }
            else
            {
                _coEdited.Add(vm);
                vm.IsCoEdited = true;
            }
            UpdateActiveTrackLabel();
            UpdateSelectionInfo();
            UpdateFingerOverlay();
            BuildElements();
        }

        private void ClearCoEdited()
        {
            foreach (var vm in _coEdited) vm.IsCoEdited = false;
            _coEdited.Clear();
        }

        private void UpdateActiveTrackLabel()
        {
            var vm = _activeVm;
            txtActiveTrack.Text = vm == null ? "No track" : _coEdited.Count == 0 ? $"{vm.Number}. {vm.Name}" : $"{vm.Number}. {vm.Name} + {_coEdited.Count}";
            bdActiveTrack.ToolTip = vm == null || _coEdited.Count == 0
                ? "The track being edited. Click a track in the list, or a note of another track, to edit that track. Ctrl+click tracks to edit several."
                : "Edited tracks: " + string.Join(", ", EditTracks.Select(t => $"{t.Index + 1}. {t.Name}")) + ". New notes go into the first one.";
        }

        public long SnapTicks => cmbGrid.SelectedItem is GridChoice g ? ChoiceTicks(g) : 0;

        public long NewNoteLength => PianoRollStyle.Rows.IsDrums ? DrumHitLength : cmbLength.SelectedItem switch
        {
            GridChoice { IsLast: true } => _lastLength,
            GridChoice { IsGrid: true } => Math.Max(1, SnapTicks > 0 ? SnapTicks : _lastLength),
            GridChoice g => ChoiceTicks(g),
            _ => _lastLength,
        };

        public int NewVelocity => _velocity;

        public void RememberNoteLength(long ticks)
        {
            if (PianoRollStyle.Rows.IsDrums) return;   // drum hits have a fixed length; keep the length for other tracks
            _lastLength = Math.Max(1, ticks);
        }

        private long ChoiceTicks(GridChoice g)
        {
            long tpq = _song?.TicksPerQuarter ?? 480;
            if (g.IsBar) return _song?.TicksPerBar ?? tpq * 4;
            return g.Quarters <= 0 ? 0 : Math.Max(1, (long)Math.Round(tpq * g.Quarters));
        }

        public void Commit(string label, Action change)
        {
            if (_song == null) return;
            if (_recordPhase != RecordPhase.Off) StopRecording();   // a take ends before any other edit
            _history.Record(label);
            change();
            AfterEdit(label);
        }

        /// <summary>Everything that follows an edit, an undo or a redo: new playback data, views and title.</summary>
        private void AfterEdit(string? label, bool tracksChanged = false)
        {
            if (_song == null) return;
            _song.Rebuild();
            _player.UpdateSong(_song);
            if (tracksChanged) SyncTrackList();
            foreach (var vm in Tracks) vm.Refresh();
            _editor.ValidateSelection();

            // Room to keep drawing after the last note; the view never shrinks under the user.
            double needed = _song.LengthTicks + _song.TicksPerBar * 8;
            if (needed > viewer.ContentWidth) viewer.ContentWidth = needed;
            _tracksWindow?.UpdateLength(viewer.ContentWidth);

            BuildElements();
            UpdateFingerOverlay();
            FillLaneList();
            UpdateRuler();
            UpdateFileInfo();
            UpdateSelectionInfo();
            _eventList?.Refresh();
            UpdateTrackSummary();
            ShowPosition(force: true);
            if (label != null) txtStatus.Text = label;
        }

        public void ActivateTrack(SongTrack track)
        {
            var vm = Tracks.FirstOrDefault(t => t.Track == track);
            if (vm != null) SetActiveTrack(vm);
        }

        public void RefreshNotes() => BuildElements();

        public void SelectionChanged()
        {
            UpdateSelectionInfo();
            // A single clicked note lends its velocity to the next drawn notes.
            if (_editor.Selection.Count == 1 && !txtVelocity.IsKeyboardFocused)
            {
                _velocity = _editor.Selection.First().Velocity;
                txtVelocity.Text = _velocity.ToString(CultureInfo.InvariantCulture);
            }
        }

        // --- Active track ---

        private void SetActiveTrack(TrackViewModel? vm, bool keepCoEdited = false)
        {
            if (vm == _activeVm) return;
            if (!keepCoEdited)
            {
                // Clicking a track that is already edited with the others keeps the group, with it in front.
                if (vm != null && _coEdited.Remove(vm))
                {
                    vm.IsCoEdited = false;
                    if (_activeVm != null) { _coEdited.Add(_activeVm); _activeVm.IsCoEdited = true; }
                    keepCoEdited = true;
                }
                else ClearCoEdited();
            }
            if (_activeVm != null) _activeVm.IsActive = false;
            _activeVm = vm;
            if (vm != null) vm.IsActive = true;
            _editor.Selection.Clear();

            UpdateActiveTrackLabel();
            UpdateFingerOverlay();
            dotActiveTrack.Fill = vm?.Brush ?? (Brush)FindResource("TextFaint");
            FillLaneList();
            UpdateSelectionInfo();
            BuildElements();
            _eventList?.Refresh();
        }

        private void OnTrackRowClick(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not TrackViewModel vm) return;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { ToggleCoEdit(vm); return; }
            SetActiveTrack(vm);
            if (e.ClickCount == 2) ShowTrackSettings(vm);   // double-click: rename, channel, instrument...
        }

        // --- Toolbar ---

        private void OnToolClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is string tag && Enum.TryParse<EditTool>(tag, out var tool)) SetTool(tool);
        }

        private void SetTool(EditTool tool)
        {
            _editor.CancelDrag();
            _editor.Tool = tool;
            tglSelect.IsChecked = tool == EditTool.Select;
            tglDraw.IsChecked = tool == EditTool.Draw;
            tglErase.IsChecked = tool == EditTool.Erase;
            tglSplit.IsChecked = tool == EditTool.Split;
        }

        private void OnGridChanged(object sender, SelectionChangedEventArgs e) => viewer.InvalidateElements();

        private void OnVelocityCommitted(object sender, RoutedEventArgs e) => ReadVelocity();

        private void OnVelocityKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                ReadVelocity();
                viewer.Focus();
                e.Handled = true;
            }
        }

        private void ReadVelocity()
        {
            if (int.TryParse(txtVelocity.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) _velocity = Math.Clamp(v, 1, 127);
            txtVelocity.Text = _velocity.ToString(CultureInfo.InvariantCulture);
        }

        private void OnDimOthersClick(object sender, RoutedEventArgs e) => BuildElements();

        private void OnDrumViewClick(object sender, RoutedEventArgs e)
        {
            _settings.DrumView = tglDrumView.IsChecked == true;
            _settings.Save();
            BuildElements();
            if (_activeVm?.Track.IsDrums != true)
                txtStatus.Text = _settings.DrumView ? "Drum view: shown when a drum track (channel 10) is edited." : "Drum view off.";
        }

        private void OnUndo(object sender, RoutedEventArgs e) => Undo();
        private void OnRedo(object sender, RoutedEventArgs e) => Redo();

        private void Undo()
        {
            _editor.CancelDrag();
            if (_recordPhase != RecordPhase.Off) StopRecording();
            if (_history.Undo() is { } label) AfterEdit("Undo: " + label, tracksChanged: true);
        }

        private void Redo()
        {
            _editor.CancelDrag();
            if (_recordPhase != RecordPhase.Off) StopRecording();
            if (_history.Redo() is { } label) AfterEdit("Redo: " + label, tracksChanged: true);
        }

        /// <summary>Enable or disable the edit buttons and show the unsaved mark in the title.</summary>
        private void UpdateEditState()
        {
            bool loaded = _song != null;
            pnlEdit.IsEnabled = loaded;
            btnSave.IsEnabled = btnSaveAs.IsEnabled = loaded;
            btnUndo.IsEnabled = _history.CanUndo;
            btnRedo.IsEnabled = _history.CanRedo;
            btnUndo.ToolTip = _history.UndoLabel is { } u ? $"Undo {u} (Ctrl+Z)" : "Undo (Ctrl+Z)";
            btnRedo.ToolTip = _history.RedoLabel is { } r ? $"Redo {r} (Ctrl+Y)" : "Redo (Ctrl+Y)";

            string name = _song?.Name ?? "";
            Title = _song == null ? "nMIDI Editor" : $"{(_history.IsDirty ? "● " : "")}{name} – nMIDI Editor";
            txtFileName.Text = _song == null ? "No file" : (_history.IsDirty ? name + " ●" : name);
        }

        private void UpdateSelectionInfo()
        {
            UpdateNoteFields();
            if (_song == null || _activeVm == null)
            {
                txtSelection.Text = "";
                return;
            }
            var notes = _editor.SelectedNotes();
            if (notes.Count == 0)
            {
                txtSelection.Text = $"{_activeVm.Track.Notes.Count:N0} notes in track  ·  nothing selected";
                return;
            }
            if (notes.Count == 1)
            {
                var n = notes[0];
                txtSelection.Text = $"{NoteName(n.Number)}  ·  velocity {n.Velocity}  ·  start {_song.FormatBars(n.Start).Trim()}"
                                    + $"  ·  length {n.Length} ticks  ·  ch {n.Channel + 1}";
                return;
            }
            int low = notes.Min(n => n.Number), high = notes.Max(n => n.Number);
            int vLow = notes.Min(n => n.Velocity), vHigh = notes.Max(n => n.Velocity);
            string velocities = vLow == vHigh ? $"{vLow}" : $"{vLow}–{vHigh}";
            string from = _song.FormatBars(notes.Min(n => n.Start)).Trim();
            txtSelection.Text = $"{notes.Count:N0} notes selected  ·  {NoteName(low)}–{NoteName(high)}  ·  velocity {velocities}  ·  from {from}";
        }

        private static string NoteName(int note) => $"{NoteHelper.Pretty(note)}{NoteHelper.Octave(note)}";

        // --- Audition ---

        public void Audition(int note, int velocity)
        {
            StopAudition();
            var vm = _activeVm ?? Tracks.FirstOrDefault();
            var device = _outputs.Get(vm?.OutputDevice ?? NoSongPort);
            if (device == null) return;
            int channel = vm?.Track.Channel ?? 0;
            try
            {
                var on = new NoteOnEvent((SevenBitNumber)Math.Clamp(note, 0, 127), (SevenBitNumber)Math.Clamp(velocity, 1, 127));
                on.Channel = (FourBitNumber)channel;
                device.SendEvent(on);
                _sounding = (device, note, channel);
                _auditionTimer.Start();
            }
            catch (MidiDeviceException) { }
        }

        private void StopAudition()
        {
            _auditionTimer.Stop();
            if (_sounding is not { } s) return;
            _sounding = null;
            try { s.Device.SendEvent(new NoteOffEvent((SevenBitNumber)s.Note, (SevenBitNumber)0) { Channel = (FourBitNumber)s.Channel }); }
            catch (MidiDeviceException) { }
        }

        /// <summary>A click on the keys beside the roll plays the note and selects every note of that key (Shift adds).</summary>
        private void OnPianoKeyClicked(int note, bool add)
        {
            Audition(note, _velocity);
            if (_activeVm == null) return;
            var notes = EditTracks.SelectMany(t => t.Notes).Where(n => n.Number == note);
            _editor.SetSelection(add ? _editor.Selection.Concat(notes) : notes);
        }

        // --- Commands ---

        private bool CanEdit => _song != null && _activeVm != null;

        private void SelectAll()
        {
            if (!CanEdit) return;
            _editor.SetSelection(EditTracks.SelectMany(t => t.Notes));
        }

        /// <summary>Select every note of the active track, on all its channels (not those of the tracks edited with it).</summary>
        private void SelectAllInTrack()
        {
            if (!CanEdit) return;
            var track = _activeVm!.Track;
            _editor.SetSelection(track.Notes);
            string name = string.IsNullOrWhiteSpace(track.Name) ? $"track {track.Index + 1}" : track.Name.Trim();
            txtStatus.Text = $"{_editor.Selection.Count:N0} notes selected in {name}.";
        }

        private void DeleteSelection(string label = "Delete notes")
        {
            var track = ActiveTrack;
            var notes = _editor.SelectedNotes();
            if (track == null || notes.Count == 0) return;
            var tracks = EditTracks;
            Commit(label, () => NoteOps.ReplaceInTracks(tracks, notes, Array.Empty<SongNote>(), null, track));
            _editor.ClearSelection();
        }

        private void CopySelection()
        {
            var notes = _editor.SelectedNotes();
            if (notes.Count == 0) return;
            long first = notes.Min(n => n.Start);
            _clipboard = notes.Select(n => n with { Start = n.Start - first, End = n.End - first }).ToList();
            txtStatus.Text = notes.Count == 1 ? "1 note copied." : $"{notes.Count} notes copied.";
        }

        private void CutSelection()
        {
            CopySelection();
            DeleteSelection("Cut notes");
        }

        /// <summary>Paste at the playhead (on the grid line at or before it), into the active track.</summary>
        private void Paste()
        {
            var track = ActiveTrack;
            if (track == null || _clipboard.Count == 0) return;
            long at = NoteOps.SnapDown(_player.PositionTicks, SnapTicks);
            var notes = _clipboard.Select(n => n with { Start = n.Start + at, End = n.End + at, Channel = track.Channel }).ToList();
            Commit(notes.Count == 1 ? "Paste note" : "Paste notes", () => track.Notes = NoteOps.Replace(track.Notes, Array.Empty<SongNote>(), notes));
            _editor.SetSelection(notes);
            // Paste again goes after this one.
            long end = notes.Max(n => n.End);
            if (end > _player.PositionTicks) SeekTo(NoteOps.Snap(end, SnapTicks));
        }

        /// <summary>Copy the selection to just after itself, rounded up to the grid.</summary>
        private void Duplicate()
        {
            var track = ActiveTrack;
            var notes = _editor.SelectedNotes();
            if (track == null || notes.Count == 0) return;
            long start = notes.Min(n => n.Start), end = notes.Max(n => n.End);
            long grid = SnapTicks > 0 ? SnapTicks : 1;
            long offset = Math.Max(grid, (long)Math.Ceiling((end - start) / (double)grid) * grid);
            var copies = NoteOps.Move(notes, offset, 0);
            var tracks = EditTracks;
            Commit("Duplicate notes", () => NoteOps.ReplaceInTracks(tracks, Array.Empty<SongNote>(), copies, notes, track));
            _editor.SetSelection(copies);
        }

        /// <summary>Replace the selected notes with a changed copy (move, transpose, length, velocity...).</summary>
        private void TransformSelection(string label, Func<List<SongNote>, List<SongNote>> transform)
        {
            var track = ActiveTrack;
            var notes = _editor.SelectedNotes();
            if (track == null || notes.Count == 0) return;
            var changed = transform(notes);
            if (changed.Count == notes.Count && changed.Zip(notes).All(x => x.First == x.Second)) return;   // nothing changes
            var tracks = EditTracks;
            Commit(label, () => NoteOps.ReplaceInTracks(tracks, notes, changed, changed.Count == notes.Count ? notes : null, track, moveLocks: true));
            _editor.SetSelection(changed);
        }

        private void Transpose(int semitones)
        {
            TransformSelection(semitones > 0 ? "Transpose up" : "Transpose down", notes => NoteOps.Move(notes, 0, semitones));
            if (_editor.Selection.Count == 1) Audition(_editor.Selection.First().Number, _editor.Selection.First().Velocity);
        }

        private long StepTicks => SnapTicks > 0 ? SnapTicks : Math.Max(1, (_song?.TicksPerQuarter ?? 480) / 4);

        /// <summary>Move the selection one grid step; with nothing selected, move the playhead instead.</summary>
        private void Nudge(int direction)
        {
            if (_editor.Selection.Count == 0)
            {
                double tick = _player.PositionTicks;
                long target = direction > 0 ? NoteOps.SnapDown(tick, StepTicks) + StepTicks
                                            : (long)Math.Ceiling(tick / StepTicks) * StepTicks - StepTicks;
                SeekTo(Math.Max(0, target));
                if (btnFollow.IsChecked == true) Follow(_player.PositionTicks);
                return;
            }
            TransformSelection(direction > 0 ? "Move right" : "Move left", notes => NoteOps.Move(notes, direction * StepTicks, 0));
        }

        private void ChangeLength(int direction) =>
            TransformSelection(direction > 0 ? "Lengthen notes" : "Shorten notes",
                notes => NoteOps.ResizeEnds(notes, direction * StepTicks, Math.Min(StepTicks, notes.Min(n => n.Length))));

        private void SetSelectionLength() =>
            TransformSelection("Set note length", notes => NoteOps.SetLength(notes, NewNoteLength));

        private void SetSelectionVelocity(Func<SongNote, int> velocity, string label) =>
            TransformSelection(label, notes => NoteOps.MapVelocity(notes, (n, _) => velocity(n)));

        private void SplitAtPlayhead()
        {
            long tick = (long)Math.Round(_player.PositionTicks);
            TransformSelection("Split notes", notes => NoteOps.Split(notes, tick));
        }

        // --- Context menu ---

        public void ShowContextMenu()
        {
            bool any = _editor.Selection.Count > 0;
            var menu = new ContextMenu();
            menu.Items.Add(Item("Cut", "Ctrl+X", any, CutSelection));
            menu.Items.Add(Item("Copy", "Ctrl+C", any, CopySelection));
            menu.Items.Add(Item("Paste at playhead", "Ctrl+V", _clipboard.Count > 0, Paste));
            menu.Items.Add(Item("Duplicate", "Ctrl+D", any, Duplicate));
            menu.Items.Add(Item("Delete", "Del", any, () => DeleteSelection()));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Select all", "Ctrl+A", true, SelectAll));
            menu.Items.Add(Item("Select all in track", "Ctrl+Shift+A", true, SelectAllInTrack));
            menu.Items.Add(Item("Select none", "Esc", any, _editor.ClearSelection));
            menu.Items.Add(new Separator());

            var transpose = new MenuItem { Header = "Transpose", IsEnabled = any };
            transpose.Items.Add(Item("Up a semitone", "↑", any, () => Transpose(1)));
            transpose.Items.Add(Item("Down a semitone", "↓", any, () => Transpose(-1)));
            transpose.Items.Add(Item("Up an octave", "Shift+↑", any, () => Transpose(12)));
            transpose.Items.Add(Item("Down an octave", "Shift+↓", any, () => Transpose(-12)));
            menu.Items.Add(transpose);

            var velocity = new MenuItem { Header = "Velocity", IsEnabled = any };
            velocity.Items.Add(Item($"Set to {_velocity}", "", any, () => SetSelectionVelocity(_ => _velocity, "Set velocity")));
            velocity.Items.Add(Item("Louder (+10 %)", "", any, () => SetSelectionVelocity(n => (int)Math.Round(n.Velocity * 1.1) + 1, "Louder")));
            velocity.Items.Add(Item("Softer (−10 %)", "", any, () => SetSelectionVelocity(n => (int)Math.Round(n.Velocity * 0.9), "Softer")));
            menu.Items.Add(velocity);

            menu.Items.Add(Item("Set length to the Length box", "", any, SetSelectionLength));
            menu.Items.Add(Item("Split at playhead", "", any, SplitAtPlayhead));
            var process = new MenuItem { Header = "Process" };
            foreach (var item in ProcessItems()) process.Items.Add(item);
            menu.Items.Add(process);
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(_history.UndoLabel is { } u ? $"Undo {u}" : "Undo", "Ctrl+Z", _history.CanUndo, Undo));
            menu.Items.Add(Item(_history.RedoLabel is { } r ? $"Redo {r}" : "Redo", "Ctrl+Y", _history.CanRedo, Redo));

            menu.PlacementTarget = viewer;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        private static MenuItem Item(string header, string gesture, bool enabled, Action action)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }

        // --- Keyboard ---

        /// <summary>Editing shortcuts. Returns true if the key was used.</summary>
        private bool HandleEditKey(KeyEventArgs e)
        {
            var mods = Keyboard.Modifiers;
            bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift), alt = mods.HasFlag(ModifierKeys.Alt);
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (HandleFingerKey(key, shift, ctrl)) return true;

            switch (key)
            {
                case Key.N when ctrl: NewSong(); return true;
                case Key.OemComma when ctrl: ShowSettings(); return true;
                case Key.S when ctrl && shift: SaveAs(); return true;
                case Key.S when ctrl: Save(); return true;
            }
            if (_song == null) return false;
            if (HandleRecordKey(key, ctrl, shift, alt)) return true;

            switch (key)
            {
                case Key.Z when ctrl && shift: Redo(); return true;
                case Key.Z when ctrl: Undo(); return true;
                case Key.Y when ctrl: Redo(); return true;
                case Key.A when ctrl && shift: SelectAllInTrack(); return true;
                case Key.A when ctrl: SelectAll(); return true;
                case Key.C when ctrl: CopySelection(); return true;
                case Key.X when ctrl: CutSelection(); return true;
                case Key.V when ctrl: Paste(); return true;
                case Key.D when ctrl: Duplicate(); return true;
                case Key.Q when !ctrl && !alt: ShowQuantize(); return true;
                case Key.L when !ctrl && !alt: ToggleLoop(); return true;
                case Key.T when ctrl && !shift: AddTrack(); return true;
                case Key.Delete:
                case Key.Back:
                    DeleteSelection();
                    return true;
                case Key.Up when !ctrl && !alt: Transpose(shift ? 12 : 1); return true;
                case Key.Down when !ctrl && !alt: Transpose(shift ? -12 : -1); return true;
                case Key.Left when ctrl: ChangeLength(-1); return true;
                case Key.Right when ctrl: ChangeLength(1); return true;
                case Key.Left when !alt: Nudge(-1); return true;
                case Key.Right when !alt: Nudge(1); return true;
                case Key.D1 or Key.NumPad1 when !ctrl: SetTool(EditTool.Select); return true;
                case Key.D2 or Key.NumPad2 when !ctrl: SetTool(EditTool.Draw); return true;
                case Key.D3 or Key.NumPad3 when !ctrl: SetTool(EditTool.Erase); return true;
                case Key.D4 or Key.NumPad4 when !ctrl: SetTool(EditTool.Split); return true;
                case Key.Escape when _editor.IsDragging:
                    _editor.CancelDrag();
                    return true;
                case Key.Escape when _editor.Selection.Count > 0:
                    _editor.ClearSelection();
                    return true;
            }
            return false;
        }

        // --- New, save, and unsaved changes ---

        private void OnNew(object sender, RoutedEventArgs e) => NewSong();
        private void OnSave(object sender, RoutedEventArgs e) => Save();
        private void OnSaveAs(object sender, RoutedEventArgs e) => SaveAs();

        private void NewSong()
        {
            if (!ConfirmDiscard()) return;
            ShowSong(NewSongFromSettings());
            txtStatus.Text = "New song: pick the draw tool (2) and click in the piano roll to add notes.";
        }

        /// <summary>Save to the song's file, or ask for one. Returns false if nothing was saved.</summary>
        private bool Save()
        {
            if (_song == null) return false;
            // A song that was never saved, or an RMID file (a RIFF wrapper this editor does not write), needs a new name.
            if (_song.Path == null || _song.Path.EndsWith(".rmi", StringComparison.OrdinalIgnoreCase)) return SaveAs();
            return SaveTo(_song.Path, _song.SourceFormat);
        }

        private bool SaveAs()
        {
            if (_song == null) return false;
            var dialog = new SaveFileDialog
            {
                Title = "Save MIDI file",
                Filter = "MIDI file, format 1: one track per part (*.mid)|*.mid|MIDI file, format 0: single track (*.mid)|*.mid",
                FilterIndex = _song.SourceFormat == 0 ? 2 : 1,
                FileName = System.IO.Path.ChangeExtension(_song.Name, ".mid"),
                DefaultExt = ".mid",
                AddExtension = true,
            };
            if (_song.Path != null) dialog.InitialDirectory = System.IO.Path.GetDirectoryName(_song.Path);
            if (dialog.ShowDialog(this) != true) return false;
            return SaveTo(dialog.FileName, dialog.FilterIndex == 2 ? 0 : 1);
        }

        private bool SaveTo(string path, int format)
        {
            if (_song == null) return false;
            try
            {
                Cursor = Cursors.Wait;
                _song.Save(path, format);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                MessageBox.Show(this, $"Could not save the file:\n{ex.Message}", "nMIDI Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            finally
            {
                Cursor = null;
            }
            _song.Path = System.IO.Path.GetFullPath(path);
            _song.SourceFormat = format;
            _history.MarkSaved();
            DeleteAutosave();
            RememberFile(_song.Path);
            txtStatus.Text = $"Saved {_song.Name} (format {format}).";
            return true;
        }

        /// <summary>Ask to save unsaved changes. Returns false if the user cancelled (or the save failed).</summary>
        private bool ConfirmDiscard()
        {
            if (_recordPhase != RecordPhase.Off) StopRecording();
            if (_song == null || !_history.IsDirty) return true;
            var answer = MessageBox.Show(this, $"Save the changes to {_song.Name}?", "nMIDI Editor",
                                         MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            return answer switch
            {
                MessageBoxResult.Yes => Save(),
                MessageBoxResult.No => true,
                _ => false,
            };
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (!ConfirmDiscard()) e.Cancel = true;
            else
            {
                DeleteAutosave();
                RememberLayout();
            }
        }
    }
}
