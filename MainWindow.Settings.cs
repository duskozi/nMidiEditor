using NinePatchWpf;
using nMIDI.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace nMIDI
{
    /// <summary>The settings menu and window, the theme switch, and keeping the layout between sessions.</summary>
    public partial class MainWindow
    {
        private static readonly string[] KeyLabelNames = ["Fingers & hands", "Note names", "Nothing"];
        private static readonly string[] KeyRangeNames = ["88 keys", "Song range", "61 keys", "49 keys"];
        private static readonly string[] ChordModeNames = ["Real time (what sounds now)", "Hold the last chord until the next"];
        private static readonly string[] ColorModeNames = ["Track", "Velocity"];
        private static readonly string[] RecordModeNames = ["Overdub", "Replace"];
        private const string AutomaticPort = "(automatic: GS synth or first port)";

        /// <summary>Put the saved layout and defaults on the window (at start-up, after the editor is attached).</summary>
        private void ApplySavedSettings()
        {
            var s = _settings;
            _outputs.Preferred = s.OutputDevice;
            ApplyFingeringPrefs();

            _keyboardHeight = new GridLength(Math.Clamp(s.KeyboardHeight, 80, 500));
            if (tglKeyboard.IsChecked == true) rowKeyboard.Height = _keyboardHeight;
            tglKeyboard.IsChecked = s.ShowKeyboard;
            tglPedals.IsChecked = s.ShowPedals;
            UpdatePedalPanelVisibility();
            tglChords.IsChecked = s.ShowChords;
            UpdateChordVisibility();
            tglFalling.IsChecked = _practiceMode ? s.FallingInPractice : s.FallingInEdit;
            ApplyFalling();

            _laneHeight = new GridLength(Math.Clamp(s.LaneHeight, 60, 500));
            if (tglLane.IsChecked == true) rowLane.Height = _laneHeight;
            tglLane.IsChecked = s.ShowLane;
            tglTracks.IsChecked = s.ShowTracks;

            cmbKeyLabels.SelectedIndex = Math.Clamp(s.KeyLabels, 0, KeyLabelNames.Length - 1);
            cmbKeyRange.SelectedIndex = Math.Clamp(s.KeyRange, 0, KeyRangeNames.Length - 1);
            ApplyEditorDefaults();
            ApplyAutosaveInterval();
            HookScrollBars();
        }

        private void ApplyEditorDefaults()
        {
            var s = _settings;
            if (GridChoices.FirstOrDefault(g => g.Label == s.Grid) is { } grid) cmbGrid.SelectedItem = grid;
            if (LengthChoices.FirstOrDefault(g => g.Label == s.NoteLength) is { } length) cmbLength.SelectedItem = length;
            _velocity = Math.Clamp(s.Velocity, 1, 127);
            txtVelocity.Text = _velocity.ToString(CultureInfo.InvariantCulture);
            tglDimOthers.IsChecked = s.DimOthers;
            btnFollow.IsChecked = s.Follow;
            cmbColorMode.SelectedIndex = Math.Clamp(s.ColorMode, 0, ColorModeNames.Length - 1);
            BuildElements();
        }

        private void ApplyAutosaveInterval()
        {
            _autosaveTimer.Stop();
            if (_settings.AutosaveMinutes <= 0) return;
            _autosaveTimer.Interval = TimeSpan.FromMinutes(Math.Clamp(_settings.AutosaveMinutes, 1, 60));
            _autosaveTimer.Start();
        }

        /// <summary>Keep the layout and the toolbar choices for next time (called when the window closes).</summary>
        private void RememberLayout()
        {
            var s = _settings;
            s.ShowKeyboard = tglKeyboard.IsChecked == true;
            s.ShowPedals = tglPedals.IsChecked == true;
            s.ShowChords = tglChords.IsChecked == true;
            s.KeyboardHeight = s.ShowKeyboard ? rowKeyboard.ActualHeight : _keyboardHeight.Value;
            s.ShowLane = tglLane.IsChecked == true;
            s.LaneHeight = s.ShowLane ? rowLane.ActualHeight : _laneHeight.Value;
            s.ShowTracks = tglTracks.IsChecked == true;
            s.KeyLabels = Math.Max(0, cmbKeyLabels.SelectedIndex);
            s.KeyRange = Math.Max(0, cmbKeyRange.SelectedIndex);
            if (cmbGrid.SelectedItem is GridChoice grid) s.Grid = grid.Label;
            if (cmbLength.SelectedItem is GridChoice length) s.NoteLength = length.Label;
            s.Velocity = _velocity;
            s.DimOthers = tglDimOthers.IsChecked == true;
            s.Follow = btnFollow.IsChecked == true;
            s.ColorMode = Math.Max(0, cmbColorMode.SelectedIndex);
            s.Theme = ThemeManager.Current;
            s.Save();
        }

        // --- Menu ---

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            menu.Items.Add(Item("Settings…", "Ctrl+,", true, ShowSettings));
            var theme = new MenuItem { Header = "Theme" };
            foreach (var name in ThemeManager.Names)
            {
                var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = name == ThemeManager.Current };
                item.Click += (_, _) => SetTheme(name);
                theme.Items.Add(item);
            }
            menu.Items.Add(theme);
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Open the settings folder", "", true, OpenSettingsFolder));
            menu.PlacementTarget = btnSettings;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void SetTheme(string name)
        {
            ThemeManager.Apply(name);
            _settings.Theme = ThemeManager.Current;
            _settings.Save();
            HookScrollBars();
            txtStatus.Text = $"Theme: {ThemeManager.Current}.";
        }

        private static void OpenSettingsFolder()
        {
            try
            {
                Directory.CreateDirectory(AppSettings.Folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppSettings.Folder) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        }

        // --- Settings window ---

        private void ShowSettings()
        {
            var s = _settings;
            RememberLayout();   // the window shows the current layout
            var outputs = new[] { AutomaticPort }.Concat(_outputs.Names).ToList();
            FillInputs();
            var inputs = cmbInput.Items.OfType<string>().ToList();

            var d = new FormDialog(this, "Settings",
                "Changes apply when you press OK. The layout (keyboard and lane height, toolbar choices) is also remembered when you close the editor.");
            d.AddHeading("Appearance");
            var theme = d.AddChoice("Theme", ThemeManager.Names, ThemeManager.Current, "Studio, Light classic, Retro hardware, High contrast, Alloy, Alloy night or Alloy ember");

            d.AddHeading("MIDI");
            var output = d.AddChoice("Default output", outputs, s.OutputDevice != null && outputs.Contains(s.OutputDevice) ? s.OutputDevice : AutomaticPort);
            var applyOutput = d.AddCheck("", false, "Also use it for every track of the open song");
            var input = d.AddChoice("Default input", inputs, _input.Name ?? NoInput);
            var thru = d.AddCheck("Thru", s.Thru, "Play the input on the edited track's port");

            d.AddHeading("Recording");
            var mode = d.AddChoice("Mode", RecordModeNames, s.ReplaceWhenRecording ? "Replace" : "Overdub");
            var countIn = d.AddCheck("Count-in", s.CountIn, "One bar of metronome before recording");
            var quantize = d.AddCheck("Quantize", s.QuantizeRecording, "Quantize each take to the grid");

            d.AddHeading("Files");
            var autosave = d.AddNumber("Autosave every", s.AutosaveMinutes, 0, 60, "minutes, 0 = off");
            var reopen = d.AddCheck("At start-up", s.ReopenLast, "Open the last file again");
            var bpm = d.AddNumber("New song tempo", s.NewSongBpm, 20, 300, "BPM");

            d.NextColumn();
            d.AddHeading("Piano keyboard");
            var showKeyboard = d.AddCheck("Show", s.ShowKeyboard, "Keyboard under the piano roll");
            var keyboardHeight = d.AddNumber("Height", (int)Math.Round(s.KeyboardHeight), 80, 500, "pixels, 80 to 500");
            var showPedals = d.AddCheck("Pedals", s.ShowPedals, "Pedals next to the keyboard, pressed while the song plays");
            var labels = d.AddChoice("Labels", KeyLabelNames, KeyLabelNames[Math.Clamp(s.KeyLabels, 0, 2)]);
            var range = d.AddChoice("Range", KeyRangeNames, KeyRangeNames[Math.Clamp(s.KeyRange, 0, 3)]);
            var chordMode = d.AddChoice("Chord name", ChordModeNames, ChordModeNames[s.ChordHold ? 1 : 0]);

            d.AddHeading("Lane");
            var showLane = d.AddCheck("Show", s.ShowLane, "Velocity and controller lane");
            var laneHeight = d.AddNumber("Height", (int)Math.Round(s.LaneHeight), 60, 500, "pixels, 60 to 500");

            d.AddHeading("Editor");
            var grid = d.AddChoice("Grid", GridChoices.Select(g => g.Label), GridChoices.Any(g => g.Label == s.Grid) ? s.Grid : "1/16");
            string lengthNow = LengthChoices.Any(g => g.Label == s.NoteLength) ? s.NoteLength : "Last";
            var length = d.AddChoice("Note length", LengthChoices.Select(g => g.Label), lengthNow);
            var velocity = d.AddNumber("Velocity", s.Velocity, 1, 127, "of new notes");
            var dim = d.AddCheck("Other tracks", s.DimOthers, "Dim the notes of the other tracks");
            var pedalStrip = d.AddCheck("Pedals", s.ShowPedalStrip, "Show sustain, sostenuto and soft pedal along the bottom of the piano roll");
            var follow = d.AddCheck("Playhead", s.Follow, "Follow the playhead while playing");
            var colors = d.AddChoice("Note colour", ColorModeNames, ColorModeNames[Math.Clamp(s.ColorMode, 0, 1)]);

            d.NextColumn();
            d.AddHeading("Fingering");
            var charts = d.AddCheck("Scale runs", s.FingerCharts, "Prefer the standard scale fingering");
            var repeats = d.AddCheck("Fast repeats", s.FingerRepeats, "Change fingers on fast repeated notes (3-2-1)");
            var hands = d.AddChoice("Hands", HandsFromNames, HandsFromNames[Math.Clamp(s.FingerHands, 0, 2)]);
            var onNotes = d.AddChoice("Fingers on notes", FingersOnNotesNames, FingersOnNotesNames[Math.Clamp(s.FingersOnNotes, 0, 2)]);
            var saveFingering = d.AddCheck("Save in MIDI file", s.SaveFingering, "Finger corrections and fingering modes, as text events");

            if (!d.Ask()) return;

            // Fingering
            s.FingerCharts = charts();
            s.FingerRepeats = repeats();
            s.FingerHands = Array.IndexOf(HandsFromNames, hands());
            s.FingersOnNotes = Array.IndexOf(FingersOnNotesNames, onNotes());
            s.SaveFingering = saveFingering();
            ApplyFingeringPrefs();
            UpdateFingerOverlay();

            // Appearance
            if (theme() != ThemeManager.Current) SetTheme(theme());

            // MIDI
            string? port = output() == AutomaticPort ? null : output();
            s.OutputDevice = port;
            _outputs.Preferred = port;
            if (applyOutput() && _song != null && _outputs.DefaultName is { } chosen) cmbAllOutputs.SelectedItem = chosen;
            else if (_song == null) { _noSongPort = null; SetAllOutputsSelection(); }
            if (input() != (string?)cmbInput.SelectedItem) cmbInput.SelectedItem = input();   // opens the port and saves it
            tglThru.IsChecked = thru();
            OnThruClick(tglThru, new RoutedEventArgs());

            // Recording and files
            s.ReplaceWhenRecording = mode() == "Replace";
            s.CountIn = countIn();
            s.QuantizeRecording = quantize();
            s.AutosaveMinutes = autosave();
            s.ReopenLast = reopen();
            s.NewSongBpm = bpm();
            ApplyAutosaveInterval();

            // Layout
            s.ShowKeyboard = showKeyboard();
            s.ShowPedals = showPedals();
            tglPedals.IsChecked = s.ShowPedals;
            UpdatePedalPanelVisibility();
            tglChords.IsChecked = s.ShowChords;
            s.ChordHold = chordMode() == ChordModeNames[1];
            UpdateChordVisibility();
            ResetLiveChord();
            tglFalling.IsChecked = _practiceMode ? s.FallingInPractice : s.FallingInEdit;
            ApplyFalling();
            s.KeyboardHeight = keyboardHeight();
            s.KeyLabels = Array.IndexOf(KeyLabelNames, labels());
            s.KeyRange = Array.IndexOf(KeyRangeNames, range());
            s.ShowLane = showLane();
            s.LaneHeight = laneHeight();
            ApplyPanelSizes();

            // Editor
            s.Grid = grid();
            s.NoteLength = length();
            s.Velocity = velocity();
            s.DimOthers = dim();
            s.ShowPedalStrip = pedalStrip();
            RefreshPedals();
            s.Follow = follow();
            s.ColorMode = Array.IndexOf(ColorModeNames, colors());
            ApplyEditorDefaults();

            s.Save();
            txtStatus.Text = "Settings saved.";
        }

        private void ApplyPanelSizes()
        {
            var s = _settings;
            _keyboardHeight = new GridLength(Math.Clamp(s.KeyboardHeight, 80, 500));
            tglKeyboard.IsChecked = s.ShowKeyboard;
            if (s.ShowKeyboard) rowKeyboard.Height = _keyboardHeight;
            _laneHeight = new GridLength(Math.Clamp(s.LaneHeight, 60, 500));
            tglLane.IsChecked = s.ShowLane;
            if (s.ShowLane) rowLane.Height = _laneHeight;
            cmbKeyLabels.SelectedIndex = Math.Clamp(s.KeyLabels, 0, KeyLabelNames.Length - 1);
            cmbKeyRange.SelectedIndex = Math.Clamp(s.KeyRange, 0, KeyRangeNames.Length - 1);
        }

        /// <summary>A new song with the tempo from the settings.</summary>
        private Song NewSongFromSettings()
        {
            var song = Song.CreateEmpty();
            if (_settings.NewSongBpm != 120)
            {
                song.Tempos = new[] { new TempoChange(0, TempoChange.FromBpm(_settings.NewSongBpm)) };
                song.Rebuild();
            }
            return song;
        }

        /// <summary>Open the last file at start-up when the settings ask for it.</summary>
        private void ReopenLastFile()
        {
            if (!_settings.ReopenLast || _song != null) return;
            var last = _settings.RecentFiles.FirstOrDefault();
            if (last != null && File.Exists(last)) Open(last);
        }

        // --- Piano roll scroll bars ---

        /// <summary>Give the zoom scroll bars of the piano roll the theme's thumb colour and small corners.</summary>
        private void HookScrollBars()
        {
            StyleScrollBars(viewer);
            Dispatcher.BeginInvoke(new Action(() => StyleScrollBars(viewer)), DispatcherPriority.Loaded);
            if (_tracksWindow != null) StyleScrollBars(_tracksWindow);
        }

        /// <summary>Set the corner radius and thumb brushes of every zoom scroll bar under an element.</summary>
        internal static void StyleScrollBars(DependencyObject root)
        {
            foreach (var bar in Descendants(root).OfType<ZoomScrollBar>())
            {
                var radius = ZoomScrollBar.CornerRadiusProperty;
                double r = Application.Current.TryFindResource("ScrollThumbRadius") is double d ? d : 2;
                if (radius.PropertyType == typeof(double)) bar.SetValue(radius, r);
                else if (radius.PropertyType == typeof(CornerRadius)) bar.SetValue(radius, new CornerRadius(r));
                bar.SetResourceReference(ZoomScrollBar.ThumbBrushProperty, "ScrollThumbBrush");
                bar.SetResourceReference(ZoomScrollBar.ThumbHoverBrushProperty, "ScrollThumbHoverBrush");
                bar.SetResourceReference(ZoomScrollBar.TrackBrushProperty, "ScrollTrackBrush");
            }
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            int count = root is Visual || root is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(root) : 0;
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var d in Descendants(child)) yield return d;
            }
        }
    }
}
