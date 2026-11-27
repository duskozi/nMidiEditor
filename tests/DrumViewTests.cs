using nMIDI.PianoRoll;
using nMIDI.Theory;
using System.Linq;
using Xunit;

namespace nMIDI.Tests
{
    public class DrumViewTests
    {
        [Fact]
        public void PianoRowsHaveEveryNoteHighestOnTop()
        {
            var rows = RollRows.Piano;
            Assert.False(rows.IsDrums);
            Assert.Equal(128, rows.Count);
            Assert.Equal(127, rows.NoteAt(0));
            Assert.Equal(0, rows.NoteAt(127));
            Assert.Equal(127 - 60, rows.RowOf(60));
            // Outside the layout: clamped, like the old note-from-mouse code.
            Assert.Equal(127, rows.NoteAt(-5));
            Assert.Equal(0, rows.NoteAt(400));
        }

        [Fact]
        public void DrumRowsAreTheKitAndTheUsedDrums()
        {
            var rows = RollRows.Drums(new[] { 36, 56, 81, 36 });

            Assert.True(rows.IsDrums);
            Assert.Equal(DrumMap.CommonKit.Union(new[] { 56, 81 }).OrderByDescending(n => n), rows.Notes);
            Assert.Equal(0, rows.RowOf(81));                 // highest on top
            Assert.Equal(rows.Count - 1, rows.RowOf(36));    // bass drum at the bottom
            Assert.Equal(-1, rows.RowOf(60));                // a key the track does not use has no row
        }

        [Fact]
        public void ShiftingMovesByDrumRowsAndStaysInside()
        {
            var rows = RollRows.Drums(System.Array.Empty<int>());
            int kickRow = rows.RowOf(36);

            Assert.Equal(rows.NoteAt(kickRow - 1), rows.Shift(36, -1));   // one row up: the next drum
            Assert.Equal(37, rows.Shift(36, -1));                           // side stick, above the kick
            Assert.Equal(36, rows.Shift(36, 5));                            // already the bottom row
            Assert.Equal(rows.NoteAt(0), rows.Shift(36, -100));             // clamped to the top
            Assert.Equal(60, rows.Shift(60, 1));                            // not in the layout: unchanged
        }

        [Fact]
        public void SameLayoutComparesTheRows()
        {
            Assert.True(RollRows.Drums(new[] { 70 }).SameLayout(RollRows.Drums(new[] { 70, 36 })));
            Assert.False(RollRows.Drums(new[] { 70 }).SameLayout(RollRows.Drums(new[] { 71 })));
            Assert.False(RollRows.Piano.SameLayout(RollRows.Drums(new int[0])));
        }

        [Fact]
        public void DrumNamesFollowTheGeneralMidiMap()
        {
            Assert.Equal("Acoustic Bass Drum", DrumMap.Name(35));
            Assert.Equal("Bass Drum", DrumMap.Name(36));
            Assert.Equal("Acoustic Snare", DrumMap.Name(38));
            Assert.Equal("Closed Hi-Hat", DrumMap.Name(42));
            Assert.Equal("Open Hi-Hat", DrumMap.Name(46));
            Assert.Equal("Crash Cymbal 1", DrumMap.Name(49));
            Assert.Equal("Ride Cymbal 1", DrumMap.Name(51));
            Assert.Equal("Open Triangle", DrumMap.Name(81));
            Assert.Equal("High Q", DrumMap.Name(27));
            Assert.Equal("Open Surdo", DrumMap.Name(87));
            Assert.Null(DrumMap.Name(26));
            Assert.Null(DrumMap.Name(88));
            Assert.Equal(DrumMap.LastNamed - DrumMap.FirstNamed + 1,
                         Enumerable.Range(DrumMap.FirstNamed, 61).Select(DrumMap.Name).Distinct().Count());
            Assert.StartsWith("C", DrumMap.Label(24));
        }
    }
}
