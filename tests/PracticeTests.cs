using nMIDI.Model;
using nMIDI.Theory;
using System.Linq;
using Xunit;

namespace nMIDI.Tests
{
    public class PracticeTests
    {
        private static PracticeNote N(int key, long start, Hand hand = Hand.None, int track = 0) =>
            new(new SongNote(key, 0, start, start + 400, 90), track, hand, 1);

        [Fact]
        public void Notes_that_start_together_make_one_step()
        {
            var s = new PracticeScript(new[] { N(60, 0), N(64, 5), N(67, 0), N(62, 480) }, PracticeHands.Both, chordWindow: 20);
            Assert.Equal(2, s.Steps.Count);
            Assert.Equal(new[] { 60, 64, 67 }, s.Steps[0].Keys);
            Assert.Equal(480, s.Steps[1].Tick);
        }

        [Fact]
        public void Hands_come_from_fingering_or_middle_c()
        {
            var notes = new[] { N(72, 0, Hand.Left), N(48, 0), N(64, 480), N(50, 960, Hand.Right) };
            var right = new PracticeScript(notes, PracticeHands.Right, 20);
            Assert.Equal(new[] { 64, 50 }, right.Notes.Select(n => n.Note.Number));
            var left = new PracticeScript(notes, PracticeHands.Left, 20);
            Assert.Equal(new[] { 48, 72 }, left.Notes.Select(n => n.Note.Number).OrderBy(k => k));
        }

        [Fact]
        public void Wait_mode_needs_every_key_in_any_order()
        {
            var s = new PracticeScript(new[] { N(60, 0), N(64, 0), N(67, 480) }, PracticeHands.Both, 20);
            var run = new PracticeSession(s, wait: true, window: 120);
            Assert.Equal(PressResult.Correct, run.Press(64, 0));
            Assert.Same(s.Steps[0], run.CurrentStep);
            Assert.Equal(PressResult.Wrong, run.Press(61, 0));
            Assert.Equal(PressResult.Correct, run.Press(60, 0));
            Assert.Same(s.Steps[1], run.CurrentStep);
            run.Press(67, 480);
            Assert.True(run.IsDone);
            Assert.Equal(2, run.Steps);
            Assert.Equal(1, run.CleanSteps);
            Assert.Equal(1, run.WrongKeys);
            Assert.Equal(50, run.Accuracy);
        }

        [Fact]
        public void Play_along_judges_hits_misses_and_timing()
        {
            var s = new PracticeScript(new[] { N(60, 0), N(62, 480), N(64, 960) }, PracticeHands.Both, 20);
            var run = new PracticeSession(s, wait: false, window: 100);
            Assert.Equal(PressResult.Correct, run.Press(60, 30));       // 30 ticks late
            Assert.Equal(PressResult.Wrong, run.Press(62, 200));        // too early for 480
            run.Advance(700);                                           // 62 passed unplayed
            Assert.Equal(PressResult.Correct, run.Press(64, 950));      // 10 ticks early
            run.Advance(2000);
            Assert.Equal(2, run.Hits);
            Assert.Equal(1, run.Misses);
            Assert.Equal(1, run.WrongKeys);
            Assert.Equal(20, run.TotalOffset);
            Assert.True(run.IsDone);
        }

        [Fact]
        public void Range_starts_at_the_loop()
        {
            var s = new PracticeScript(new[] { N(60, 0), N(62, 480), N(64, 960) }, PracticeHands.Both, 20);
            var run = new PracticeSession(s, wait: true, window: 100, fromTick: 400, toTick: 900);
            Assert.Equal(1, run.Total);
            Assert.Equal(new[] { 62 }, run.CurrentStep!.Keys);
            run.Press(62, 480);
            Assert.True(run.IsDone);
        }

        [Fact]
        public void Accompaniment_leaves_out_the_practised_notes()
        {
            var song = new Song(480);
            var t = new SongTrack("Piano", 0);
            t.SetNotes(new[] { new SongNote(48, 0, 0, 480, 90), new SongNote(72, 0, 0, 480, 90) });
            song.SetTracks(new[] { t });
            song.Rebuild();
            var notes = t.Notes.Select(n => new PracticeNote(n, 0, Hand.None, 0));
            var script = new PracticeScript(notes, PracticeHands.Right, 20);
            var copy = script.Accompaniment(song);
            Assert.Equal(new[] { 48 }, copy.Tracks[0].Notes.Select(n => n.Number));
            Assert.Equal(2, song.Tracks[0].Notes.Count);
        }
    }
}
