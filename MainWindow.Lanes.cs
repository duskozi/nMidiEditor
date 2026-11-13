using nMIDI.Model;
using nMIDI.PianoRoll;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace nMIDI
{
    /// <summary>
    /// The lane under the piano roll (velocity, controllers, pitch bend, pressure, program changes)
    /// and the note fields in its header, which edit the selected notes by typing.
    /// </summary>
    public partial class MainWindow : ILaneHost
    {
        /// <summary>An entry of the lane list.</summary>
        private sealed record LaneItem(LaneMode Mode, string Label)
        {
            public override string ToString() => Label;
        }

        private GridLength _laneHeight = new(120);
        private bool _fillingLaneList;
        private readonly Dictionary<TextBox, string> _shownNoteText = new();

        HashSet<SongNote> ILaneHost.Selection => _editor.Selection;

        public Color ActiveColor => _activeVm?.Color ?? Colors.Gray;

        public void ShowLaneValue(string? text) => txtLaneValue.Text = text ?? "";

        private void AttachLane(FrameworkElement surface)
        {
            lane.Attach(this, viewer, surface);
            lane.SelectionRequested += (_, notes) => _editor.SetSelection(notes);
            FillLaneList();
            UpdateNoteFields();
        }

        // --- Lane list ---

        /// <summary>
        /// Fill the lane list: velocity, pitch bend, pressure and program change, then the controllers
        /// the edited track uses (marked ●), the common ones, and all the others.
        /// </summary>
        private void FillLaneList()
        {
            var keep = (cmbLane.SelectedItem as LaneItem)?.Mode ?? new LaneMode(LaneKind.Velocity);
            var controls = ActiveTrack?.Controls ?? Array.Empty<SongCtrl>();
            var usedCc = controls.Where(c => c.Kind == CtrlKind.Controller).Select(c => c.Number).ToHashSet();
            bool Uses(CtrlKind kind) => controls.Any(c => c.Kind == kind);
            string Mark(bool used, string label) => used ? "● " + label : label;

            _fillingLaneList = true;
            try
            {
                cmbLane.Items.Clear();
                cmbLane.Items.Add(new LaneItem(new LaneMode(LaneKind.Velocity), "Velocity"));
                cmbLane.Items.Add(new LaneItem(new LaneMode(LaneKind.PitchBend), Mark(Uses(CtrlKind.PitchBend), "Pitch bend")));
                cmbLane.Items.Add(new LaneItem(new LaneMode(LaneKind.Program), Mark(Uses(CtrlKind.Program), "Program change")));
                cmbLane.Items.Add(new LaneItem(new LaneMode(LaneKind.ChannelPressure), Mark(Uses(CtrlKind.ChannelPressure), "Channel pressure")));
                cmbLane.Items.Add(new LaneItem(new LaneMode(LaneKind.Tempo), Mark(_song != null && _song.Tempos.Count > 1, "Tempo (whole song)")));

                var order = usedCc.OrderBy(c => c)
                    .Concat(ControllerNames.Common.Where(c => !usedCc.Contains(c)))
                    .Concat(Enumerable.Range(0, 120).Where(c => !usedCc.Contains(c) && !ControllerNames.Common.Contains(c)));
                foreach (int cc in order)
                    cmbLane.Items.Add(new LaneItem(new LaneMode(LaneKind.Controller, cc), Mark(usedCc.Contains(cc), ControllerNames.Label(cc))));

                cmbLane.SelectedItem = cmbLane.Items.OfType<LaneItem>().FirstOrDefault(i => i.Mode == keep) ?? cmbLane.Items[0];
            }
            finally
            {
                _fillingLaneList = false;
            }
            if (cmbLane.SelectedItem is LaneItem item && item.Mode != lane.Mode) lane.SetMode(item.Mode);
        }

        private void OnLaneChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_fillingLaneList || cmbLane.SelectedItem is not LaneItem item) return;
            lane.SetMode(item.Mode);
        }

        private void OnLaneToggled(object sender, RoutedEventArgs e)
        {
            if (rowLane == null || lane == null || splitLane == null) return;   // during InitializeComponent
            bool open = tglLane.IsChecked == true;
            if (open)
            {
                rowLane.Height = _laneHeight;
                rowLane.MinHeight = 60;
            }
            else
            {
                _laneHeight = rowLane.Height;
                rowLane.MinHeight = 0;
                rowLane.Height = GridLength.Auto;
            }
            lane.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            splitLane.Visibility = lane.Visibility;
        }

        /// <summary>Choose a lane from the edit menu or a shortcut.</summary>
        private void ShowLane(LaneMode mode)
        {
            var item = cmbLane.Items.OfType<LaneItem>().FirstOrDefault(i => i.Mode == mode);
            if (item != null) cmbLane.SelectedItem = item;
            if (tglLane.IsChecked != true) tglLane.IsChecked = true;
        }

        // --- Note fields ---

        private IEnumerable<TextBox> NoteFields => new[] { txtNotePitch, txtNoteStart, txtNoteLength, txtNoteVelocity, txtNoteRelease, txtNoteChannel };

        /// <summary>Show the selected note (or what the selected notes have in common) in the fields.</summary>
        private void UpdateNoteFields()
        {
            var notes = _song != null ? _editor.SelectedNotes() : new List<SongNote>();
            pnlNote.IsEnabled = notes.Count > 0;

            static string Common(IEnumerable<int> values)
            {
                var distinct = values.Distinct().Take(2).ToList();
                return distinct.Count == 1 ? distinct[0].ToString(CultureInfo.InvariantCulture) : "";
            }

            var texts = notes.Count == 0
                ? new[] { "", "", "", "", "", "" }
                : new[]
                {
                    NoteName(notes.Min(n => n.Number)),
                    _song!.FormatBars(notes.Min(n => n.Start)).Trim(),
                    Common(notes.Select(n => (int)Math.Min(int.MaxValue, n.Length))),
                    Common(notes.Select(n => n.Velocity)),
                    Common(notes.Select(n => n.OffVelocity)),
                    Common(notes.Select(n => n.Channel + 1)),
                };

            int i = 0;
            foreach (var box in NoteFields)
            {
                string text = texts[i++];
                _shownNoteText[box] = text;
                if (!box.IsKeyboardFocused) box.Text = text;
            }
        }

        private void OnNoteFieldKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox box) return;
            if (e.Key == Key.Enter)
            {
                ApplyNoteField(box);
                box.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                box.Text = _shownNoteText.GetValueOrDefault(box, "");
                viewer.Focus();
                e.Handled = true;
            }
        }

        private void OnNoteFieldLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box && box.Text != _shownNoteText.GetValueOrDefault(box, "")) ApplyNoteField(box);
        }

        /// <summary>Apply a typed value to the selected notes. Pitch, start and velocity accept +n / −n.</summary>
        private void ApplyNoteField(TextBox box)
        {
            if (_song == null || _editor.Selection.Count == 0) return;
            string text = box.Text.Trim().Replace('−', '-');
            if (text.Length == 0 || text == _shownNoteText.GetValueOrDefault(box, ""))
            {
                box.Text = _shownNoteText.GetValueOrDefault(box, "");
                return;
            }
            bool relative = text.StartsWith('+') || (text.StartsWith('-') && text.Length > 1);
            int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number);
            bool isNumber = int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

            bool ok = true;
            switch (box.Tag as string)
            {
                case "Pitch":
                    if (relative && isNumber) Transpose(number);
                    else if (ParsePitch(text) is int pitch)
                        TransformSelection("Set pitch", notes => NoteOps.Move(notes, 0, pitch - notes.Min(n => n.Number)));
                    else ok = false;
                    break;

                case "Start":
                    if (relative && isNumber) TransformSelection("Move notes", notes => NoteOps.Move(notes, number, 0));
                    else if (ParsePosition(text) is long start)
                        TransformSelection("Set start", notes => NoteOps.Move(notes, start - notes.Min(n => n.Start), 0));
                    else ok = false;
                    break;

                case "Length":
                    if (relative && isNumber) TransformSelection("Change length", notes => NoteOps.ResizeEnds(notes, number, 1));
                    else if (isNumber && number > 0) TransformSelection("Set length", notes => NoteOps.SetLength(notes, number));
                    else ok = false;
                    break;

                case "Velocity":
                    if (!isNumber) ok = false;
                    else if (relative) SetSelectionVelocity(n => n.Velocity + number, "Change velocity");
                    else SetSelectionVelocity(_ => number, "Set velocity");
                    break;

                case "Release":
                    if (isNumber && !relative && number is >= 0 and <= 127)
                        TransformSelection("Set release velocity", notes => notes.Select(n => n with { OffVelocity = number }).ToList());
                    else ok = false;
                    break;

                case "Channel":
                    if (isNumber && !relative && number is >= 1 and <= 16)
                        TransformSelection("Set channel", notes => notes.Select(n => n with { Channel = number - 1 }).ToList());
                    else ok = false;
                    break;
            }

            if (!ok)
            {
                txtStatus.Text = $"'{box.Text}' is not a valid {((string)box.Tag).ToLowerInvariant()}.";
                box.Text = _shownNoteText.GetValueOrDefault(box, "");
            }
            UpdateNoteFields();
            box.Text = _shownNoteText.GetValueOrDefault(box, "");
        }

        private static readonly Regex PitchPattern = new(@"^([A-Ga-g])([#♯b♭]?)(-?\d+)$", RegexOptions.Compiled);

        /// <summary>A note name such as C4, F#3 or Bb2 (middle C = C4), or a note number.</summary>
        public static int? ParsePitch(string text)
        {
            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                return number is >= 0 and <= 127 ? number : null;
            var m = PitchPattern.Match(text.Trim());
            if (!m.Success) return null;
            int pc = char.ToUpperInvariant(m.Groups[1].Value[0]) switch { 'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, _ => 11 };
            pc += m.Groups[2].Value switch { "#" or "♯" => 1, "b" or "♭" => -1, _ => 0 };
            int pitch = (int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) + 1) * 12 + pc;
            return pitch is >= 0 and <= 127 ? pitch : null;
        }

        /// <summary>"bar.beat.tick", "bar.beat" or "bar" (counted from 1) as a tick.</summary>
        private long? ParsePosition(string text)
        {
            if (_song == null) return null;
            var parts = text.Split('.', ':');
            var numbers = new List<long>();
            foreach (var part in parts)
            {
                if (!long.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long n)) return null;
                numbers.Add(n);
            }
            if (numbers.Count is 0 or > 3 || numbers[0] < 1) return null;
            long bar = numbers[0] - 1;
            long beat = numbers.Count > 1 ? Math.Max(0, numbers[1] - 1) : 0;
            long tick = numbers.Count > 2 ? numbers[2] : 0;
            return _song.FromBarBeat(bar, beat, tick);
        }
    }
}
