using nMIDI.Model;
using nMIDI.Theory;
using System;
using System.Linq;
using Xunit;

namespace nMIDI.Tests
{
    public class ChordTests
    {
        [Theory]
        [InlineData("C", 60, 64, 67)]
        [InlineData("C", 48, 64, 67, 72)]
        [InlineData("Am", 57, 60, 64)]
        [InlineData("G7", 55, 59, 62, 65)]
        [InlineData("G7", 55, 59, 65)]            // fifth left out
        [InlineData("Cmaj7", 60, 64, 67, 71)]
        [InlineData("Dm7", 50, 53, 57, 60)]
        [InlineData("B♭", 58, 62, 65)]
        [InlineData("F♯m7♭5", 54, 57, 60, 64)]
        [InlineData("Bdim", 59, 62, 65)]
        [InlineData("Dsus4", 62, 67, 69)]
        [InlineData("C6", 60, 64, 67, 69)]
        [InlineData("Am7", 57, 60, 64, 67)]       // same keys as C6, but A in the bass
        [InlineData("C/E", 52, 60, 67)]
        [InlineData("G7/B", 47, 62, 65, 67)]
        [InlineData("E5", 40, 47)]
        [InlineData("G♯m", 56, 59, 63)]           // minor chords spelled with sharps
        [InlineData("C♯m7", 49, 52, 56, 59)]
        [InlineData("E♭m", 51, 54, 58)]
        [InlineData("A♭", 56, 60, 63)]            // major keeps the flat
        public void Names_common_chords(string expected, params int[] notes)
        {
            Assert.Equal(expected, ChordNamer.Name(notes));
        }

        [Theory]
        [InlineData(60)]
        [InlineData(60, 72)]
        [InlineData(60, 62)]
        [InlineData(60, 61, 62)]
        public void No_name_for_single_keys_or_clusters(params int[] notes)
        {
            Assert.Null(ChordNamer.Name(notes));
        }

        [Fact]
        public void Names_single_notes()
        {
            Assert.Equal("C4 E4", ChordNamer.NoteNames(new[] { 64, 60, 60 }));
            Assert.Equal("B♭3", ChordNamer.NoteNames(new[] { 58 }));
            Assert.Null(ChordNamer.NoteNames(Array.Empty<int>()));
        }

        [Fact]
        public void Timeline_shows_notes_where_there_is_no_chord()
        {
            // Bar 1: a single E held for a beat, then C and E (a third, not a chord) for a beat.
            var song = SongWith(new SongNote(64, 0, 0, 480, 90), new SongNote(60, 0, 480, 960, 90), new SongNote(64, 0, 480, 960, 90));
            var chords = new ChordTimeline(song.Tracks, song);
            Assert.Equal(new ChordSpan(0, 480, "E4", IsNotes: true), chords.Spans[0]);
            Assert.Equal(new ChordSpan(480, 960, "C4 E4", IsNotes: true), chords.Spans[1]);
        }

        private static Song SongWith(params SongNote[] notes)
        {
            var song = new Song(480);
            var track = new SongTrack("T", 0);
            track.SetNotes(notes);
            song.SetTracks(new[] { track });
            song.Rebuild();
            return song;
        }

        [Fact]
        public void Timeline_joins_beats_and_names_arpeggios()
        {
            // Bar 1: a held C chord. Bar 2: an arpeggio of F (F A C F), one note per beat, each held to the bar end.
            var song = SongWith(
                new SongNote(48, 0, 0, 1920, 90), new SongNote(64, 0, 0, 1920, 90), new SongNote(67, 0, 0, 1920, 90),
                new SongNote(53, 0, 1920, 3840, 90), new SongNote(57, 0, 2400, 3840, 90),
                new SongNote(60, 0, 2880, 3840, 90), new SongNote(65, 0, 3360, 3840, 90));
            var chords = new ChordTimeline(song.Tracks, song);

            Assert.Equal(new ChordSpan(0, 1920, "C"), chords.Spans[0]);
            Assert.Equal("F", chords.At(2400));
            Assert.Equal("F", chords.At(3700));
            Assert.Null(chords.At(5000));
        }

        [Fact]
        public void Timeline_drops_a_short_passing_note()
        {
            // A held G chord with a quick A (an eighth of the beat) in the melody.
            var song = SongWith(
                new SongNote(43, 0, 0, 480, 90), new SongNote(59, 0, 0, 480, 90), new SongNote(62, 0, 0, 480, 90),
                new SongNote(69, 0, 400, 460, 90));
            Assert.Equal("G", new ChordTimeline(song.Tracks, song).At(0));
        }

        [Fact]
        public void Drum_tracks_are_left_out()
        {
            var song = new Song(480);
            var drums = new SongTrack("D", 9);
            drums.SetNotes(new[] { new SongNote(36, 9, 0, 480, 90), new SongNote(40, 9, 0, 480, 90), new SongNote(43, 9, 0, 480, 90) });
            song.SetTracks(new[] { drums });
            song.Rebuild();
            Assert.True(new ChordTimeline(song.Tracks, song).IsEmpty);
        }
    }
}
