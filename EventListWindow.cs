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
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace nMIDI
{
    /// <summary>
    /// Every event of the edited track (or the song's tempo, time signature, key and marker events) as an
    /// editable list. Typing in a cell changes the event as one undo step; Delete removes the selected rows.
    /// </summary>
    public sealed class EventListWindow : Window
    {
        internal enum RowKind { Note, Controller, Program, PitchBend, ChannelPressure, PolyPressure, Meta, Tempo, TimeSignature, SongMeta }

        /// <summary>One row. The settable texts hold what the user typed until it is applied.</summary>
        public sealed class Row
        {
            internal RowKind Kind;
            internal object Source = null!;
            internal string ShownPosition = "", ShownTick = "";
            internal bool Fixed;   // the first tempo or time signature, which stays at the start

            public long Tick { get; init; }
            public string Position { get; set; } = "";
            public string TickText { get; set; } = "";
            public string Type { get; init; } = "";
            public string Channel { get; set; } = "";
            public string Data1 { get; set; } = "";
            public string Data2 { get; set; } = "";
            public string Length { get; set; } = "";
            public string Info { get; init; } = "";
        }

        private static readonly string[] Views =
        [
            "Edited track: all events",
            "Edited track: notes",
            "Edited track: controllers, programs, pitch bend, pressure",
            "Song: tempo, time signature, key signature, markers",
        ];

        private readonly MainWindow _main;
        private readonly DataGrid _grid = new();
        private readonly ComboBox _view = new();
        private readonly TextBlock _status = new();
        private readonly Button _add, _delete, _goTo;
        private bool _refreshing, _editing, _refreshPending;
        private object? _reselect;

        public EventListWindow(MainWindow main)
        {
            _main = main;
            Owner = main;
            Title = "Event list";
            Width = 820;
            Height = 600;
            MinWidth = 520;
            MinHeight = 300;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Res<Brush>("Bg1");
            Foreground = Res<Brush>("Text");
            FontFamily = Res<FontFamily>("UiFont");
            FontSize = 12;
            UseLayoutRounding = true;

            foreach (var v in Views) _view.Items.Add(v);
            _view.SelectedIndex = 0;
            _view.Height = 26;
            _view.MinWidth = 330;
            _view.SelectionChanged += (_, _) => Refresh();

            _add = MakeButton("Add ▾", "Add a note or controller at the playhead (or a tempo, time signature, key or marker in the song view)");
            _add.Click += (_, _) => ShowAddMenu();
            _delete = MakeButton("Delete", "Delete the selected events (Del)");
            _delete.Click += (_, _) => DeleteSelected();
            _goTo = MakeButton("Go to", "Move the playhead to the selected event");
            _goTo.Click += (_, _) =>
            {
                if (_grid.SelectedItem is Row row) _main.SeekTo(row.Tick);
            };

            var top = new DockPanel { Margin = new Thickness(10, 10, 10, 6), LastChildFill = false };
            top.Children.Add(_view);
            foreach (var b in new[] { _goTo, _delete, _add })
            {
                DockPanel.SetDock(b, Dock.Right);
                b.Margin = new Thickness(6, 0, 0, 0);
                top.Children.Add(b);
            }

            BuildGrid();

            _status.Foreground = Res<Brush>("TextDim");
            _status.Margin = new Thickness(10, 4, 10, 8);
            _status.TextTrimming = TextTrimming.CharacterEllipsis;

            var root = new DockPanel();
            DockPanel.SetDock(top, Dock.Top);
            DockPanel.SetDock(_status, Dock.Bottom);
            root.Children.Add(top);
            root.Children.Add(_status);
            root.Children.Add(_grid);
            Content = root;

            SourceInitialized += (_, _) => FormDialog.DarkTitleBar(this);
            PreviewKeyDown += OnPreviewKeyDown;
            Loaded += (_, _) => Refresh();
        }

        private static T Res<T>(string key) => (T)Application.Current.FindResource(key);

        private static Button MakeButton(string text, string tip) =>
            new() { Content = text, ToolTip = tip, Height = 26, MinWidth = 64, Padding = new Thickness(10, 0, 10, 0), Style = Res<Style>("ToolButton") };

        private void BuildGrid()
        {
            _grid.AutoGenerateColumns = false;
            _grid.CanUserAddRows = false;
            _grid.CanUserDeleteRows = false;
            _grid.CanUserReorderColumns = false;
            _grid.CanUserSortColumns = false;
            _grid.SelectionMode = DataGridSelectionMode.Extended;
            _grid.SelectionUnit = DataGridSelectionUnit.FullRow;
            _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            _grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            _grid.HorizontalGridLinesBrush = Res<Brush>("LineSoft");
            _grid.Background = Res<Brush>("Bg0");
            _grid.RowBackground = Res<Brush>("Bg0");
            _grid.AlternatingRowBackground = Res<Brush>("Bg1");
            _grid.Foreground = Res<Brush>("Text");
            _grid.BorderBrush = Res<Brush>("Line");
            _grid.BorderThickness = new Thickness(0, 1, 0, 1);
            _grid.RowHeight = 22;
            _grid.EnableRowVirtualization = true;
            VirtualizingPanel.SetVirtualizationMode(_grid, VirtualizationMode.Recycling);

            var header = new Style(typeof(DataGridColumnHeader));
            header.Setters.Add(new Setter(BackgroundProperty, Res<Brush>("Bg2")));
            header.Setters.Add(new Setter(ForegroundProperty, Res<Brush>("TextDim")));
            header.Setters.Add(new Setter(PaddingProperty, new Thickness(6, 4, 6, 4)));
            header.Setters.Add(new Setter(BorderBrushProperty, Res<Brush>("Line")));
            header.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            _grid.ColumnHeaderStyle = header;

            var cell = new Style(typeof(DataGridCell));
            cell.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
            cell.Setters.Add(new Setter(PaddingProperty, new Thickness(4, 0, 4, 0)));
            var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(BackgroundProperty, Res<Brush>("AccentSoft")));
            selected.Setters.Add(new Setter(ForegroundProperty, Res<Brush>("Text")));
            cell.Triggers.Add(selected);
            _grid.CellStyle = cell;

            var editBox = new Style(typeof(TextBox));
            editBox.Setters.Add(new Setter(BackgroundProperty, Res<Brush>("Bg3")));
            editBox.Setters.Add(new Setter(ForegroundProperty, Res<Brush>("Text")));
            editBox.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, Res<Brush>("Text")));
            editBox.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
            editBox.Setters.Add(new Setter(PaddingProperty, new Thickness(2, 0, 2, 0)));

            void Column(string header, string path, double width, bool editable)
            {
                var column = new DataGridTextColumn
                {
                    Header = header,
                    Binding = new Binding(path)
                    {
                        Mode = editable ? BindingMode.TwoWay : BindingMode.OneWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                    },
                    Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star),
                    IsReadOnly = !editable,
                    EditingElementStyle = editBox,
                };
                _grid.Columns.Add(column);
            }
            Column("Position", nameof(Row.Position), 100, true);
            Column("Tick", nameof(Row.TickText), 80, true);
            Column("Type", nameof(Row.Type), 120, false);
            Column("Ch", nameof(Row.Channel), 40, true);
            Column("Note / number", nameof(Row.Data1), 96, true);
            Column("Value", nameof(Row.Data2), 70, true);
            Column("Length", nameof(Row.Length), 70, true);
            Column("", nameof(Row.Info), 0, false);

            _grid.BeginningEdit += (_, e) =>
            {
                if (e.Row.Item is not Row row || !CanEdit(row, e.Column.DisplayIndex)) e.Cancel = true;
                else _editing = true;
            };
            _grid.RowEditEnding += (_, e) =>
            {
                _editing = false;
                if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is Row row)
                    Dispatcher.BeginInvoke(new Action(() => Apply(row)), DispatcherPriority.Background);
                else if (_refreshPending) Refresh();
            };
            _grid.CellEditEnding += (_, e) =>
            {
                if (e.EditAction == DataGridEditAction.Cancel) _editing = false;
            };
            _grid.SelectionChanged += (_, _) => OnGridSelectionChanged();
        }

        // --- Rows ---

        private bool SongView => _view.SelectedIndex == 3;

        /// <summary>Rebuild the rows from the song (after any edit, an undo, or a change of edited track).</summary>
        public void Refresh()
        {
            if (!IsLoaded) return;
            if (_editing)
            {
                _refreshPending = true;
                return;
            }
            _refreshPending = false;
            var song = _main.CurrentSong;
            var track = _main.ActiveTrack;
            Title = SongView ? "Event list · song" : track != null ? $"Event list · {track.Index + 1}. {track.Name}" : "Event list";

            var keep = new HashSet<object>(_grid.SelectedItems.Cast<Row>().Select(r => r.Source), ReferenceEqualityComparer.Instance);
            foreach (var n in _main.SelectedNoteSet) keep.Add(n);
            if (_reselect != null) keep.Add(_reselect);
            _reselect = null;

            List<Row> rows;
            if (song == null) rows = new List<Row>();
            else if (SongView) rows = SongRows(song);
            else if (track != null) rows = TrackRows(song, track, _view.SelectedIndex);
            else rows = new List<Row>();

            _refreshing = true;
            try
            {
                _grid.ItemsSource = rows;
                Row? first = null;
                foreach (var row in rows)
                {
                    if (!keep.Contains(row.Source)) continue;
                    _grid.SelectedItems.Add(row);
                    first ??= row;
                }
                if (first != null) _grid.ScrollIntoView(first);
            }
            finally
            {
                _refreshing = false;
            }

            bool trackView = !SongView && track != null;
            _add.IsEnabled = song != null && (SongView || trackView);
            _delete.IsEnabled = rows.Count > 0;
            _goTo.IsEnabled = rows.Count > 0;
            _status.Text = song == null ? "No song." :
                SongView ? $"{rows.Count:N0} song events. Type in Position, Tick or Value to change one; tempo values are in BPM."
                : track == null ? "Pick a track in the main window."
                : $"{rows.Count:N0} events. Type in a cell and press Enter to change it; notes take names like C4 or F#3, pitch bend is −8192 to 8191.";
        }

        private static List<Row> TrackRows(Song song, SongTrack track, int view)
        {
            var rows = new List<(Row Row, int Rank)>();
            if (view is 0 or 1)
                foreach (var n in track.Notes)
                {
                    string release = n.OffVelocity > 0 ? $"release velocity {n.OffVelocity}" : "";
                    var row = Make(song, n.Start, RowKind.Note, n, "Note", n.Channel + 1,
                                   MainWindow.PitchName(n.Number), Num(n.Velocity), n.Length.ToString(CultureInfo.InvariantCulture), release);
                    rows.Add((row, 2));
                }
            if (view is 0 or 2)
                foreach (var c in track.Controls)
                {
                    int ch = c.Channel + 1;
                    var row = c.Kind switch
                    {
                        CtrlKind.Controller =>
                            Make(song, c.Tick, RowKind.Controller, c, "Controller", ch, Num(c.Number), Num(c.Value), "", ControllerNames.Label(c.Number)),
                        CtrlKind.Program =>
                            Make(song, c.Tick, RowKind.Program, c, "Program", ch, Num(c.Value + 1), "", "",
                                 GeneralMidi.Instruments[Math.Clamp(c.Value, 0, 127)]),
                        CtrlKind.PitchBend =>
                            Make(song, c.Tick, RowKind.PitchBend, c, "Pitch bend", ch, "", Num(c.Value - 8192), "", c.Value == 8192 ? "centre" : ""),
                        CtrlKind.ChannelPressure =>
                            Make(song, c.Tick, RowKind.ChannelPressure, c, "Channel pressure", ch, "", Num(c.Value), "", ""),
                        _ =>
                            Make(song, c.Tick, RowKind.PolyPressure, c, "Key pressure", ch, MainWindow.PitchName(c.Number), Num(c.Value), "", ""),
                    };
                    rows.Add((row, 1));
                }
            if (view == 0)
                foreach (var m in track.Meta)
                    rows.Add((Make(song, m.Tick, RowKind.Meta, m, MetaType(m.Event), 0, "", "", "", MetaText(m.Event)), 0));
            return rows.OrderBy(r => r.Row.Tick).ThenBy(r => r.Rank).Select(r => r.Row).ToList();
        }

        private static List<Row> SongRows(Song song)
        {
            var rows = new List<(Row Row, int Rank)>();
            bool first = true;
            foreach (var t in song.Tempos)
            {
                string bpm = t.Bpm.ToString("0.###", CultureInfo.InvariantCulture);
                var row = Make(song, t.Tick, RowKind.Tempo, t, "Tempo", 0, "", bpm, "", $"{t.MicrosecondsPerQuarter:N0} µs per quarter note");
                row.Fixed = first;
                first = false;
                rows.Add((row, 1));
            }
            first = true;
            foreach (var s in song.TimeSignatures)
            {
                var row = Make(song, s.Tick, RowKind.TimeSignature, s, "Time signature", 0,
                               Num(s.Numerator), Num(s.Denominator), "", $"{s.Numerator}/{s.Denominator}");
                row.Fixed = first;
                first = false;
                rows.Add((row, 0));
            }
            foreach (var m in song.Conductor)
                rows.Add((Make(song, m.Tick, RowKind.SongMeta, m, MetaType(m.Event), 0, "", "", "", MetaText(m.Event)), 2));
            return rows.OrderBy(r => r.Row.Tick).ThenBy(r => r.Rank).Select(r => r.Row).ToList();
        }

        private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

        private static Row Make(Song song, long tick, RowKind kind, object source, string type, int channel,
                                string data1, string data2, string length, string info)
        {
            string position = song.FormatBars(tick).Trim();
            string tickText = tick.ToString(CultureInfo.InvariantCulture);
            return new Row
            {
                Kind = kind,
                Source = source,
                Tick = tick,
                ShownPosition = position,
                ShownTick = tickText,
                Position = position,
                TickText = tickText,
                Type = type,
                Channel = channel > 0 ? Num(channel) : "",
                Data1 = data1,
                Data2 = data2,
                Length = length,
                Info = info,
            };
        }

        private static string MetaType(MidiEvent e) => e switch
        {
            KeySignatureEvent => "Key signature",
            MarkerEvent => "Marker",
            CuePointEvent => "Cue point",
            LyricEvent => "Lyric",
            CopyrightNoticeEvent => "Copyright",
            InstrumentNameEvent => "Instrument name",
            SequenceTrackNameEvent => "Track name",
            TextEvent => "Text",
            SysExEvent => "SysEx",
            _ => e.EventType.ToString(),
        };

        private static string MetaText(MidiEvent e) => e switch
        {
            KeySignatureEvent k => $"{Math.Abs((int)k.Key)} {(k.Key >= 0 ? "sharps" : "flats")}, {(k.Scale == 0 ? "major" : "minor")}",
            BaseTextEvent t => t.Text,
            SysExEvent s => $"{s.Data?.Length ?? 0} bytes",
            _ => "",
        };

        /// <summary>Which cells of a row can be typed in (columns: position, tick, type, ch, data 1, value, length, info).</summary>
        private static bool CanEdit(Row row, int column) => column switch
        {
            0 or 1 => !row.Fixed,
            3 => row.Kind is RowKind.Note or RowKind.Controller or RowKind.Program or RowKind.PitchBend
                          or RowKind.ChannelPressure or RowKind.PolyPressure,
            4 => row.Kind is RowKind.Note or RowKind.Controller or RowKind.Program or RowKind.PolyPressure or RowKind.TimeSignature,
            5 => row.Kind is RowKind.Note or RowKind.Controller or RowKind.PitchBend or RowKind.ChannelPressure
                          or RowKind.PolyPressure or RowKind.Tempo or RowKind.TimeSignature,
            6 => row.Kind == RowKind.Note,
            _ => false,
        };

        // --- Editing ---

        private static bool Int(string text, int min, int max, out int value) =>
            int.TryParse(text.Trim().Replace('−', '-'), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)
            && value >= min && value <= max;

        /// <summary>Apply what was typed in a row, as one undo step. Bad values are reported and dropped.</summary>
        private void Apply(Row row)
        {
            var song = _main.CurrentSong;
            if (song == null) return;
            var track = _main.ActiveTrack;
            string? error = null;

            long tick = row.Tick;
            if (row.Position.Trim() != row.ShownPosition)
            {
                if (_main.ParseBars(row.Position.Trim()) is long t) tick = t;
                else error = $"'{row.Position}' is not a position (bar.beat.tick, counted from 1).";
            }
            else if (row.TickText.Trim() != row.ShownTick)
            {
                if (long.TryParse(row.TickText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long t)) tick = t;
                else error = $"'{row.TickText}' is not a tick.";
            }

            int channel = 0;
            if (error == null && row.Channel.Length > 0 && !Int(row.Channel, 1, 16, out channel)) error = "The channel is 1 to 16.";
            channel -= 1;

            if (error == null)
                error = row.Source switch
                {
                    SongNote n when track != null => ApplyNote(track, n, row, tick, channel),
                    SongCtrl c when track != null => ApplyCtrl(track, c, row, tick, channel),
                    SongMeta m when row.Kind == RowKind.Meta && track != null =>
                        Replace("Move event", () => track.Meta = Swap(track.Meta, m, Keep(m with { Tick = tick }), x => x.Tick), m.Tick == tick),
                    SongMeta m when row.Kind == RowKind.SongMeta =>
                        Replace("Move event", () => song.Conductor = Swap(song.Conductor, m, Keep(m with { Tick = tick }), x => x.Tick), m.Tick == tick),
                    TempoChange t => ApplyTempo(song, t, row, tick),
                    TimeSignatureChange s => ApplySignature(song, s, row, tick),
                    _ => null,
                };

            if (error != null)
            {
                _status.Text = error;
                System.Media.SystemSounds.Beep.Play();
                Refresh();
            }
        }

        /// <summary>
        /// The list with <paramref name="old"/> replaced by <paramref name="changed"/>, in time order. With
        /// <paramref name="onePerTick"/>, an event already at the new tick is replaced too (tempo, time signature).
        /// </summary>
        private static List<T> Swap<T>(IEnumerable<T> list, T old, T changed, Func<T, long> tick, bool onePerTick = false) where T : class =>
            list.Where(x => !ReferenceEquals(x, old) && !(onePerTick && tick(x) == tick(changed)))
                .Append(changed)
                .OrderBy(tick)
                .ToList();

        private T Keep<T>(T item) where T : notnull
        {
            _reselect = item;
            return item;
        }

        /// <summary>Commit a change unless nothing changes. Returns null (no error).</summary>
        private string? Replace(string label, Action change, bool same)
        {
            if (same) Refresh();
            else _main.Commit(label, change);
            return null;
        }

        private string? ApplyNote(SongTrack track, SongNote n, Row row, long tick, int channel)
        {
            if (MainWindow.ParsePitch(row.Data1.Trim()) is not int pitch) return $"'{row.Data1}' is not a note (C4, F#3, Bb2 or 0–127).";
            if (!Int(row.Data2, 1, 127, out int velocity)) return "The velocity is 1 to 127.";
            if (!long.TryParse(row.Length.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long length) || length < 1)
                return "The length is a number of ticks, 1 or more.";
            var changed = n with { Number = pitch, Velocity = velocity, Start = tick, End = tick + length, Channel = channel };
            if (changed == n) return Replace("", () => { }, true);
            _main.Commit("Edit note", () => track.Notes = NoteOps.Replace(track.Notes, new[] { n }, new[] { changed }));
            _main.SelectNotes(new[] { changed });
            return null;
        }

        private string? ApplyCtrl(SongTrack track, SongCtrl c, Row row, long tick, int channel)
        {
            int number = c.Number, value = c.Value;
            switch (c.Kind)
            {
                case CtrlKind.Controller:
                    if (!Int(row.Data1, 0, 127, out number)) return "The controller number is 0 to 127.";
                    if (!Int(row.Data2, 0, 127, out value)) return "The value is 0 to 127.";
                    break;
                case CtrlKind.Program:
                    if (!Int(row.Data1, 1, 128, out int program)) return "The program is 1 to 128.";
                    value = program - 1;
                    break;
                case CtrlKind.PitchBend:
                    if (!Int(row.Data2, -8192, 8191, out int bend)) return "Pitch bend is −8192 to 8191 (0 is the centre).";
                    value = bend + 8192;
                    break;
                case CtrlKind.ChannelPressure:
                    if (!Int(row.Data2, 0, 127, out value)) return "The value is 0 to 127.";
                    break;
                case CtrlKind.PolyPressure:
                    if (MainWindow.ParsePitch(row.Data1.Trim()) is not int key) return $"'{row.Data1}' is not a note.";
                    number = key;
                    if (!Int(row.Data2, 0, 127, out value)) return "The value is 0 to 127.";
                    break;
            }
            var changed = c with { Tick = tick, Channel = channel, Number = number, Value = value };
            if (changed == c) return Replace("", () => { }, true);
            Keep(changed);
            _main.Commit("Edit event", () => track.SetControls(track.Controls.Where(x => !ReferenceEquals(x, c)).Append(changed)));
            return null;
        }

        private string? ApplyTempo(Song song, TempoChange t, Row row, long tick)
        {
            if (!double.TryParse(row.Data2.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double bpm) || bpm < 5 || bpm > 1000)
                return "The tempo is 5 to 1000 BPM.";
            var changed = new TempoChange(row.Fixed ? 0 : tick, TempoChange.FromBpm(bpm));
            if (changed == t) return Replace("", () => { }, true);
            Keep(changed);
            _main.Commit("Edit tempo", () => song.Tempos = Swap(song.Tempos, t, changed, x => x.Tick, onePerTick: true));
            return null;
        }

        private string? ApplySignature(Song song, TimeSignatureChange s, Row row, long tick)
        {
            if (!Int(row.Data1, 1, 32, out int numerator)) return "The beats per bar are 1 to 32.";
            if (!Int(row.Data2, 1, 32, out int denominator) || (denominator & (denominator - 1)) != 0) return "The beat unit is 1, 2, 4, 8, 16 or 32.";
            var changed = new TimeSignatureChange(row.Fixed ? 0 : tick, numerator, denominator);
            if (changed == s) return Replace("", () => { }, true);
            Keep(changed);
            _main.Commit("Edit time signature", () => song.TimeSignatures = Swap(song.TimeSignatures, s, changed, x => x.Tick, onePerTick: true));
            return null;
        }

        private void DeleteSelected()
        {
            var song = _main.CurrentSong;
            var rows = _grid.SelectedItems.Cast<Row>().Where(r => !r.Fixed).ToList();
            if (song == null || rows.Count == 0) return;
            var gone = new HashSet<object>(rows.Select(r => r.Source), ReferenceEqualityComparer.Instance);
            var track = _main.ActiveTrack;
            _main.Commit(rows.Count == 1 ? "Delete event" : "Delete events", () =>
            {
                if (SongView)
                {
                    song.Tempos = song.Tempos.Where(t => !gone.Contains(t)).ToList();
                    song.TimeSignatures = song.TimeSignatures.Where(s => !gone.Contains(s)).ToList();
                    song.Conductor = song.Conductor.Where(m => !gone.Contains(m)).ToList();
                }
                else if (track != null)
                {
                    track.Notes = track.Notes.Where(n => !gone.Contains(n)).ToList();
                    track.Controls = track.Controls.Where(c => !gone.Contains(c)).ToList();
                    track.Meta = track.Meta.Where(m => !gone.Contains(m)).ToList();
                }
            });
        }

        private void ShowAddMenu()
        {
            var song = _main.CurrentSong;
            if (song == null) return;
            long tick = _main.PlayheadTick;
            string where = song.FormatBars(tick).Trim();
            var menu = new ContextMenu();
            void Add(string header, Action action)
            {
                var item = new MenuItem { Header = header };
                item.Click += (_, _) => action();
                menu.Items.Add(item);
            }
            if (SongView)
            {
                Add($"Tempo at {where}…", () => _main.EditTempo(tick));
                Add($"Time signature at {where}…", () => _main.EditTimeSignature(tick));
                Add($"Key signature at {where}…", () => _main.EditKeySignature(tick));
                Add($"Marker at {where}…", () => _main.EditMarker(tick));
            }
            else if (_main.ActiveTrack is { } track)
            {
                Add($"Note at {where}", () =>
                {
                    var note = new SongNote(60, track.Channel, tick, tick + Math.Max(1, _main.NewNoteTicks), _main.NewVelocity);
                    _main.Commit("Add note", () => track.Notes = NoteOps.Replace(track.Notes, Array.Empty<SongNote>(), new[] { note }));
                    _main.SelectNotes(new[] { note });
                });
                Add($"Controller or program change at {where}…", () => AddControl(track, tick));
            }
            menu.PlacementTarget = _add;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void AddControl(SongTrack track, long tick)
        {
            var kinds = new[] { "Controller", "Program change", "Pitch bend", "Channel pressure" };
            var dialog = new FormDialog(this, "Add event", $"A new event at {_main.CurrentSong!.FormatBars(tick).Trim()} on channel {track.Channel + 1}.");
            var kind = dialog.AddChoice("Type", kinds, kinds[0]);
            var number = dialog.AddNumber("Number", 7, 0, 128, "controller 0–127, or program 1–128");
            var value = dialog.AddNumber("Value", 100, -8192, 8191, "0–127; pitch bend −8192 to 8191");
            if (!dialog.Ask()) return;
            SongCtrl ctrl;
            switch (Array.IndexOf(kinds, kind()))
            {
                case 1:
                    ctrl = new SongCtrl(tick, track.Channel, CtrlKind.Program, 0, Math.Clamp(number() - 1, 0, 127));
                    break;
                case 2:
                    ctrl = new SongCtrl(tick, track.Channel, CtrlKind.PitchBend, 0, Math.Clamp(value() + 8192, 0, 16383));
                    break;
                case 3:
                    ctrl = new SongCtrl(tick, track.Channel, CtrlKind.ChannelPressure, 0, Math.Clamp(value(), 0, 127));
                    break;
                default:
                    ctrl = new SongCtrl(tick, track.Channel, CtrlKind.Controller, Math.Clamp(number(), 0, 127), Math.Clamp(value(), 0, 127));
                    break;
            }
            Keep(ctrl);
            _main.Commit("Add event", () => track.SetControls(track.Controls.Append(ctrl)));
        }

        private void OnGridSelectionChanged()
        {
            if (_refreshing || SongView) return;
            var notes = _grid.SelectedItems.Cast<Row>().Select(r => r.Source).OfType<SongNote>().ToList();
            if (notes.Count > 0) _main.SelectNotes(notes);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_editing || Keyboard.FocusedElement is TextBox) return;
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            switch (e.Key)
            {
                case Key.Delete:
                    DeleteSelected();
                    e.Handled = true;
                    break;
                case Key.Z or Key.Y or Key.S when ctrl:
                case Key.Space:
                    _main.OnPreviewKeyDown(_main, e);
                    break;
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
            }
        }
    }
}
