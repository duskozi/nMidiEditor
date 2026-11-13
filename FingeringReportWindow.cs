using Microsoft.Win32;
using nMIDI.Model;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    /// How good the fingering is, compared with what the file says: hands against separate LH/RH tracks,
    /// fingers against fingers written in the file (or a PIG file), the user's corrections, and stability.
    /// Differences can be outlined in the piano roll and stepped through; selected notes get a finger with 1–5.
    /// </summary>
    public sealed class FingeringReportWindow : Window
    {
        private readonly MainWindow _main;
        private readonly ComboBox _part = new() { Height = 26, MinWidth = 320 };
        private readonly Grid _summary = new() { Margin = new Thickness(12, 4, 12, 8) };
        private readonly DataGrid _bars = new();
        private readonly TextBlock _status = new();
        private readonly ToggleButton _showDiffs;
        private readonly Button _prev, _next;
        private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(700) };
        private CancellationTokenSource? _cancel;
        private FingeringReport? _report;
        private int _current = -1;
        private Dictionary<SongNote, TruthFinger>? _pig;
        private string? _pigName;
        private FingeringGroup? _pigGroup;

        public FingeringReportWindow(MainWindow main)
        {
            _main = main;
            Owner = main;
            Title = "Fingering report";
            Icon = AppIcons.FingeringReport;
            Width = 760;
            Height = 640;
            MinWidth = 560;
            MinHeight = 420;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Res<Brush>("Bg1");
            Foreground = Res<Brush>("Text");
            FontFamily = Res<FontFamily>("UiFont");
            FontSize = 12;
            UseLayoutRounding = true;

            _part.SelectionChanged += (_, _) => { _pig = null; _pigName = null; Recompute(); };
            var pig = MakeButton("Compare with PIG file…",
                "Load a PIG fingering file (the PIG dataset format) as the truth instead of the fingers written in the MIDI file");
            pig.Click += (_, _) => LoadPig();
            var refresh = MakeButton("Refresh", "Compute the report again");
            refresh.Click += (_, _) => Recompute();

            var top = new DockPanel { Margin = new Thickness(12, 12, 12, 4), LastChildFill = false };
            top.Children.Add(Caption("PART", 8));
            top.Children.Add(_part);
            foreach (var b in new[] { refresh, pig })
            {
                DockPanel.SetDock(b, Dock.Right);
                b.Margin = new Thickness(6, 0, 0, 0);
                top.Children.Add(b);
            }

            _summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            _summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            _summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            BuildBars();

            _showDiffs = new ToggleButton { Content = "Show differences", Style = Res<Style>("ToolToggle"), Height = 26, Padding = new Thickness(10, 0, 10, 0),
                ToolTip = "Outline in red the notes where the fingering differs from the file (or the PIG file)" };
            _showDiffs.Click += (_, _) => PushDifferences();
            _prev = MakeButton("◀ Previous", "Go to the previous difference");
            _prev.Click += (_, _) => Step(-1);
            _next = MakeButton("Next ▶", "Go to the next difference");
            _next.Click += (_, _) => Step(1);

            var fingerBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            fingerBar.Children.Add(Caption("SET FINGER", 6));
            for (int f = 1; f <= 5; f++)
            {
                int finger = f;
                var b = MakeButton(f.ToString(), $"Fix finger {f} on the selected notes (Shift+{f} in the piano roll)");
                b.MinWidth = 28;
                b.Margin = new Thickness(3, 0, 0, 0);
                b.Click += (_, _) => _main.SelectFinger(finger);
                fingerBar.Children.Add(b);
            }
            var clear = MakeButton("Clear", "Remove the correction from the selected notes (Shift+0)");
            clear.Margin = new Thickness(3, 0, 0, 0);
            clear.Click += (_, _) => _main.SelectFinger(0);
            fingerBar.Children.Add(clear);

            var bottom = new DockPanel { Margin = new Thickness(12, 8, 12, 4) };
            var nav = new StackPanel { Orientation = Orientation.Horizontal };
            nav.Children.Add(_showDiffs);
            _prev.Margin = new Thickness(6, 0, 0, 0);
            _next.Margin = new Thickness(3, 0, 0, 0);
            nav.Children.Add(_prev);
            nav.Children.Add(_next);
            DockPanel.SetDock(nav, Dock.Left);
            bottom.Children.Add(nav);
            bottom.Children.Add(fingerBar);

            _status.Foreground = Res<Brush>("TextDim");
            _status.Margin = new Thickness(12, 4, 12, 10);
            _status.TextWrapping = TextWrapping.Wrap;

            var root = new DockPanel();
            foreach (var (el, dock) in new (UIElement, Dock)[] { (top, Dock.Top), (_summary, Dock.Top), (_status, Dock.Bottom), (bottom, Dock.Bottom) })
            {
                DockPanel.SetDock(el, dock);
                root.Children.Add(el);
            }
            root.Children.Add(_bars);
            Content = root;

            _debounce.Tick += (_, _) => { _debounce.Stop(); Recompute(); };
            SourceInitialized += (_, _) => FormDialog.DarkTitleBar(this);
            Loaded += (_, _) => ShowSong();
            Closed += (_, _) => _cancel?.Cancel();
        }

        private static T Res<T>(string key) => (T)Application.Current.FindResource(key);

        private static TextBlock Caption(string text, double gap) => new()
        {
            Text = text,
            Style = Res<Style>("CapsLabel"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, gap, 0),
        };

        private static Button MakeButton(string text, string tip) =>
            new() { Content = text, ToolTip = tip, Height = 26, MinWidth = 64, Padding = new Thickness(10, 0, 10, 0), Style = Res<Style>("ToolButton") };

        private void BuildBars()
        {
            _bars.AutoGenerateColumns = false;
            _bars.IsReadOnly = true;
            _bars.CanUserAddRows = false;
            _bars.SelectionMode = DataGridSelectionMode.Single;
            _bars.HeadersVisibility = DataGridHeadersVisibility.Column;
            _bars.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            _bars.HorizontalGridLinesBrush = Res<Brush>("LineSoft");
            _bars.Background = Res<Brush>("Bg0");
            _bars.RowBackground = Res<Brush>("Bg0");
            _bars.AlternatingRowBackground = Res<Brush>("Bg1");
            _bars.Foreground = Res<Brush>("Text");
            _bars.BorderBrush = Res<Brush>("Line");
            _bars.BorderThickness = new Thickness(0, 1, 0, 1);
            _bars.RowHeight = 22;
            _bars.Margin = new Thickness(12, 0, 12, 0);
            var header = new Style(typeof(DataGridColumnHeader));
            header.Setters.Add(new Setter(BackgroundProperty, Res<Brush>("Bg2")));
            header.Setters.Add(new Setter(ForegroundProperty, Res<Brush>("TextDim")));
            header.Setters.Add(new Setter(PaddingProperty, new Thickness(6, 4, 6, 4)));
            header.Setters.Add(new Setter(BorderBrushProperty, Res<Brush>("Line")));
            header.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            _bars.ColumnHeaderStyle = header;
            var cell = new Style(typeof(DataGridCell));
            cell.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
            cell.Setters.Add(new Setter(PaddingProperty, new Thickness(4, 0, 4, 0)));
            var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(BackgroundProperty, Res<Brush>("AccentSoft")));
            selected.Setters.Add(new Setter(ForegroundProperty, Res<Brush>("Text")));
            cell.Triggers.Add(selected);
            _bars.CellStyle = cell;
            void Column(string h, string path, double width) => _bars.Columns.Add(new DataGridTextColumn
            {
                Header = h,
                Binding = new Binding(path),
                Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star),
            });
            Column("Bar", nameof(BarRow.Bar), 60);
            Column("Notes", nameof(BarRow.Notes), 70);
            Column("Hand ≠ file", nameof(BarRow.HandDiffs), 100);
            Column("Finger ≠ written", nameof(BarRow.FingerDiffs), 120);
            Column("Finger match", nameof(BarRow.FingerMatch), 100);
            Column("Corrections", nameof(BarRow.Corrections), 0);
            _bars.MouseDoubleClick += (_, _) =>
            {
                if (_bars.SelectedItem is BarRow row && _report != null)
                {
                    var first = _report.Group.Tracks
                        .SelectMany(t => t.Notes.Select(n => (t, n)))
                        .Where(x => x.n.Start >= row.Tick)
                        .OrderBy(x => x.n.Start)
                        .FirstOrDefault();
                    if (first.n != null) _main.RevealNote(first.t, first.n);
                }
            };
        }

        /// <summary>A new song was loaded, or the window opened: list its fingered parts.</summary>
        internal void ShowSong()
        {
            var song = _main.CurrentSong;
            var keep = (_part.SelectedItem as PartItem)?.Group.Tracks.FirstOrDefault();
            _part.Items.Clear();
            if (song == null) return;
            foreach (var g in song.Fingering.Groups) _part.Items.Add(new PartItem(g));
            var active = _main.ActiveTrack;
            var pick = _part.Items.Cast<PartItem>().FirstOrDefault(p => p.Group.Tracks.Contains(keep ?? active!))
                       ?? _part.Items.Cast<PartItem>().FirstOrDefault(p => active != null && p.Group.Tracks.Contains(active))
                       ?? _part.Items.Cast<PartItem>().FirstOrDefault();
            _part.SelectedItem = pick;
            if (pick == null) Show(null, "No track of this song is fingered. Set a fingering mode in Tracks ▾ → Fingering.");
        }

        /// <summary>The fingering changed (an edit, a correction, new settings): compute again shortly.</summary>
        internal void ResultsChanged()
        {
            var song = _main.CurrentSong;
            if (song != null)
            {
                // The groups may have changed (a mode or pair was set).
                var now = song.Fingering.Groups.Select(g => string.Join(",", g.Tracks.Select(t => t.Index))).ToList();
                var shown = _part.Items.Cast<PartItem>().Select(p => string.Join(",", p.Group.Tracks.Select(t => t.Index))).ToList();
                if (!now.SequenceEqual(shown)) { ShowSong(); return; }
            }
            _debounce.Stop();
            _debounce.Start();
        }

        private sealed record PartItem(FingeringGroup Group)
        {
            public override string ToString() => Group.Tracks.Length > 1
                ? Group.Describe()
                : $"{Group.Tracks[0].Index + 1}. {Group.Tracks[0].Name} ({Group.Describe()})";
        }

        private void LoadPig()
        {
            if (_part.SelectedItem is not PartItem item) return;
            var dialog = new OpenFileDialog { Title = "Open PIG fingering file", Filter = "PIG fingering files (*.txt)|*.txt|All files|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                _pig = FingeringReport.MatchPig(dialog.FileName, item.Group, out int total);
                _pigGroup = item.Group;
                _pigName = $"{System.IO.Path.GetFileName(dialog.FileName)}: {_pig.Count:N0} of {total:N0} notes matched";
                Recompute();
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _status.Text = "Could not read the PIG file: " + ex.Message;
            }
        }

        private void Recompute()
        {
            var song = _main.CurrentSong;
            if (song == null || _part.SelectedItem is not PartItem item) return;
            // The group object is rebuilt when tracks change; use the current one for the same tracks.
            var group = song.Fingering.Groups.FirstOrDefault(g => g.Tracks.SequenceEqual(item.Group.Tracks)) ?? item.Group;
            var pig = _pig != null && _pigGroup != null && _pigGroup.Tracks.SequenceEqual(group.Tracks) ? _pig : null;
            _cancel?.Cancel();
            var cancel = new CancellationTokenSource();
            _cancel = cancel;
            _status.Text = "Computing…";
            Task.Run(() => FingeringReport.Build(song, group, pig, cancel.Token), cancel.Token).ContinueWith(t =>
            {
                if (cancel.IsCancellationRequested || !IsLoaded) return;
                if (t.IsFaulted) { _status.Text = "The report could not be computed: " + t.Exception?.GetBaseException().Message; return; }
                if (t.IsCompletedSuccessfully) Show(t.Result, null);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void Show(FingeringReport? r, string? message)
        {
            _report = r;
            _summary.Children.Clear();
            _summary.RowDefinitions.Clear();
            _bars.ItemsSource = r?.Bars;
            if (r == null) { _status.Text = message ?? ""; PushDifferences(); return; }

            void Line(string label, string value, string explain)
            {
                int row = _summary.RowDefinitions.Count;
                _summary.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var a = new TextBlock { Text = label, Foreground = Res<Brush>("TextDim"), Margin = new Thickness(0, 3, 8, 3) };
                var b = new TextBlock
                {
                    Text = value,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = Res<FontFamily>("MonoFont"),
                    Margin = new Thickness(0, 3, 8, 3),
                };
                var c = new TextBlock
                {
                    Text = explain,
                    Foreground = Res<Brush>("TextFaint"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 3),
                    FontSize = 11,
                };
                Grid.SetRow(a, row); Grid.SetRow(b, row); Grid.SetRow(c, row);
                Grid.SetColumn(b, 1); Grid.SetColumn(c, 2);
                _summary.Children.Add(a); _summary.Children.Add(b); _summary.Children.Add(c);
            }
            string Value(Score s, string none) => s.Total == 0 ? none : s.ToString();

            Line("Notes", r.Notes.ToString("N0", CultureInfo.CurrentCulture), r.Group.Describe());
            Line("Hands agree with the file", Value(r.HandAgreement, "no hands in the file"),
                r.HandAgreement.Total > 0
                    ? "The algorithm's own split (file hands ignored) against the file's LH/RH tracks."
                    : "The file gives no hands for this part (one track, not named LH or RH).");
            string truth = _pigName ?? (r.TruthSource == "" ? "none" : r.TruthSource);
            Line("Fingers match", Value(r.FingerMatch, "no written fingers"),
                r.FingerMatch.Total > 0
                    ? $"Shown fingers against {truth}."
                    : "The file has no fingers written as text or lyric events (1–5); load a PIG file to compare.");
            Line("Hand and finger both right", Value(r.BothRight, "–"), "Where the written finger has a hand (L3, R2, PIG) or the file gives the hands.");
            Line("Agrees with your corrections", Value(r.Corrections, "no corrections"),
                r.Corrections.Total > 0
                    ? "The engine's own choice, before your correction, was the same finger."
                    : "Select notes and press Shift+1…5 to fix a finger.");
            Line("Stability", Value(r.Stability, "–"), "The fingering shown while editing (worked out around each edit) against a fresh full run.");
            Line("From scale charts", Value(r.Chart, "–"), "Notes fingered with the standard fingering of a recognised scale run.");

            _current = -1;
            _status.Text = $"{r.Differences.Count:N0} differences.  Double-click a bar to go there.  Computed in {r.Elapsed.TotalMilliseconds:N0} ms."
                + (_pigName != null ? "  PIG: " + _pigName : "");
            PushDifferences();
        }

        private void PushDifferences()
        {
            bool on = _showDiffs.IsChecked == true && _report != null;
            _prev.IsEnabled = _next.IsEnabled = on && _report!.Differences.Count > 0;
            if (!on) { _main.ShowDifferences(null, null); return; }
            var set = new HashSet<SongNote>(_report!.Differences.Select(d => d.Note), ReferenceEqualityComparer.Instance);
            var current = _current >= 0 && _current < _report.Differences.Count ? _report.Differences[_current].Note : null;
            _main.ShowDifferences(set, current);
        }

        private void Step(int direction)
        {
            if (_report == null || _report.Differences.Count == 0) return;
            int count = _report.Differences.Count;
            _current = _current < 0 ? (direction > 0 ? 0 : count - 1) : (_current + direction + count) % count;
            var d = _report.Differences[_current];
            _main.RevealNote(d.Track, d.Note);
            _status.Text = $"Difference {_current + 1} of {count}, bar {(_main.CurrentSong?.FormatBars(d.Note.Start).Trim() ?? "")}: {d.Why}.";
            PushDifferences();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
            base.OnPreviewKeyDown(e);
        }
    }
}
