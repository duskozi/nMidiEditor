using nMIDI.Model;
using nMIDI.Piano;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace nMIDI
{
    /// <summary>
    /// The falling-notes view: in place of the piano roll, notes fall onto the keyboard panel (with the bar lines and
    /// the pedals); without a song, what you play rises from the keys. Edit and Practice mode each remember whether it
    /// is on.
    /// </summary>
    public partial class MainWindow
    {
        private int _fallingSignature;
        private DateTime _fallingBuilt;

        private void InitFalling()
        {
            falling.Keyboard = keyboard;
            falling.Seconds = _settings.FallingSeconds;
            falling.SecondsChanged += s => _settings.FallingSeconds = s;
        }

        private void OnFallingToggled(object sender, RoutedEventArgs e)
        {
            if (_practiceMode) _settings.FallingInPractice = tglFalling.IsChecked == true;
            else _settings.FallingInEdit = tglFalling.IsChecked == true;
            ApplyFalling();
        }

        /// <summary>Show the falling notes or the piano roll, as the mode's setting says.</summary>
        private void ApplyFalling()
        {
            bool on = tglFalling.IsChecked == true;
            falling.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            viewer.Visibility = on ? Visibility.Hidden : Visibility.Visible;
            pnlEmpty.Visibility = on || _song != null ? Visibility.Collapsed : Visibility.Visible;
            if (on && tglKeyboard.IsChecked != true) tglKeyboard.IsChecked = true;
            _fallingSignature = 0;
            RefreshFalling();
        }

        /// <summary>The mode changed: take that mode's falling-notes setting.</summary>
        private void FallingForMode()
        {
            tglFalling.IsChecked = _practiceMode ? _settings.FallingInPractice : _settings.FallingInEdit;
            ApplyFalling();
        }

        private int FallingSignature()
        {
            var hash = new HashCode();
            hash.Add(_song);
            foreach (var vm in Tracks)
            {
                hash.Add(RuntimeHelpers.GetHashCode(vm.Track.Notes));
                hash.Add(RuntimeHelpers.GetHashCode(vm.Track.Fingering));
                hash.Add(vm.ShowNotes);
                hash.Add(vm.IsAudible);
                hash.Add(vm.Color);
            }
            hash.Add(_practiceScript);
            hash.Add(_pedalTimeline);
            return hash.ToHashCode();
        }

        /// <summary>Give the view the song's notes, bar lines and pedals when they changed (every frame; cheap when nothing did).</summary>
        private void RefreshFalling()
        {
            if (falling.Visibility != Visibility.Visible) return;
            if (_song is not { } song)
            {
                if (falling.SongClock != null)
                {
                    falling.SongClock = null;
                    falling.SetSong(Array.Empty<FallingNote>(), Array.Empty<(double, bool)>(), Array.Empty<FallingPedal>());
                }
                return;
            }
            falling.SongClock ??= () => _song is { } s ? s.Timeline.TickToTime(_player.PositionTicks) / 1e6 : 0;

            int signature = FallingSignature();
            // The fingering finishes in the background: let finger numbers catch up, but not more than twice a second.
            if (signature == _fallingSignature || (_fallingSignature != 0 && DateTime.UtcNow - _fallingBuilt < TimeSpan.FromMilliseconds(500))) return;
            _fallingSignature = signature;
            _fallingBuilt = DateTime.UtcNow;

            var timeline = song.Timeline;
            double Sec(long tick) => timeline.TickToTime(tick) / 1e6;
            var practised = _practiceScript?.Notes.Select(n => n.Note).ToHashSet(ReferenceEqualityComparer.Instance);

            var notes = new List<FallingNote>();
            foreach (var vm in Tracks)
            {
                if (!vm.ShowNotes) continue;
                var fingers = vm.Track.Fingering;
                var list = vm.Track.Notes;
                for (int i = 0; i < list.Count; i++)
                {
                    var n = list[i];
                    var f = i < fingers.Length ? fingers[i] : default;
                    // In practice mode the part you play is bright and the accompaniment dim.
                    bool dim = practised != null ? !practised.Contains(n) : !vm.IsAudible;
                    notes.Add(new FallingNote(Sec(n.Start), Sec(n.End), n.Number, vm.Color, (int)f.Hand, f.Finger, dim));
                }
            }

            var lines = new List<(double, bool)>();
            foreach (var (tick, _, beatTicks, beats) in song.BarLines(0, song.LengthTicks + song.TicksPerBar))
                for (int b = 0; b < beats; b++) lines.Add((Sec(tick + b * beatTicks), b == 0));

            var pedals = new List<FallingPedal>();
            if (_pedalTimeline is { IsEmpty: false } timelinePedals)
                foreach (var pedal in PedalTimeline.All)
                    foreach (var span in timelinePedals.Spans(pedal, 0, song.LengthTicks, song.LengthTicks))
                        if (span.Value > 0) pedals.Add(new FallingPedal(pedal, Sec(span.Start), Sec(span.End), span.Value));

            falling.SetSong(notes, lines, pedals);
        }

        // --- Live notes (no song): what you play rises from the keys ---

        private void FallingLiveNote(int note, bool down, int velocity = 100)
        {
            if (down) falling.LiveNoteOn(note, velocity, ((SolidColorBrush)FindResource("Accent")).Color);
            else falling.LiveNoteOff(note);
        }

        private void FallingLivePedal(int number, int value)
        {
            if (number is 64 or 66 or 67) falling.LivePedal((Pedal)number, value);
        }
    }
}
