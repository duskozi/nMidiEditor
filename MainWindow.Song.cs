using Melanchall.DryWetMidi.Core;
using nMIDI.Model;
using nMIDI.PianoRoll;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace nMIDI
{
    public enum LaneRangeOp { Delete, Copy, Cut, Paste, Duplicate, Move }

    /// <summary>
    /// Song structure: track commands and settings, tempo, time and key signatures, markers,
    /// inserting and deleting bars, the loop range and range edits over all tracks.
    /// </summary>
    public partial class MainWindow
    {
        private long _loopStart, _loopEnd;
        private static RangeClip? _rangeClip;

        private static readonly string[] MajorKeys = ["C♭", "G♭", "D♭", "A♭", "E♭", "B♭", "F", "C", "G", "D", "A", "E", "B", "F♯", "C♯"];
        private static readonly string[] MinorKeys = ["A♭", "E♭", "B♭", "F", "C", "G", "D", "A", "E", "B", "F♯", "C♯", "G♯", "D♯", "A♯"];

        private sealed record KeySigChoice(int Sharps, bool Minor)
        {
            public override string ToString()
            {
                string name = (Minor ? MinorKeys : MajorKeys)[Sharps + 7];
                string accidentals = Sharps == 0 ? "no sharps or flats" : Sharps > 0 ? $"{Sharps}♯" : $"{-Sharps}♭";
                return $"{name} {(Minor ? "minor" : "major")}  ({accidentals})";
            }
        }

        private static readonly KeySigChoice[] KeySigChoices =
            Enumerable.Range(-7, 15).Select(s => new KeySigChoice(s, false)).Concat(Enumerable.Range(-7, 15).Select(s => new KeySigChoice(s, true))).ToArray();

        private void InitSong()
        {
            ruler.Snap = () => SnapTicks > 0 ? SnapTicks : _song?.TicksPerBeat ?? 480;
            ruler.LoopDragged += (_, range) => SetLoopRange(range.Start, range.End, enable: true);
            ruler.FlagClicked += (_, flag) => EditFlag(flag);
            ruler.MenuRequested += (_, request) => ShowRulerMenu(request.Tick, request.Flag);
        }

        /// <summary>Show the song's conductor events on the ruler and its bar lines in the grid.</summary>
        private void UpdateRuler()
        {
            if (_song == null)
            {
                ruler.Flags = Array.Empty<RulerFlag>();
                ruler.Bars = null;
                ruler.InvalidateVisual();
                return;
            }
            var flags = new List<RulerFlag>();
            foreach (var t in _song.Tempos)
                if (t.Tick > 0 || _song.Tempos.Count > 1) flags.Add(new RulerFlag(t.Tick, FlagKind.Tempo, $"♩{t.Bpm:0.#}", t));
            foreach (var s in _song.TimeSignatures)
                if (s.Tick > 0 || _song.TimeSignatures.Count > 1 || s.Numerator != 4 || s.Denominator != 4)
                    flags.Add(new RulerFlag(s.Tick, FlagKind.TimeSignature, $"{s.Numerator}/{s.Denominator}", s));
            foreach (var m in _song.Conductor)
            {
                if (m.Event is KeySignatureEvent k)
                {
                    bool minor = k.Scale == 1;
                    string key = (minor ? MinorKeys : MajorKeys)[Math.Clamp(k.Key + 7, 0, 14)];
                    flags.Add(new RulerFlag(m.Tick, FlagKind.KeySignature, minor ? key + "m" : key, m));
                }
                else if (m.Event is MarkerEvent marker)
                    flags.Add(new RulerFlag(m.Tick, FlagKind.Marker, marker.Text ?? "", m));
                else if (m.Event is CuePointEvent cue)
                    flags.Add(new RulerFlag(m.Tick, FlagKind.Marker, cue.Text ?? "", m));
            }
            ruler.Flags = flags.OrderBy(f => f.Tick).ToList();
            var song = _song;
            ruler.Bars = song.TimeSignatures.Count > 1 ? (from, to) => song.BarLines(from, to).Select(b => (b.Tick, b.Bar)) : null;
            ruler.TicksPerBeat = song.TicksPerBeat;
            ruler.BeatsPerBar = song.BeatsPerBar;
            ruler.Tempos = song.Tempos.Select(t => (t.Tick, t.Bpm)).ToList();
            ruler.FormatTick = tick => song.FormatBars(tick).Trim();
            ruler.InvalidateVisual();
        }

        /// <summary>The song's bar lines for the grid, or null when the time signature never changes.</summary>
        private Func<long, long, IEnumerable<(long Tick, long Bar, long BeatTicks, int Beats)>>? GridBars =>
            _song is { } song && song.TimeSignatures.Count > 1 ? song.BarLines : null;

        // --- Loop ---

        private bool HasLoop => _loopEnd > _loopStart;

        private void SetLoopRange(long start, long end, bool enable)
        {
            _loopStart = Math.Max(0, Math.Min(start, end));
            _loopEnd = Math.Max(start, end);
            if (enable && HasLoop) tglLoop.IsChecked = true;
            ApplyLoop();
        }

        private void OnLoopClick(object sender, RoutedEventArgs e)
        {
            // Turning the loop on without a range loops the bar at the playhead.
            if (tglLoop.IsChecked == true && !HasLoop && _song != null)
            {
                var (start, length) = SongOps.Bars(_song, (long)_player.PositionTicks, 1);
                _loopStart = start;
                _loopEnd = start + length;
            }
            ApplyLoop();
        }

        private void ToggleLoop()
        {
            tglLoop.IsChecked = tglLoop.IsChecked != true;
            OnLoopClick(tglLoop, new RoutedEventArgs());
        }

        private void ApplyLoop()
        {
            bool on = tglLoop.IsChecked == true && HasLoop;
            _player.SetLoop(_loopStart, _loopEnd, on);
            ruler.LoopStart = _loopStart;
            ruler.LoopEnd = _loopEnd;
            ruler.LoopEnabled = on;
            ruler.InvalidateVisual();
            UpdateLoopShade();
            if (HasLoop && _song != null)
                txtStatus.Text = $"Loop {_song.FormatBars(_loopStart).Trim()} – {_song.FormatBars(_loopEnd).Trim()}{(on ? "" : " (off)")}";
        }

        private void UpdateLoopShade()
        {
            bool show = HasLoop && _song != null;
            var visibility = show ? Visibility.Visible : Visibility.Collapsed;
            loopShade.Visibility = loopStartLine.Visibility = loopEndLine.Visibility = visibility;
            if (!show) return;
            double x1 = viewer.WorldToViewX(_loopStart), x2 = viewer.WorldToViewX(_loopEnd);
            Canvas.SetLeft(loopShade, x1);
            loopShade.Width = Math.Max(0, x2 - x1);
            loopShade.Opacity = tglLoop.IsChecked == true ? 1 : 0.35;
            loopStartLine.X1 = loopStartLine.X2 = x1;
            loopEndLine.X1 = loopEndLine.X2 = x2;
        }

        // --- Tracks ---

        private void OnTracksMenuClick(object sender, RoutedEventArgs e) => ShowTracksMenu(_activeVm, btnTracks);

        private void OnTrackRowRightClick(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not TrackViewModel vm) return;
            e.Handled = true;
            SetActiveTrack(vm);
            ShowTracksMenu(vm, null);
        }

        private void ShowTracksMenu(TrackViewModel? vm, UIElement? target)
        {
            if (_song == null) return;
            var track = vm?.Track;
            bool has = track != null;
            var menu = new ContextMenu();
            menu.Items.Add(Item("Track settings…", "", has, () => ShowTrackSettings(vm!)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Add track", "", true, AddTrack));
            menu.Items.Add(Item("Duplicate track", "", has, () => TrackCommand("Duplicate track", () => SongOps.DuplicateTrack(_song!, track!))));
            menu.Items.Add(Item("Delete track", "", has && _song.Tracks.Count > 1, () => DeleteTrack(track!)));
            menu.Items.Add(Item("Move up", "", has && track!.Index > 0,
                                () => TrackCommand("Move track", () => SongOps.MoveTrack(_song!, track!, -1))));
            menu.Items.Add(Item("Move down", "", has && track!.Index < _song.Tracks.Count - 1,
                                () => TrackCommand("Move track", () => SongOps.MoveTrack(_song!, track!, 1))));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Merge other tracks into this one…", "", has && _song.Tracks.Count > 1, () => ShowMerge(track!)));
            menu.Items.Add(Item("Split by channel", "", has && track!.Channels.Count > 1,
                                () => TrackCommand("Split by channel", () => SongOps.SplitByChannel(_song!, track!))));
            menu.Items.Add(Item("Split by pitch (hands)…", "", has && track!.Notes.Count > 0, () => ShowSplitByPitch(track!)));
            menu.Items.Add(new Separator());
            menu.Items.Add(FingeringMenu(track));
            menu.Items.Add(Item("Event list…", "Ctrl+E", has, ShowEventList));
            menu.Items.Add(Item("Export track as MIDI file…", "", has, () => ExportTrack(selectionOnly: false)));
            menu.Items.Add(Item("Export selected notes as MIDI file…", "", has && _editor.Selection.Count > 0, () => ExportTrack(selectionOnly: true)));
            if (target != null)
            {
                menu.PlacementTarget = target;
                menu.Placement = PlacementMode.Bottom;
            }
            else menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        /// <summary>An undoable change to the track list.</summary>
        private void TrackCommand(string label, Action change)
        {
            if (_song == null) return;
            _editor.CancelDrag();
            _history.Record(label);
            change();
            AfterEdit(label, tracksChanged: true);
        }

        private void AddTrack()
        {
            if (_song == null) return;
            SongTrack? added = null;
            TrackCommand("Add track", () => added = SongOps.AddTrack(_song!, (_activeVm?.Index ?? _song!.Tracks.Count - 1) + 1));
            if (added != null) ActivateTrack(added);
        }

        private void DeleteTrack(SongTrack track)
        {
            if (_song == null || _song.Tracks.Count < 2) return;
            if (track.Notes.Count > 0 &&
                MessageBox.Show(this, $"Delete the track '{track.Name}' with its {track.Notes.Count:N0} notes? (Undo brings it back.)", "nMIDI Editor",
                                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            TrackCommand("Delete track", () => SongOps.DeleteTrack(_song!, track));
        }

        private void ShowMerge(SongTrack into)
        {
            var d = new FormDialog(this, "Merge tracks", $"Moves the notes, controllers and events of the checked tracks into '{into.Name}' and removes them.");
            var checks = _song!.Tracks.Where(t => t != into).Select(t => (t, d.AddCheck($"{t.Index + 1}. {t.Name}", false))).ToList();
            if (!d.Ask()) return;
            var chosen = checks.Where(c => c.Item2()).Select(c => c.t).ToList();
            if (chosen.Count == 0) return;
            TrackCommand("Merge tracks", () => SongOps.MergeTracks(_song!, into, chosen));
        }

        private void ShowSplitByPitch(SongTrack track)
        {
            var d = new FormDialog(this, "Split by pitch",
                "Moves the notes below the split point to a new track under this one, for example to separate the left hand " +
                "of a piano part. Both tracks keep the controllers.");
            var point = d.AddText("Split point", "C4", "notes below go to the new track");
            var name = d.AddText("New track name", track.Name + " LH");
            if (!d.Ask()) return;
            if (ParsePitch(point()) is not int split)
            {
                txtStatus.Text = $"'{point()}' is not a note.";
                return;
            }
            TrackCommand("Split by pitch", () => SongOps.SplitByPitch(_song!, track, split, name().Trim().Length > 0 ? name().Trim() : track.Name + " LH"));
        }

        private sealed record ColorChoice(Color Color, string Name)
        {
            public override string ToString() => Name;
        }

        private void ShowTrackSettings(TrackViewModel vm)
        {
            if (_song == null) return;
            var track = vm.Track;
            var channels = Enumerable.Range(0, 16).Select(c => new Choice<int>(c, c == 9 ? "10 (drums)" : $"{c + 1}")).ToList();
            var instruments = GeneralMidi.Instruments.Select((n, i) => new Choice<int>(i, $"{i + 1}. {n}")).ToList();
            string[] colorNames = ["Blue", "Orange", "Green", "Red", "Violet", "Cyan", "Yellow", "Pink", "Lime", "Indigo", "Apricot", "Teal"];
            var colors = NoteSkins.TrackColors.Select((c, i) => new ColorChoice(c, colorNames[i])).ToList();
            int program = SongOps.GetInitial(track, CtrlKind.Program, 0) ?? 0;
            int volume = SongOps.GetInitial(track, CtrlKind.Controller, 7) ?? 100;
            int pan = SongOps.GetInitial(track, CtrlKind.Controller, 10) ?? 64;

            var d = new FormDialog(this, "Track settings", "Instrument, volume and pan are written at the start of the track, on its channel.");
            var name = d.AddText("Name", track.Name);
            var channel = d.AddChoice("Channel", channels, channels[track.Channel]);
            var moveAll = d.AddCheck("", true, "Move all the track's notes and controllers to this channel");
            var instrument = d.AddChoice("Instrument", instruments, instruments[Math.Clamp(program, 0, 127)], "General MIDI");
            var vol = d.AddNumber("Volume", volume, 0, 127, "CC 7, 0 to 127");
            var panValue = d.AddNumber("Pan", pan - 64, -64, 63, "−64 left, 0 centre, 63 right");
            var color = d.AddChoice("Colour", colors, colors.FirstOrDefault(c => c.Color == vm.Color) ?? colors[0]);
            if (!d.Ask()) return;

            string newName = name().Trim().Length > 0 ? name().Trim() : track.Name;
            int newChannel = channel().Value;
            int newProgram = instrument().Value, newVolume = vol(), newPan = panValue() + 64;
            bool changed = newName != track.Name || newChannel != track.Channel || newProgram != program || newVolume != volume || newPan != pan;
            if (changed)
            {
                Commit("Track settings", () =>
                {
                    track.Name = newName;
                    if (newChannel != track.Channel)
                    {
                        if (moveAll()) SongOps.SetChannel(track, newChannel);
                        else track.Channel = newChannel;
                    }
                    if (newProgram != program || SongOps.GetInitial(track, CtrlKind.Program, 0) == null)
                        SongOps.SetInitial(track, CtrlKind.Program, 0, newProgram);
                    if (newVolume != volume) SongOps.SetInitial(track, CtrlKind.Controller, 7, newVolume);
                    if (newPan != pan) SongOps.SetInitial(track, CtrlKind.Controller, 10, newPan);
                });
            }
            if (color().Color != vm.Color)
            {
                vm.SetColor(color().Color);
                if (vm == _activeVm) dotActiveTrack.Fill = vm.Brush;
                BuildElements();
            }
            if (vm == _activeVm || _coEdited.Contains(vm)) UpdateActiveTrackLabel();
        }

        // --- Song menu: bars, ranges, conductor events ---

        private void OnSongMenuClick(object sender, RoutedEventArgs e)
        {
            if (_song == null) return;
            var menu = new ContextMenu();
            foreach (var item in SongItems((long)_player.PositionTicks)) menu.Items.Add(item);
            menu.PlacementTarget = btnSong;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private IEnumerable<object> SongItems(long tick)
        {
            bool loop = HasLoop;
            string where = _song != null ? _song.FormatBars(tick).Trim() : "";
            yield return Item($"Insert bars at {where}…", "", true, () => ShowInsertBars(tick));
            yield return Item($"Delete bars from {where}…", "", true, () => ShowDeleteBars(tick));
            yield return new Separator();
            yield return Item("Cut loop range (all tracks, removes the time)", "", loop, () => RangeCommand(cut: true, remove: true));
            yield return Item("Copy loop range (all tracks)", "", loop, () => RangeCommand(cut: false, remove: false));
            yield return Item("Paste range at playhead, inserting time", "", _rangeClip != null, () => PasteRangeAt(insert: true));
            yield return Item("Paste range at playhead, mixing in", "", _rangeClip != null, () => PasteRangeAt(insert: false));
            yield return Item("Delete loop range (removes the time)", "", loop, () => RangeCommand(cut: false, remove: true));
            yield return Item("Clear loop range (keeps the time)", "", loop, ClearLoopRange);
            yield return Item("Insert silence the length of the loop range", "", loop,
                              () => SongCommand("Insert time", () => SongOps.InsertTime(_song!, _loopStart, _loopEnd - _loopStart)));
            yield return new Separator();
            yield return Item($"Tempo at {where}…", "", true, () => ShowTempoDialog(null, tick));
            yield return Item($"Time signature at {where}…", "", true, () => ShowTimeSignatureDialog(null, tick));
            yield return Item($"Key signature at {where}…", "", true, () => ShowKeySignatureDialog(null, tick));
            yield return Item($"Marker at {where}…", "", true, () => ShowMarkerDialog(null, tick));
            var markers = _song?.Conductor.Where(m => m.Event is MarkerEvent or CuePointEvent).OrderBy(m => m.Tick).ToList() ?? new List<SongMeta>();
            var go = new MenuItem { Header = "Go to marker", IsEnabled = markers.Count > 0 };
            foreach (var m in markers)
            {
                string text = m.Event is MarkerEvent me ? me.Text : ((CuePointEvent)m.Event).Text;
                go.Items.Add(Item($"{_song!.FormatBars(m.Tick).Trim()}  {text}", "", true, () => GoTo(m.Tick)));
            }
            yield return go;
            yield return Item("Tempo lane", "", true, () => ShowLane(new LaneMode(LaneKind.Tempo)));
        }

        private void GoTo(long tick)
        {
            SeekTo(tick);
            viewer.ScrollToX(tick, 0.1);
        }

        /// <summary>An undoable song-wide change (time, conductor).</summary>
        private void SongCommand(string label, Action change)
        {
            if (_song == null) return;
            _editor.CancelDrag();
            _history.Record(label);
            change();
            AfterEdit(label);
        }

        private void ShowInsertBars(long tick)
        {
            string bar = _song!.FormatBars(tick).Trim().Split('.')[0];
            var d = new FormDialog(this, "Insert bars", $"Inserts empty bars before bar {bar} on every track. Notes that sound across it get longer.");
            var count = d.AddNumber("Bars", 1, 1, 999);
            if (!d.Ask()) return;
            var (start, length) = SongOps.Bars(_song, tick, count());
            SongCommand(count() == 1 ? "Insert bar" : "Insert bars", () => SongOps.InsertTime(_song!, start, length));
        }

        private void ShowDeleteBars(long tick)
        {
            string bar = _song!.FormatBars(tick).Trim().Split('.')[0];
            var d = new FormDialog(this, "Delete bars", $"Deletes bars from bar {bar} on every track and moves what follows earlier.");
            var count = d.AddNumber("Bars", 1, 1, 999);
            if (!d.Ask()) return;
            var (start, length) = SongOps.Bars(_song, tick, count());
            SongCommand(count() == 1 ? "Delete bar" : "Delete bars", () => SongOps.DeleteTime(_song!, start, start + length));
        }

        private void RangeCommand(bool cut, bool remove)
        {
            if (_song == null || !HasLoop) return;
            if (cut || !remove) _rangeClip = SongOps.CopyRange(_song, _loopStart, _loopEnd);
            if (remove)
            {
                long start = _loopStart, end = _loopEnd;
                SongCommand(cut ? "Cut range" : "Delete range", () => SongOps.DeleteTime(_song!, start, end));
                SetLoopRange(start, start, enable: false);
            }
            else txtStatus.Text = $"Copied {_song.FormatBars(_loopStart).Trim()} – {_song.FormatBars(_loopEnd).Trim()} of every track.";
        }

        private void PasteRangeAt(bool insert)
        {
            if (_song == null || _rangeClip == null) return;
            var clip = _rangeClip;
            long at = NoteOps.SnapDown(_player.PositionTicks, SnapTicks);
            SongCommand(insert ? "Paste range (insert)" : "Paste range", () => SongOps.PasteRange(_song!, clip, at, insert));
        }

        private void ClearLoopRange()
        {
            if (_song == null || !HasLoop) return;
            long start = _loopStart, end = _loopEnd;
            SongCommand("Clear range", () => SongOps.ClearRange(_song!, start, end, _song!.Tracks));
        }

        // --- Ranges of one track (track lanes window) ---

        private static RangeClip? _laneClip;

        /// <summary>Edit a time range of one track: delete, copy, cut, paste at the playhead, duplicate after itself, or move.</summary>
        internal void LaneRangeCommand(LaneRangeOp op, SongTrack track, long from, long to, long by = 0)
        {
            if (_song == null) return;
            var song = _song;
            switch (op)
            {
                case LaneRangeOp.Copy:
                    _laneClip = SongOps.CopyRange(song, from, to, new[] { track });
                    txtStatus.Text = $"Copied {song.FormatBars(from).Trim()} – {song.FormatBars(to).Trim()} of {track.Name}.";
                    break;
                case LaneRangeOp.Cut:
                    _laneClip = SongOps.CopyRange(song, from, to, new[] { track });
                    SongCommand("Cut range", () => SongOps.ClearRange(song, from, to, new[] { track }));
                    break;
                case LaneRangeOp.Delete:
                    SongCommand("Delete range", () => SongOps.ClearRange(song, from, to, new[] { track }));
                    break;
                case LaneRangeOp.Paste when _laneClip != null:
                {
                    var clip = _laneClip;
                    long at = NoteOps.SnapDown(_player.PositionTicks, song.TicksPerBeat);
                    int offset = track.Index - clip.Tracks[0].Track;
                    SongCommand("Paste range", () => SongOps.PasteRange(song, clip, at, insert: false, trackOffset: offset));
                    break;
                }
                case LaneRangeOp.Duplicate:
                {
                    var clip = SongOps.CopyRange(song, from, to, new[] { track });
                    SongCommand("Duplicate range", () => SongOps.PasteRange(song, clip, to, insert: false));
                    break;
                }
                case LaneRangeOp.Move when by != 0:
                {
                    var clip = SongOps.CopyRange(song, from, to, new[] { track });
                    SongCommand("Move range", () =>
                    {
                        SongOps.ClearRange(song, from, to, new[] { track });
                        SongOps.PasteRange(song, clip, Math.Max(0, from + by), insert: false);
                    });
                    break;
                }
            }
        }

        // --- Ruler ---

        private void ShowRulerMenu(long tick, RulerFlag? flag)
        {
            if (_song == null) return;
            long beat = NoteOps.Snap(tick, _song.TicksPerBeat);
            var menu = new ContextMenu();
            if (flag != null)
            {
                menu.Items.Add(Item($"Edit {FlagName(flag)}…", "", true, () => EditFlag(flag)));
                bool required = flag.Tick == 0 && flag.Kind is FlagKind.Tempo or FlagKind.TimeSignature;
                menu.Items.Add(Item($"Delete {FlagName(flag)}", "", !required, () => DeleteFlag(flag)));
                menu.Items.Add(new Separator());
            }
            menu.Items.Add(Item("Move the playhead here", "", true, () => SeekTo(tick)));
            long loopEnd = Math.Max(beat + 1, HasLoop ? _loopEnd : beat + _song.TicksPerBar);
            long loopStart = HasLoop ? Math.Min(_loopStart, beat - 1) : Math.Max(0, beat - _song.TicksPerBar);
            menu.Items.Add(Item("Loop starts here", "", true, () => SetLoopRange(beat, loopEnd, enable: true)));
            menu.Items.Add(Item("Loop ends here", "", true, () => SetLoopRange(loopStart, beat, enable: true)));
            menu.Items.Add(Item("Loop this bar", "", true, () =>
            {
                var (start, length) = SongOps.Bars(_song!, tick, 1);
                SetLoopRange(start, start + length, enable: true);
            }));
            menu.Items.Add(Item("Remove the loop range", "", HasLoop, () =>
            {
                tglLoop.IsChecked = false;
                SetLoopRange(0, 0, enable: false);
            }));
            menu.Items.Add(new Separator());
            foreach (var item in SongItems(beat)) menu.Items.Add(item);
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        private static string FlagName(RulerFlag flag) => flag.Kind switch
        {
            FlagKind.Tempo => "tempo change",
            FlagKind.TimeSignature => "time signature",
            FlagKind.KeySignature => "key signature",
            _ => "marker",
        };

        private void EditFlag(RulerFlag flag)
        {
            switch (flag.Source)
            {
                case TempoChange t: ShowTempoDialog(t, t.Tick); break;
                case TimeSignatureChange s: ShowTimeSignatureDialog(s, s.Tick); break;
                case SongMeta { Event: KeySignatureEvent } m: ShowKeySignatureDialog(m, m.Tick); break;
                case SongMeta m: ShowMarkerDialog(m, m.Tick); break;
            }
        }

        private void DeleteFlag(RulerFlag flag)
        {
            if (_song == null) return;
            switch (flag.Source)
            {
                case TempoChange t when t.Tick > 0:
                    SongCommand("Delete tempo change", () => _song!.Tempos = _song!.Tempos.Where(x => x != t).ToList());
                    break;
                case TimeSignatureChange s when s.Tick > 0:
                    SongCommand("Delete time signature", () => _song!.TimeSignatures = _song!.TimeSignatures.Where(x => x != s).ToList());
                    break;
                case SongMeta m:
                    SongCommand($"Delete {FlagName(flag)}", () => _song!.Conductor = _song!.Conductor.Where(x => !ReferenceEquals(x, m)).ToList());
                    break;
            }
        }

        /// <summary>Position field shared by the conductor dialogs.</summary>
        private Func<long?> AddPosition(FormDialog d, long tick)
        {
            var text = d.AddText("Position", _song!.FormatBars(tick).Trim(), "bar.beat.tick");
            return () => ParsePosition(text().Trim());
        }

        private void ShowTempoDialog(TempoChange? existing, long tick)
        {
            if (_song == null) return;
            existing ??= _song.Tempos.FirstOrDefault(t => t.Tick == tick);
            double bpm = existing?.Bpm ?? _song.Timeline.BpmAt(tick, _song.TicksPerQuarter);
            var d = new FormDialog(this, existing != null ? "Edit tempo change" : "Insert tempo change",
                                   "Tempo in quarter notes per minute. It lasts until the next tempo change.");
            var value = d.AddText("BPM", bpm.ToString("0.###", CultureInfo.InvariantCulture), "5 to 1000");
            var position = existing?.Tick == 0 ? () => (long?)0 : AddPosition(d, tick);
            if (!d.Ask()) return;

            if (!double.TryParse(value().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double newBpm) || newBpm < 5 || newBpm > 1000
                || position() is not long at)
            {
                txtStatus.Text = "Not a valid tempo or position.";
                return;
            }
            var change = new TempoChange(at, TempoChange.FromBpm(newBpm));
            SongCommand(existing != null ? "Edit tempo" : "Insert tempo", () =>
                _song!.Tempos = _song!.Tempos.Where(t => t != existing && t.Tick != at).Append(change).ToList());
        }

        private void ShowTimeSignatureDialog(TimeSignatureChange? existing, long tick)
        {
            if (_song == null) return;
            // A new signature starts at the bar line at or before the position.
            if (existing == null)
            {
                var (bar, _, _) = _song.ToBarBeat(tick);
                tick = _song.FromBarBeat(bar, 0, 0);
                existing = _song.TimeSignatures.FirstOrDefault(s => s.Tick == tick);
            }
            var current = existing ?? _song.TimeSignatures.Last(s => s.Tick <= tick);
            var denominators = new[] { 1, 2, 4, 8, 16, 32 };
            var d = new FormDialog(this, existing != null ? "Edit time signature" : "Insert time signature",
                                   "Put a time signature at the start of a bar. Notes do not move; the bar lines after it change.");
            var numerator = d.AddNumber("Beats per bar", current.Numerator, 1, 64);
            var denominator = d.AddChoice("Beat value", denominators, current.Denominator, "4 = quarter note, 8 = eighth note");
            var position = existing?.Tick == 0 ? () => (long?)0 : AddPosition(d, tick);
            if (!d.Ask()) return;
            if (position() is not long at)
            {
                txtStatus.Text = "Not a valid position.";
                return;
            }
            var change = new TimeSignatureChange(at, numerator(), denominator());
            SongCommand(existing != null ? "Edit time signature" : "Insert time signature", () =>
                _song!.TimeSignatures = _song!.TimeSignatures.Where(s => s != existing && s.Tick != at).Append(change).ToList());
        }

        private void ShowKeySignatureDialog(SongMeta? existing, long tick)
        {
            if (_song == null) return;
            var current = existing?.Event as KeySignatureEvent
                          ?? _song.Conductor.Where(m => m.Tick <= tick).Select(m => m.Event).OfType<KeySignatureEvent>().LastOrDefault();
            var selected = KeySigChoices.FirstOrDefault(k => current != null && k.Sharps == current.Key && k.Minor == (current.Scale == 1)) ?? KeySigChoices[7];
            var d = new FormDialog(this, existing != null ? "Edit key signature" : "Insert key signature",
                                   "A key signature tells notation programs how to spell the notes. It does not change the sound.");
            var key = d.AddChoice("Key", KeySigChoices, selected);
            var position = AddPosition(d, tick);
            if (!d.Ask()) return;
            if (position() is not long at)
            {
                txtStatus.Text = "Not a valid position.";
                return;
            }
            var k = key();
            var meta = new SongMeta(at, new KeySignatureEvent((sbyte)k.Sharps, (byte)(k.Minor ? 1 : 0)));
            SongCommand(existing != null ? "Edit key signature" : "Insert key signature", () =>
                _song!.Conductor = _song!.Conductor.Where(m => !ReferenceEquals(m, existing) && !(m.Event is KeySignatureEvent && m.Tick == at))
                                                   .Append(meta).OrderBy(m => m.Tick).ToList());
        }

        private void ShowMarkerDialog(SongMeta? existing, long tick)
        {
            if (_song == null) return;
            string text = existing?.Event switch { MarkerEvent m => m.Text, CuePointEvent c => c.Text, _ => "" } ?? "";
            var d = new FormDialog(this, existing != null ? "Edit marker" : "Insert marker",
                                   "Markers name parts of the song (Intro, Verse, Chorus...). Jump to them from the Song menu.");
            var name = d.AddText("Text", text.Length > 0 ? text : "Marker");
            var position = AddPosition(d, tick);
            if (!d.Ask()) return;
            if (position() is not long at)
            {
                txtStatus.Text = "Not a valid position.";
                return;
            }
            var meta = new SongMeta(at, new MarkerEvent(name()));
            SongCommand(existing != null ? "Edit marker" : "Insert marker", () =>
                _song!.Conductor = _song!.Conductor.Where(m => !ReferenceEquals(m, existing)).Append(meta).OrderBy(m => m.Tick).ToList());
        }
    }
}
