using Melanchall.DryWetMidi.Core;
using nMIDI.Model;
using System;
using System.Linq;
using Xunit;

namespace nMIDI.Tests
{
    public class SongOpsTests
    {
        private static Song MakeSong()
        {
            var song = new Song(480);
            var a = new SongTrack("A", 0);
            a.SetNotes(new[] { new SongNote(60, 0, 0, 480, 100), new SongNote(62, 0, 1920, 2400, 100), new SongNote(64, 0, 3840, 4320, 100), new SongNote(65, 0, 1800, 4000, 100) });
            a.SetControls(new[] { new SongCtrl(0, 0, CtrlKind.Controller, 7, 100), new SongCtrl(2000, 0, CtrlKind.Controller, 7, 50), new SongCtrl(4000, 0, CtrlKind.Controller, 7, 80) });
            var b = new SongTrack("B", 1);
            b.SetNotes(new[] { new SongNote(36, 1, 1920, 2000, 100) });
            song.SetTracks(new[] { a, b });
            song.Tempos = new[] { new TempoChange(0, 500_000), new TempoChange(2000, 400_000), new TempoChange(5000, 300_000) };
            song.TimeSignatures = new[] { new TimeSignatureChange(0, 4, 4), new TimeSignatureChange(3840, 3, 4) };
            song.Conductor = new[] { new SongMeta(2000, new MarkerEvent("B")), new SongMeta(2100, new KeySignatureEvent(2, 0)) };
            song.Rebuild();
            return song;
        }

        [Fact]
        public void InsertTime_shifts_everything_after_the_point()
        {
            var song = MakeSong();
            SongOps.InsertTime(song, 1920, 1920);
            song.Rebuild();
            var a = song.Tracks[0];
            Assert.Contains(new SongNote(60, 0, 0, 480, 100), a.Notes);
            Assert.Contains(new SongNote(62, 0, 3840, 4320, 100), a.Notes);
            Assert.Contains(new SongNote(65, 0, 1800, 5920, 100), a.Notes);   // sounds across the point: longer
            Assert.Equal(new long[] { 0, 3920, 5920 }, a.Controls.Select(c => c.Tick));
            Assert.Equal(new long[] { 0, 3920, 6920 }, song.Tempos.Select(t => t.Tick));
            Assert.Equal(5760, song.TimeSignatures[1].Tick);
            Assert.All(song.Conductor, m => Assert.True(m.Tick >= 3920));
            Assert.Equal(3840, song.Tracks[1].Notes[0].Start);
        }

        [Fact]
        public void DeleteTime_removes_and_pulls_back_keeping_the_last_values()
        {
            var song = MakeSong();
            SongOps.DeleteTime(song, 1920, 3840);
            song.Rebuild();
            var a = song.Tracks[0];
            Assert.DoesNotContain(a.Notes, n => n.Number == 62);              // started inside
            Assert.Contains(new SongNote(64, 0, 1920, 2400, 100), a.Notes);   // pulled back
            Assert.Contains(new SongNote(65, 0, 1800, 2080, 100), a.Notes);   // lasted past the end: shortened
            // CC7 50 at 2000 was inside: it moves to 1920 so the volume after the cut stays the same.
            Assert.Contains(new SongCtrl(1920, 0, CtrlKind.Controller, 7, 50), a.Controls);
            Assert.Contains(new SongCtrl(2080, 0, CtrlKind.Controller, 7, 80), a.Controls);
            Assert.Contains(new TempoChange(1920, 400_000), song.Tempos);
            Assert.Contains(new TempoChange(3080, 300_000), song.Tempos);
            Assert.Equal(new TimeSignatureChange(1920, 3, 4), song.TimeSignatures[1]);
            Assert.DoesNotContain(song.Conductor, m => m.Event is MarkerEvent);
            Assert.Contains(song.Conductor, m => m.Event is KeySignatureEvent && m.Tick == 1920);
            Assert.Empty(song.Tracks[1].Notes);
        }

        [Fact]
        public void Copy_and_paste_a_range()
        {
            var song = MakeSong();
            var clip = SongOps.CopyRange(song, 1920, 3840);
            Assert.Equal(1920, clip.Length);
            Assert.Single(clip.Tracks[0].Notes);   // 62 (65 started before the range)
            SongOps.PasteRange(song, clip, 7680, insert: false);
            song.Rebuild();
            Assert.Contains(new SongNote(62, 0, 7680, 8160, 100), song.Tracks[0].Notes);
            Assert.Contains(new SongNote(36, 1, 7680, 7760, 100), song.Tracks[1].Notes);
            Assert.Contains(new SongCtrl(7760, 0, CtrlKind.Controller, 7, 50), song.Tracks[0].Controls);

            var song2 = MakeSong();
            SongOps.PasteRange(song2, SongOps.CopyRange(song2, 0, 1920), 0, insert: true);
            song2.Rebuild();
            Assert.Equal(2, song2.Tracks[0].Notes.Count(n => n.Number == 60));
            Assert.Contains(new SongNote(62, 0, 3840, 4320, 100), song2.Tracks[0].Notes);
        }

        [Fact]
        public void Bars_follow_the_signature()
        {
            var song = MakeSong();
            Assert.Equal((1920L, 1920L), SongOps.Bars(song, 2500, 1));
            Assert.Equal((3840L, 1440L * 2), SongOps.Bars(song, 4000, 2));   // 3/4 after bar 3
        }

        [Fact]
        public void Track_commands()
        {
            var song = MakeSong();
            var added = SongOps.AddTrack(song, 2);
            Assert.Equal(2, added.Channel);   // 0 and 1 are used, 9 is skipped anyway
            Assert.Equal(3, song.Tracks.Count);

            var copy = SongOps.DuplicateTrack(song, song.Tracks[0]);
            Assert.Equal(1, copy.Index);
            Assert.Equal(song.Tracks[0].Notes, copy.Notes);

            SongOps.MoveTrack(song, copy, 10);
            Assert.Same(copy, song.Tracks[^1]);

            SongOps.MergeTracks(song, song.Tracks[0], new[] { song.Tracks[1] });
            Assert.Equal(3, song.Tracks.Count);
            Assert.Equal(5, song.Tracks[0].Notes.Count);
            Assert.Equal(new[] { 0, 1 }, song.Tracks[0].Channels);

            var created = SongOps.SplitByChannel(song, song.Tracks[0]);
            Assert.Single(created);
            Assert.Equal(new[] { 0 }, song.Tracks[0].Channels);
            Assert.Equal(new[] { 1 }, song.Tracks[1].Channels);

            var low = SongOps.SplitByPitch(song, song.Tracks[0], 63, "LH");
            Assert.All(low.Notes, n => Assert.True(n.Number < 63));
            Assert.All(song.Tracks[0].Notes, n => Assert.True(n.Number >= 63));

            SongOps.SetChannel(low, 5);
            Assert.All(low.Notes, n => Assert.Equal(5, n.Channel));
            SongOps.SetInitial(low, CtrlKind.Program, 0, 40);
            Assert.Equal(40, SongOps.GetInitial(low, CtrlKind.Program, 0));

            SongOps.DeleteTrack(song, low);
            Assert.DoesNotContain(low, song.Tracks);
            song.Rebuild();
        }
    }
}
