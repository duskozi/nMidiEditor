using nMIDI.Model;
using nMIDI.Theory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace nMIDI
{
    /// <summary>
    /// The Process menu (quantize, transpose, length, velocity, humanize, reverse, invert, select by
    /// rule, remove duplicates) and the scale shown in the piano roll.
    /// </summary>
    public partial class MainWindow
    {
        private sealed record KeyChoice(int Key, string Label)
        {
            public override string ToString() => Label;
        }

        private sealed record Choice<T>(T Value, string Label)
        {
            public override string ToString() => Label;
        }

        private static readonly KeyChoice[] Keys = Enumerable.Range(0, 12).Select(k => new KeyChoice(k, NoteHelper.Pretty(k))).ToArray();

        // Settings the dialogs remember between uses.
        private GridChoice? _quantizeGrid;
        private QuantizeMode _quantizeMode = QuantizeMode.StartKeepLength;
        private int _quantizeStrength = 100, _quantizeSwing;
        private int _transposeAmount = 12;
        private bool _transposeInScale;
        private int _lengthPercent = 50;
        private int _humanizeTiming = 10, _humanizeVelocity = 8, _humanizeLength;
        private int _velocityPercent = 100, _velocityAdd, _rampFrom = 40, _rampTo = 110;
        private NoteRule _lastRule = new();

        private void InitProcess()
        {
            foreach (var k in Keys) cmbKey.Items.Add(k);
            cmbKey.SelectedIndex = 0;
            cmbScale.Items.Add("Off");
            foreach (var s in Scale.All.Where(s => s.Name != "Chromatic")) cmbScale.Items.Add(s);
            cmbScale.SelectedIndex = 0;
        }

        /// <summary>The scale chosen in the toolbar, or null.</summary>
        private Scale? CurrentScale => cmbScale?.SelectedItem as Scale;
        private int CurrentKey => (cmbKey?.SelectedItem as KeyChoice)?.Key ?? 0;

        private void OnScaleChanged(object sender, SelectionChangedEventArgs e)
        {
            if (viewer == null) return;
            tglScaleSnap.IsEnabled = CurrentScale != null;
            viewer.InvalidateElements();   // redraws the row shading
        }

        /// <summary>Drawn notes move to the nearest note of the scale when the snap button is on.</summary>
        public int SnapPitch(int note)
        {
            if (PianoRoll.PianoRollStyle.Rows.IsDrums) return note;   // drums: the row's drum, never a scale note
            return tglScaleSnap.IsChecked == true && CurrentScale is { } scale ? scale.Nearest(CurrentKey, note) : note;
        }

        // --- Menu ---

        private void OnProcessClick(object sender, RoutedEventArgs e)
        {
            var menu = BuildProcessMenu();
            menu.PlacementTarget = btnProcess;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private ContextMenu BuildProcessMenu()
        {
            var menu = new ContextMenu();
            foreach (var item in ProcessItems()) menu.Items.Add(item);
            return menu;
        }

        /// <summary>The Process commands, also used as a submenu of the right-click menu.</summary>
        private IEnumerable<object> ProcessItems()
        {
            bool track = CanEdit;
            yield return Item("Quantize…", "Q", track, ShowQuantize);
            yield return Item("Transpose…", "", track, ShowTranspose);
            yield return Item("Change length…", "", track, ShowLength);
            yield return Item("Velocity…", "", track, ShowVelocity);
            yield return Item("Humanize…", "", track, ShowHumanize);
            yield return new Separator();
            yield return Item("Legato (each note to the next)", "", track,
                              () => ProcessNotes("Legato", n => NoteProcessing.ChangeLength(n, LengthMode.Legato)));
            yield return Item("Remove overlaps of the same key", "", track,
                              () => ProcessNotes("Remove overlaps", n => NoteProcessing.ChangeLength(n, LengthMode.RemoveOverlaps)));
            yield return Item("Remove duplicate notes", "", track, RemoveDuplicates);
            yield return Item("Reverse (retrograde)", "", track, () => ProcessNotes("Reverse", NoteProcessing.Reverse));
            yield return Item("Invert (mirror the pitches)", "", track, () => ProcessNotes("Invert", n => NoteProcessing.Invert(n)));
            yield return new Separator();
            yield return Item("Select notes…", "", track, ShowSelectByRule);
        }

        /// <summary>
        /// Run a command on the selected notes, or on every note of the track when nothing is selected.
        /// The changed notes stay selected if they were.
        /// </summary>
        private void ProcessNotes(string label, Func<List<SongNote>, List<SongNote>> transform)
        {
            var track = ActiveTrack;
            if (track == null) return;
            bool selection = _editor.Selection.Count > 0;
            var notes = selection ? _editor.SelectedNotes() : track.Notes.ToList();
            if (notes.Count == 0) return;
            var changed = transform(notes);
            if (changed.Count == notes.Count && changed.Zip(notes).All(x => x.First == x.Second))
            {
                txtStatus.Text = $"{label}: nothing to change.";
                return;
            }
            var tracks = selection ? EditTracks : new[] { track };
            Commit(label, () => NoteOps.ReplaceInTracks(tracks, notes, changed, changed.Count == notes.Count ? notes : null, track));
            if (selection) _editor.SetSelection(changed);
            txtStatus.Text = $"{label}: {notes.Count:N0} notes{(selection ? "" : " (whole track)")}.";
        }

        private string Scope => _editor.Selection.Count > 0
            ? $"Changes the {_editor.Selection.Count:N0} selected notes."
            : $"Nothing is selected, so this changes all {ActiveTrack?.Notes.Count ?? 0:N0} notes of the track.";

        // --- Dialogs ---

        private void ShowQuantize()
        {
            if (!CanEdit) return;
            var grids = GridChoices.Where(g => !(g.Quarters <= 0 && !g.IsBar)).ToList();
            var current = _quantizeGrid ?? (cmbGrid.SelectedItem as GridChoice is { } g && grids.Contains(g) ? g : grids.First(x => x.Label == "1/16"));
            var modes = new[]
            {
                new Choice<QuantizeMode>(QuantizeMode.StartKeepLength, "Start (keep length)"),
                new Choice<QuantizeMode>(QuantizeMode.StartAndEnd, "Start and end"),
                new Choice<QuantizeMode>(QuantizeMode.EndOnly, "End only"),
            };

            var d = new FormDialog(this, "Quantize", Scope);
            var grid = d.AddChoice("Grid", grids, current);
            var mode = d.AddChoice("Move", modes, modes.First(m => m.Value == _quantizeMode));
            var strength = d.AddNumber("Strength %", _quantizeStrength, 1, 100, "100 = exactly on the grid");
            var swing = d.AddNumber("Swing %", _quantizeSwing, 0, 100, "0 = straight, 33 ≈ triplet feel");
            if (!d.Ask()) return;

            _quantizeGrid = grid();
            _quantizeMode = mode().Value;
            _quantizeStrength = strength();
            _quantizeSwing = swing();
            long ticks = ChoiceTicks(_quantizeGrid);
            ProcessNotes("Quantize", n => NoteProcessing.Quantize(n, ticks, _quantizeMode, _quantizeStrength / 100.0, _quantizeSwing / 100.0));
        }

        private void ShowTranspose()
        {
            if (!CanEdit) return;
            var scales = Scale.All.Where(s => s.Name != "Chromatic").ToList();
            var d = new FormDialog(this, "Transpose", Scope);
            var amount = d.AddNumber("Amount", _transposeAmount, -96, 96, "semitones, or scale steps");
            var inScale = d.AddCheck("Scale steps", _transposeInScale, "move by steps of the scale (diatonic)");
            var key = d.AddChoice("Key", Keys, Keys[CurrentKey]);
            var scale = d.AddChoice("Scale", scales, CurrentScale ?? scales[0]);
            if (!d.Ask()) return;

            _transposeAmount = amount();
            _transposeInScale = inScale();
            int n = _transposeAmount;
            if (_transposeInScale)
            {
                var s = scale();
                int k = key().Key;
                ProcessNotes($"Transpose {n:+0;-0} steps", notes => NoteProcessing.TransposeInScale(notes, n, s, k));
            }
            else ProcessNotes($"Transpose {n:+0;-0}", notes => NoteProcessing.Transpose(notes, n));
        }

        private void ShowLength()
        {
            if (!CanEdit) return;
            var lengths = GridChoices.Where(g => g.Quarters > 0 || g.IsBar).ToList();
            var modes = new[]
            {
                new Choice<LengthMode>(LengthMode.Set, "Set to a length"),
                new Choice<LengthMode>(LengthMode.Scale, "Scale by a percentage"),
                new Choice<LengthMode>(LengthMode.Legato, "Legato: each note to the next"),
                new Choice<LengthMode>(LengthMode.RemoveOverlaps, "Remove overlaps of the same key"),
            };
            var d = new FormDialog(this, "Change length", Scope);
            var mode = d.AddChoice("Mode", modes, modes[0]);
            var length = d.AddChoice("Length", lengths, lengths.First(g => g.Label == "1/8"), "for Set");
            var percent = d.AddNumber("Percentage", _lengthPercent, 1, 1000, "for Scale (50 = half, staccato)");
            if (!d.Ask()) return;

            _lengthPercent = percent();
            var m = mode().Value;
            long ticks = ChoiceTicks(length());
            double factor = _lengthPercent / 100.0;
            ProcessNotes(modes.First(x => x.Value == m).Label.Split(':')[0], notes => NoteProcessing.ChangeLength(notes, m, ticks, factor));
        }

        private void ShowVelocity()
        {
            if (!CanEdit) return;
            var modes = new[]
            {
                new Choice<int>(0, "Scale and add"),
                new Choice<int>(1, "Set all to one value"),
                new Choice<int>(2, "Ramp (crescendo / diminuendo)"),
            };
            var d = new FormDialog(this, "Velocity", Scope);
            var mode = d.AddChoice("Mode", modes, modes[0]);
            d.AddHeading("Scale and add");
            var percent = d.AddNumber("Scale %", _velocityPercent, 1, 400, "100 = unchanged");
            var add = d.AddNumber("Add", _velocityAdd, -127, 127, "after scaling");
            d.AddHeading("Set");
            var set = d.AddNumber("Velocity", _velocity, 1, 127);
            d.AddHeading("Ramp");
            var from = d.AddNumber("From", _rampFrom, 1, 127, "at the first note");
            var to = d.AddNumber("To", _rampTo, 1, 127, "at the last note");
            if (!d.Ask()) return;

            _velocityPercent = percent();
            _velocityAdd = add();
            _rampFrom = from();
            _rampTo = to();
            int value = set();
            switch (mode().Value)
            {
                case 0: ProcessNotes("Scale velocity", n => NoteProcessing.ScaleVelocity(n, _velocityPercent / 100.0, _velocityAdd)); break;
                case 1: ProcessNotes("Set velocity", n => NoteProcessing.ScaleVelocity(n, 0, value)); break;
                default: ProcessNotes("Velocity ramp", n => NoteProcessing.RampVelocity(n, _rampFrom, _rampTo)); break;
            }
        }

        private void ShowHumanize()
        {
            if (!CanEdit) return;
            var d = new FormDialog(this, "Humanize", Scope + " Each note moves by a random amount up to the values below.");
            var timing = d.AddNumber("Timing ± ticks", _humanizeTiming, 0, 10_000, $"{_song!.TicksPerQuarter} ticks = 1 beat");
            var velocity = d.AddNumber("Velocity ±", _humanizeVelocity, 0, 127);
            var length = d.AddNumber("Length ± ticks", _humanizeLength, 0, 10_000);
            if (!d.Ask()) return;

            _humanizeTiming = timing();
            _humanizeVelocity = velocity();
            _humanizeLength = length();
            int seed = Environment.TickCount;
            ProcessNotes("Humanize", n => NoteProcessing.Humanize(n, _humanizeTiming, _humanizeVelocity, _humanizeLength, seed));
        }

        private void ShowSelectByRule()
        {
            if (!CanEdit) return;
            var r = _lastRule;
            var actions = new[]
            {
                new Choice<int>(0, "Replace the selection"),
                new Choice<int>(1, "Add to the selection"),
                new Choice<int>(2, "Keep only matching selected notes"),
            };
            var channels = new[] { new Choice<int>(-1, "Any") }.Concat(Enumerable.Range(0, 16).Select(c => new Choice<int>(c, $"{c + 1}"))).ToList();
            var scaleUse = new[]
            {
                new Choice<int>(0, "Any note"),
                new Choice<int>(1, "Only notes in the scale"),
                new Choice<int>(2, "Only notes outside the scale"),
            };

            var d = new FormDialog(this, "Select notes", "Selects the notes of the edited track that match every condition.");
            var action = d.AddChoice("Selection", actions, actions[0]);
            d.AddHeading("Pitch and velocity");
            var lowPitch = d.AddText("Lowest note", NoteName(r.LowPitch), "C4, F#3 or 60");
            var highPitch = d.AddText("Highest note", NoteName(r.HighPitch));
            var lowVelocity = d.AddNumber("Velocity from", r.LowVelocity, 1, 127);
            var highVelocity = d.AddNumber("Velocity to", r.HighVelocity, 1, 127);
            d.AddHeading("Length, channel, position");
            var minLength = d.AddNumber("Length from (ticks)", (int)Math.Min(int.MaxValue, r.MinLength), 0, int.MaxValue, "0 = no limit");
            int maxLengthNow = r.MaxLength == long.MaxValue ? 0 : (int)Math.Min(int.MaxValue, r.MaxLength);
            var maxLength = d.AddNumber("Length to (ticks)", maxLengthNow, 0, int.MaxValue, "0 = no limit");
            var channel = d.AddChoice("Channel", channels, channels.First(c => c.Value == r.Channel));
            var nth = d.AddNumber("Every Nth note", r.EveryNth, 1, 64, "1 = every note");
            var offset = d.AddNumber("Starting with note", r.NthOffset + 1, 1, 64);
            var inScale = d.AddChoice("Scale", scaleUse, scaleUse[0], CurrentScale != null ? $"{Keys[CurrentKey]} {CurrentScale}" : "pick one in the toolbar");
            if (!d.Ask()) return;

            int low = ParsePitch(lowPitch()) ?? 0, high = ParsePitch(highPitch()) ?? 127;
            long maxLen = maxLength();
            _lastRule = new NoteRule(Math.Min(low, high), Math.Max(low, high), Math.Min(lowVelocity(), highVelocity()), Math.Max(lowVelocity(), highVelocity()),
                                     minLength(), maxLen == 0 ? long.MaxValue : maxLen, channel().Value, nth(), offset() - 1,
                                     inScale().Value == 1, inScale().Value == 2, CurrentScale, CurrentKey);

            var track = ActiveTrack!;
            List<SongNote> result = action().Value switch
            {
                1 => _editor.SelectedNotes().Concat(_lastRule.Apply(track.Notes)).ToList(),
                2 => _lastRule.Apply(_editor.SelectedNotes()),
                _ => _lastRule.Apply(track.Notes),
            };
            _editor.SetSelection(result);
            txtStatus.Text = $"{_editor.Selection.Count:N0} notes selected.";
        }

        /// <summary>Delete notes that have the same key, channel and start as an earlier one (keeping the longest).</summary>
        private void RemoveDuplicates()
        {
            var track = ActiveTrack;
            if (track == null) return;
            bool selection = _editor.Selection.Count > 0;
            var notes = selection ? _editor.SelectedNotes() : track.Notes.ToList();
            var duplicates = notes.GroupBy(n => (n.Number, n.Channel, n.Start))
                                  .SelectMany(g => g.OrderByDescending(n => n.Length).Skip(1))
                                  .ToList();
            if (duplicates.Count == 0)
            {
                txtStatus.Text = "No duplicate notes.";
                return;
            }
            var tracks = selection ? EditTracks : new[] { track };
            Commit("Remove duplicates", () => NoteOps.ReplaceInTracks(tracks, duplicates, Array.Empty<SongNote>(), null, track));
            txtStatus.Text = $"Removed {duplicates.Count:N0} duplicate notes.";
        }
    }
}
