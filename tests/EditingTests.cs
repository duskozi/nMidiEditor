using nMIDI.Model;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace nMIDI.Tests
{
    public class EditingTests
    {
        private static Song MakeSong(params SongNote[] notes)
        {
            var song = new Song(480);
            var track = new SongTrack("T", 0);
            track.SetNotes(notes);
            song.SetTracks(new[] { track });
            song.Rebuild();
            return song;
        }

        [Fact]
        public void Move_keeps_notes_inside_the_song_and_the_keyboard()
        {
            var notes = new List<SongNote> { new(10, 0, 100, 200, 90), new(120, 0, 300, 400, 90) };
            var moved = NoteOps.Move(notes, -500, 20);
            Assert.Equal(0, moved[0].Start);                // clamped so the first note starts at 0
            Assert.Equal(200, moved[1].Start);
            Assert.Equal(127, moved[1].Number);             // clamped so the highest note stays at 127
            Assert.Equal(17, moved[0].Number);
            Assert.Equal(100, moved[0].Length);
        }

        [Fact]
        public void Resize_keeps_a_minimum_length()
        {
            var notes = new List<SongNote> { new(60, 0, 0, 480, 90) };
            Assert.Equal(120, NoteOps.ResizeEnds(notes, -1000, 120)[0].End);
            Assert.Equal(360, NoteOps.ResizeStarts(notes, 1000, 120)[0].Start);
            Assert.Equal(0, NoteOps.ResizeStarts(notes, -1000, 120)[0].Start);
        }

        [Fact]
        public void Split_cuts_only_notes_that_cross_the_point()
        {
            var notes = new List<SongNote> { new(60, 0, 0, 480, 90), new(62, 0, 480, 960, 90) };
            var split = NoteOps.Split(notes, 240);
            Assert.Equal(3, split.Count);
            Assert.Contains(new SongNote(60, 0, 0, 240, 90), split);
            Assert.Contains(new SongNote(60, 0, 240, 480, 90), split);
            Assert.Contains(notes[1], split);
        }

        [Fact]
        public void Snap_rounds_to_the_grid()
        {
            Assert.Equal(240, NoteOps.Snap(200, 120));
            Assert.Equal(120, NoteOps.SnapDown(239, 120));
            Assert.Equal(201, NoteOps.Snap(200.6, 0));
        }

        [Fact]
        public void Replace_tells_identical_notes_apart()
        {
            var a = new SongNote(60, 0, 0, 480, 90);
            var b = new SongNote(60, 0, 0, 480, 90);   // a duplicate, equal by value
            var result = NoteOps.Replace(new[] { a, b }, new[] { a }, System.Array.Empty<SongNote>());
            Assert.Single(result);
            Assert.Same(b, result[0]);
        }

        [Fact]
        public void Undo_and_redo_restore_notes_and_track_dirty_state()
        {
            var original = new SongNote(60, 0, 0, 480, 90);
            var song = MakeSong(original);
            var track = song.Tracks[0];
            var history = new EditHistory();
            history.Reset(song);
            Assert.False(history.IsDirty);

            history.Record("Move note");
            track.Notes = NoteOps.Replace(track.Notes, new[] { original }, NoteOps.Move(new[] { original }, 480, 0));
            song.Rebuild();
            Assert.True(history.IsDirty);
            Assert.Equal(480, track.Notes[0].Start);

            history.Record("Rename");
            track.Name = "Lead";

            Assert.Equal("Rename", history.Undo());
            Assert.Equal("T", track.Name);
            Assert.Equal("Move note", history.Undo());
            Assert.Same(original, track.Notes[0]);
            Assert.False(history.IsDirty);
            Assert.Null(history.Undo());

            Assert.Equal("Move note", history.Redo());
            Assert.Equal(480, track.Notes[0].Start);
            Assert.True(history.IsDirty);
            history.MarkSaved();
            Assert.False(history.IsDirty);
            history.Undo();
            Assert.True(history.IsDirty);
        }

        [Fact]
        public void Undo_restores_removed_and_reordered_tracks()
        {
            var song = MakeSong(new SongNote(60, 0, 0, 480, 90));
            var first = song.Tracks[0];
            var history = new EditHistory();
            history.Reset(song);

            history.Record("Add track");
            var added = new SongTrack("New", 1);
            song.SetTracks(new[] { added, first });
            song.Rebuild();
            Assert.Equal(1, first.Index);

            history.Undo();
            Assert.Single(song.Tracks);
            Assert.Same(first, song.Tracks[0]);
            Assert.Equal(0, first.Index);

            history.Redo();
            Assert.Equal(2, song.Tracks.Count);
            Assert.Same(added, song.Tracks[0]);
        }

        [Fact]
        public void Recording_clears_redo()
        {
            var song = MakeSong(new SongNote(60, 0, 0, 480, 90));
            var history = new EditHistory();
            history.Reset(song);
            history.Record("A");
            song.Tracks[0].Name = "A";
            history.Undo();
            Assert.True(history.CanRedo);
            history.Record("B");
            Assert.False(history.CanRedo);
        }

        [Fact]
        public void Sort_orders_by_start_then_key_and_is_stable()
        {
            var a = new SongNote(64, 0, 0, 10, 1);
            var b = new SongNote(60, 0, 0, 10, 1);
            var c = new SongNote(60, 0, 0, 10, 1);
            var d = new SongNote(50, 0, 5, 10, 1);
            var sorted = SongTrack.Sort(new[] { d, a, b, c });
            Assert.Same(b, sorted[0]);
            Assert.Same(c, sorted[1]);
            Assert.Same(a, sorted[2]);
            Assert.Same(d, sorted[3]);
        }

        [Fact]
        public void ReplaceInTracks_keeps_moved_notes_in_their_own_track()
        {
            var a = new SongTrack("RH", 0); var b = new SongTrack("LH", 1);
            var na = new SongNote(72, 0, 0, 480, 90); var nb = new SongNote(48, 1, 0, 480, 90); var keep = new SongNote(50, 1, 480, 960, 90);
            a.SetNotes(new[] { na }); b.SetNotes(new[] { nb, keep });
            var tracks = new[] { a, b };
            var before = new List<SongNote> { na, nb };
            var moved = NoteOps.Move(before, 240, 2);
            NoteOps.ReplaceInTracks(tracks, before, moved, before, a);
            Assert.Single(a.Notes);
            Assert.Equal(74, a.Notes[0].Number);
            Assert.Equal(2, b.Notes.Count);
            Assert.Contains(b.Notes, n => n.Number == 50 && n.Start == 720 - 240);
            Assert.Contains(b.Notes, n => n.Number == 50 && n.Start == 240);
            Assert.Same(a, NoteOps.OwnerOf(tracks, a.Notes[0]));

            // New notes with no source go to the fallback track.
            NoteOps.ReplaceInTracks(tracks, new SongNote[0], new[] { new SongNote(60, 0, 0, 100, 80) }, null, a);
            Assert.Equal(2, a.Notes.Count);
        }
    }
}
