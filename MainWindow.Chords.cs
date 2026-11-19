using nMIDI.Model;
using nMIDI.Theory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace nMIDI
{
    /// <summary>
    /// Chord names: a strip along the top of the piano roll with the chords of the visible tracks, and the chord
    /// of the keys that sound now (the song, the clicked key and the MIDI input) above the keyboard.
    /// </summary>
    public partial class MainWindow
    {
        private ChordTimeline? _chordTimeline;
        private int _chordSignature;
        private string? _liveChord;
        private ChordSpan? _chordAtPlayhead;

        /// <summary>Keys held on the MIDI input, for the keyboard and the chord name.</summary>
        private readonly HashSet<int> _inputNotes = new();

        private void InitChords()
        {
            chordOverlay.Viewer = viewer;
        }

        private void OnChordsToggled(object sender, RoutedEventArgs e)
        {
            _settings.ShowChords = tglChords.IsChecked == true;
            UpdateChordVisibility();
            RefreshChords();
        }

        private void UpdateChordVisibility()
        {
            if (pnlChord == null || tglChords == null) return;
            pnlChord.Visibility = tglChords.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private int ChordSignature()
        {
            var hash = new HashCode();
            foreach (var vm in Tracks)
            {
                hash.Add(RuntimeHelpers.GetHashCode(vm.Track.Notes));
                hash.Add(vm.ShowNotes);
                hash.Add(vm.Track.Channel);
            }
            hash.Add(_song == null ? 0 : RuntimeHelpers.GetHashCode(_song.TimeSignatures));
            return hash.ToHashCode();
        }

        /// <summary>Rebuild the chords if the notes changed, and redraw the strip.</summary>
        private void RefreshChords(bool force = true)
        {
            if (_song == null || tglChords.IsChecked != true)
            {
                _chordTimeline = null;
                _chordSignature = 0;
                chordOverlay.Chords = null;
                chordOverlay.InvalidateVisual();
                return;
            }
            int signature = ChordSignature();
            if (!force && signature == _chordSignature && _chordTimeline != null) return;
            _chordSignature = signature;
            _chordTimeline = new ChordTimeline(Tracks.Where(vm => vm.ShowNotes).Select(vm => vm.Track), _song);
            chordOverlay.Chords = _chordTimeline;
            chordOverlay.InvalidateVisual();
        }

        /// <summary>Every frame: follow edits and move the highlight with the playhead.</summary>
        private void UpdateChordStrip()
        {
            RefreshChords(force: false);
            if (_chordTimeline == null) return;
            long tick = (long)_player.PositionTicks;
            // Redraw only when the playhead moves into another chord.
            var span = _chordTimeline.SpanAt(tick);
            if (span == _chordAtPlayhead) return;
            _chordAtPlayhead = span;
            chordOverlay.PlayheadTick = tick;
            chordOverlay.Accent = ((SolidColorBrush)FindResource("Accent")).Color;
            chordOverlay.InvalidateVisual();
        }

        /// <summary>Show the chord of the keys that sound now above the keyboard.</summary>
        private void ShowLiveChord(IEnumerable<int> keys)
        {
            string? name = tglChords.IsChecked == true ? ChordNamer.Name(keys) : null;
            if (name == _liveChord) return;
            _liveChord = name;
            txtChord.Text = name ?? "–";
        }

        private void TrackInputNote(int note, bool down)
        {
            if (down) _inputNotes.Add(note);
            else _inputNotes.Remove(note);
        }
    }
}
