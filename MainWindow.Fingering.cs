using nMIDI.Model;
using nMIDI.PianoRoll;
using nMIDI.Theory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace nMIDI
{
    /// <summary>Fingers on notes, fingering modes and pairs, finger corrections and the fingering report.</summary>
    public partial class MainWindow
    {
        internal static readonly string[] FingersOnNotesNames = { "Off", "Edited track", "All fingered tracks" };
        internal static readonly string[] HandsFromNames = { "From the tracks when the file has them", "From the tracks always", "Chosen by the algorithm" };

        private Song? _fingeringSong;
        private FingeringReportWindow? _report;
        private bool _fingeringRedrawQueued;

        /// <summary>Called when a song is shown: follow its fingering results.</summary>
        private void AttachFingering(Song song)
        {
            if (_fingeringSong != null) _fingeringSong.Fingering.Updated -= OnFingeringUpdated;
            _fingeringSong = song;
            song.Fingering.Updated += OnFingeringUpdated;
            fingerOverlay.Viewer = viewer;
            fingerOverlay.Differences = null;
            _report?.ShowSong();
            UpdateFingerOverlay();
        }

        private void OnFingeringUpdated(object? sender, EventArgs e)
        {
            if (_fingeringRedrawQueued) return;
            _fingeringRedrawQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _fingeringRedrawQueued = false;
                _keyboardSignature = 0;
                fingerOverlay.InvalidateVisual();
                UpdateFingeringButton();
                _report?.ResultsChanged();
            }));
        }

        /// <summary>Which tracks show their fingers on the notes (settings: off, edited track, all).</summary>
        private void UpdateFingerOverlay()
        {
            if (_song == null) { fingerOverlay.Tracks = Array.Empty<SongTrack>(); fingerOverlay.InvalidateVisual(); return; }
            var groups = _song.Fingering.Groups;
            var fingered = groups.SelectMany(g => g.Tracks).ToHashSet();
            fingerOverlay.Tracks = _settings.FingersOnNotes switch
            {
                1 => EditTracks.Where(fingered.Contains).ToArray(),
                2 => Tracks.Where(vm => vm.ShowNotes && fingered.Contains(vm.Track)).Select(vm => vm.Track).ToArray(),
                _ => Array.Empty<SongTrack>(),
            };
            fingerOverlay.InvalidateVisual();
            UpdateFingeringButton();
        }

        private void UpdateFingeringButton()
        {
            if (txtFingering == null) return;
            var track = ActiveTrack;
            var group = track != null ? _song?.Fingering.GroupOf(track) : null;
            txtFingering.Text = track == null ? "Fingering" : group == null ? "Fingering: off" : group.Tracks.Length > 1 ? "Fingering: paired" :
                group.Hands[0] switch { Hand.Left => "Fingering: LH", Hand.Right => "Fingering: RH", _ => "Fingering: both hands" };
        }

        private void OnFingeringMenuClick(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            foreach (var item in FingeringItems(ActiveTrack)) menu.Items.Add(item);
            menu.PlacementTarget = btnFingering;
            menu.Placement = PlacementMode.Top;
            menu.IsOpen = true;
        }

        /// <summary>"Fingering" submenu for the tracks menu.</summary>
        private MenuItem FingeringMenu(SongTrack? track)
        {
            var root = new MenuItem { Header = "Fingering" };
            foreach (var item in FingeringItems(track)) root.Items.Add(item);
            return root;
        }

        private List<object> FingeringItems(SongTrack? track)
        {
            var items = new List<object>();
            if (_song == null) return items;
            bool has = track != null;
            var info = track?.FingeringInfo ?? TrackFingeringInfo.Empty;
            var group = track != null ? _song.Fingering.GroupOf(track) : null;

            items.Add(new MenuItem
            {
                Header = track == null ? "No track" : $"{track.Index + 1}. {track.Name}: " + (group?.Describe() ?? "not fingered"),
                IsEnabled = false,
            });
            MenuItem Mode(string header, FingerMode mode, string tip)
            {
                var m = new MenuItem { Header = header, IsCheckable = true, IsChecked = has && info.Mode == mode, IsEnabled = has, ToolTip = tip };
                m.Click += (_, _) => SetFingerMode(track!, mode, mode == FingerMode.Off ? -1 : info.Pair);
                return m;
            }
            items.Add(Mode("Auto", FingerMode.Auto, "Keyboard instruments only. LH/RH tracks, or the only two piano tracks, are fingered together as a pair."));
            items.Add(Mode("Both hands", FingerMode.BothHands, "The algorithm splits the notes between the hands"));
            items.Add(Mode("Left hand", FingerMode.LeftHand, "Every note of this track is played by the left hand"));
            items.Add(Mode("Right hand", FingerMode.RightHand, "Every note of this track is played by the right hand"));
            items.Add(Mode("Off", FingerMode.Off, "No fingers for this track"));

            var pair = new MenuItem { Header = "Paired with", IsEnabled = has && info.Mode != FingerMode.Off };
            var none = new MenuItem { Header = "No chosen pair (automatic)", IsCheckable = true, IsChecked = info.Pair < 0 };
            none.Click += (_, _) => SetFingerMode(track!, info.Mode, -1);
            pair.Items.Add(none);
            pair.Items.Add(new Separator());
            foreach (var other in _song.Tracks)
            {
                if (other == track || other.Notes.Count == 0 || other.IsDrums) continue;
                var o = other;
                var m = new MenuItem { Header = $"{o.Index + 1}. {o.Name}", IsCheckable = true, IsChecked = info.Pair == o.Index };
                m.Click += (_, _) => SetFingerMode(track!, info.Mode, o.Index);
                pair.Items.Add(m);
            }
            items.Add(pair);
            items.Add(new Separator());

            bool selection = has && _editor.Selection.Count > 0 && group != null;
            var set = new MenuItem { Header = "Set finger of the selected notes", IsEnabled = selection };
            for (int f = 1; f <= 5; f++)
            {
                int finger = f;
                set.Items.Add(Item($"Finger {f}", $"Shift+{f}", true, () => SetSelectionFinger(finger)));
            }
            set.Items.Add(new Separator());
            set.Items.Add(Item("Left hand, keep the finger", "", true, () => SetSelectionHand(Hand.Left)));
            set.Items.Add(Item("Right hand, keep the finger", "", true, () => SetSelectionHand(Hand.Right)));
            items.Add(set);
            items.Add(Item("Clear finger corrections in the selection", "Shift+0", selection, () => SetSelectionFinger(0)));
            items.Add(Item("Clear all finger corrections of the track", "", has && info.Locks.Count > 0, () => ClearTrackLocks(track!)));
            items.Add(Item("Refinger now", "", true, RefingerNow));
            items.Add(new Separator());

            var hands = new MenuItem { Header = "Hands" };
            for (int i = 0; i < HandsFromNames.Length; i++)
            {
                int index = i;
                var m = new MenuItem { Header = HandsFromNames[i], IsCheckable = true, IsChecked = (int)FingeringPrefs.Hands == i };
                m.Click += (_, _) => { _settings.FingerHands = index; ApplyFingeringPrefs(); _settings.Save(); };
                hands.Items.Add(m);
            }
            items.Add(hands);
            var show = new MenuItem { Header = "Fingers on notes" };
            for (int i = 0; i < FingersOnNotesNames.Length; i++)
            {
                int index = i;
                var m = new MenuItem { Header = FingersOnNotesNames[i], IsCheckable = true, IsChecked = _settings.FingersOnNotes == i };
                m.Click += (_, _) => { _settings.FingersOnNotes = index; _settings.Save(); UpdateFingerOverlay(); };
                show.Items.Add(m);
            }
            items.Add(show);
            items.Add(Item("Fingering report…", "Ctrl+Shift+F", true, ShowFingeringReport));
            return items;
        }

        /// <summary>Change a track's mode and pair (undoable). A pair is set on both tracks.</summary>
        private void SetFingerMode(SongTrack track, FingerMode mode, int pair)
        {
            if (_song == null) return;
            var info = track.FingeringInfo;
            if (info.Mode == mode && info.Pair == pair) return;
            Commit("Fingering mode", () =>
            {
                // Undo the old pair on the partner, then set the new one.
                if (info.Pair >= 0 && info.Pair < _song.Tracks.Count && info.Pair != pair)
                {
                    var old = _song.Tracks[info.Pair];
                    if (old.FingeringInfo.Pair == track.Index) old.SetFingeringSetup(old.FingeringInfo.Mode, -1);
                }
                track.SetFingeringSetup(mode, pair);
                if (pair >= 0 && pair < _song.Tracks.Count)
                {
                    var partner = _song.Tracks[pair];
                    var pm = partner.FingeringInfo.Mode;
                    // Opposite hands when this track has one.
                    if (mode == FingerMode.LeftHand && pm is FingerMode.Auto or FingerMode.LeftHand) pm = FingerMode.RightHand;
                    if (mode == FingerMode.RightHand && pm is FingerMode.Auto or FingerMode.RightHand) pm = FingerMode.LeftHand;
                    if (pm == FingerMode.Off) pm = FingerMode.Auto;
                    partner.SetFingeringSetup(pm, track.Index);
                }
            });
            UpdateFingerOverlay();
        }

        /// <summary>Fix the finger of the selected notes (0 clears the correction). The hand is the one shown now.</summary>
        private void SetSelectionFinger(int finger)
        {
            var track = ActiveTrack;
            if (_song == null || track == null || _editor.Selection.Count == 0) return;
            var fingers = track.Fingering;
            var index = IndexOf(track);
            var locks = track.FingeringInfo.Locks.ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var n in _editor.Selection)
            {
                if (finger == 0) { locks.Remove((n.Number, n.Start)); continue; }
                var hand = index.TryGetValue(n, out int i) && i < fingers.Length ? fingers[i].Hand : Hand.None;
                if (locks.TryGetValue((n.Number, n.Start), out var old) && old.Hand != Hand.None) hand = old.Hand;
                locks[(n.Number, n.Start)] = new FingerLock(n.Number, n.Start, hand, finger);
            }
            Commit(finger == 0 ? "Clear finger corrections" : $"Set finger {finger}", () => track.SetFingerLocks(locks.Values));
            fingerOverlay.InvalidateVisual();
        }

        private void SetSelectionHand(Hand hand)
        {
            var track = ActiveTrack;
            if (_song == null || track == null || _editor.Selection.Count == 0) return;
            var fingers = track.Fingering;
            var index = IndexOf(track);
            var locks = track.FingeringInfo.Locks.ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var n in _editor.Selection)
            {
                int finger = locks.TryGetValue((n.Number, n.Start), out var old) ? old.Finger
                           : index.TryGetValue(n, out int i) && i < fingers.Length ? fingers[i].Finger : 0;
                locks[(n.Number, n.Start)] = new FingerLock(n.Number, n.Start, hand, Math.Clamp(finger, 1, 5));
            }
            Commit(hand == Hand.Left ? "Set left hand" : "Set right hand", () => track.SetFingerLocks(locks.Values));
        }

        private void ClearTrackLocks(SongTrack track) => Commit("Clear finger corrections", () => track.SetFingerLocks(Array.Empty<FingerLock>()));

        private static Dictionary<SongNote, int> IndexOf(SongTrack track)
        {
            var d = new Dictionary<SongNote, int>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < track.Notes.Count; i++) d[track.Notes[i]] = i;
            return d;
        }

        private void RefingerNow()
        {
            if (_song == null) return;
            _song.Fingering.Reset();
            fingerOverlay.InvalidateVisual();
            txtStatus.Text = "Refingering…";
        }

        /// <summary>Settings → engine options.</summary>
        private void ApplyFingeringPrefs()
        {
            var s = _settings;
            FingeringPrefs.Set(s.FingerCharts, s.FingerRepeats, (HandsFrom)Math.Clamp(s.FingerHands, 0, 2));
            FingeringPrefs.SaveInFile = s.SaveFingering;
            _keyboardSignature = 0;
            fingerOverlay.InvalidateVisual();
            _report?.ResultsChanged();
        }

        /// <summary>Shift+1…5 fixes the finger of the selected notes, Shift+0 clears it.</summary>
        private bool HandleFingerKey(System.Windows.Input.Key key, bool shift, bool ctrl)
        {
            if (ctrl && shift && key == System.Windows.Input.Key.F) { ShowFingeringReport(); return true; }
            if (!shift || ctrl || _editor.Selection.Count == 0) return false;
            int f = key switch
            {
                System.Windows.Input.Key.D0 or System.Windows.Input.Key.NumPad0 => 0,
                System.Windows.Input.Key.D1 or System.Windows.Input.Key.NumPad1 => 1,
                System.Windows.Input.Key.D2 or System.Windows.Input.Key.NumPad2 => 2,
                System.Windows.Input.Key.D3 or System.Windows.Input.Key.NumPad3 => 3,
                System.Windows.Input.Key.D4 or System.Windows.Input.Key.NumPad4 => 4,
                System.Windows.Input.Key.D5 or System.Windows.Input.Key.NumPad5 => 5,
                _ => -1,
            };
            if (f < 0) return false;
            SetSelectionFinger(f);
            return true;
        }

        private void ShowFingeringReport()
        {
            if (_song == null) return;
            if (_report == null)
            {
                _report = new FingeringReportWindow(this);
                _report.Closed += (_, _) =>
                {
                    _report = null;
                    fingerOverlay.Differences = null;
                    fingerOverlay.CurrentDifference = null;
                    fingerOverlay.InvalidateVisual();
                };
                _report.Show();
            }
            else
            {
                if (_report.WindowState == WindowState.Minimized) _report.WindowState = WindowState.Normal;
                _report.Activate();
            }
        }

        // What the report uses.
        internal void ShowDifferences(ISet<SongNote>? notes, SongNote? current)
        {
            fingerOverlay.Differences = notes;
            fingerOverlay.CurrentDifference = current;
            if (notes != null && _settings.FingersOnNotes == 0) { _settings.FingersOnNotes = 1; UpdateFingerOverlay(); }
            fingerOverlay.InvalidateVisual();
        }

        /// <summary>Make <paramref name="track"/> the edited track, select the note and scroll it into view.</summary>
        internal void RevealNote(SongTrack track, SongNote note)
        {
            ActivateTrack(track);
            _editor.SetSelection(new[] { note });
            var size = viewer.ViewportSize;
            double x = viewer.WorldToViewX(note.Start);
            if (x < size.Width * 0.1 || x > size.Width * 0.8) viewer.ScrollToX(note.Start, 0.3);
            BuildElements();   // first: activating a drum track switches to the drum rows
            int row = PianoRollStyle.Rows.RowOf(note.Number);
            if (row < 0) return;
            double y = viewer.WorldToViewY(row);
            if (y < 0 || y > size.Height - 10) viewer.ScrollToY(row, 0.5);
        }

        internal void SelectFinger(int finger) => SetSelectionFinger(finger);
        internal AppSettings Settings => _settings;
    }
}
