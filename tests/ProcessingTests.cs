using nMIDI.Model;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace nMIDI.Tests
{
    public class ProcessingTests
    {
        private static SongNote N(int pitch, long start, long end, int velocity = 100) => new(pitch, 0, start, end, velocity);

        [Fact]
        public void Quantize_moves_starts_to_the_grid()
        {
            var notes = new List<SongNote> { N(60, 110, 230), N(62, 250, 470) };
            var q = NoteProcessing.Quantize(notes, 120, QuantizeMode.StartKeepLength);
            Assert.Equal(120, q[0].Start);
            Assert.Equal(240, q[0].End);   // length kept
            Assert.Equal(240, q[1].Start);
        }

        [Fact]
        public void Quantize_strength_and_end()
        {
            var notes = new List<SongNote> { N(60, 100, 350) };
            var half = NoteProcessing.Quantize(notes, 120, QuantizeMode.StartKeepLength, strength: 0.5);
            Assert.Equal(110, half[0].Start);
            var both = NoteProcessing.Quantize(notes, 120, QuantizeMode.StartAndEnd);
            Assert.Equal(120, both[0].Start);
            Assert.Equal(360, both[0].End);
            var tiny = NoteProcessing.Quantize(new List<SongNote> { N(60, 100, 130) }, 120, QuantizeMode.StartAndEnd);
            Assert.True(tiny[0].End > tiny[0].Start);
        }

        [Fact]
        public void Quantize_swing_moves_every_second_line()
        {
            var notes = new List<SongNote> { N(60, 0, 100), N(60, 120, 200), N(60, 240, 300) };
            var q = NoteProcessing.Quantize(notes, 120, QuantizeMode.StartKeepLength, swing: 0.5);
            Assert.Equal(0, q[0].Start);
            Assert.Equal(150, q[1].Start);
            Assert.Equal(240, q[2].Start);
        }

        [Fact]
        public void Scale_steps_and_nearest()
        {
            var major = Scale.All.First(s => s.Name == "Major");
            Assert.Equal(64, major.TransposeSteps(0, 60, 2));    // C → E in C major
            Assert.Equal(59, major.TransposeSteps(0, 60, -1));   // C → B
            Assert.Equal(60, major.Nearest(0, 61));              // C# → C (ties go down)
            Assert.True(major.Contains(2, 66));                  // F# is in D major
            var t = NoteProcessing.TransposeInScale(new[] { N(60, 0, 1), N(64, 0, 1) }, 2, major, 0);
            Assert.Equal(new[] { 64, 67 }, t.Select(n => n.Number));
        }

        [Fact]
        public void Legato_and_overlaps()
        {
            var notes = new List<SongNote> { N(60, 0, 100), N(64, 0, 50), N(67, 480, 500) };
            var legato = NoteProcessing.ChangeLength(notes, LengthMode.Legato);
            Assert.Equal(480, legato[0].End);
            Assert.Equal(480, legato[1].End);
            Assert.Equal(500, legato[2].End);

            var overlapping = new List<SongNote> { N(60, 0, 500), N(60, 240, 600) };
            var fixedNotes = NoteProcessing.ChangeLength(overlapping, LengthMode.RemoveOverlaps);
            Assert.Equal(240, fixedNotes[0].End);
            Assert.Equal(600, fixedNotes[1].End);
        }

        [Fact]
        public void Reverse_and_invert()
        {
            var notes = new List<SongNote> { N(60, 0, 100), N(67, 200, 480) };
            var r = NoteProcessing.Reverse(notes);
            Assert.Equal((380L, 480L), (r[0].Start, r[0].End));
            Assert.Equal((0L, 280L), (r[1].Start, r[1].End));
            var i = NoteProcessing.Invert(notes, 60);
            Assert.Equal(new[] { 60, 53 }, i.Select(n => n.Number));
        }

        [Fact]
        public void Humanize_stays_within_limits_and_is_repeatable()
        {
            var notes = Enumerable.Range(0, 50).Select(k => N(60, 1000 + k * 100, 1050 + k * 100, 64)).ToList();
            var a = NoteProcessing.Humanize(notes, 10, 5, 0, seed: 42);
            var b = NoteProcessing.Humanize(notes, 10, 5, 0, seed: 42);
            Assert.Equal(a, b);
            for (int k = 0; k < notes.Count; k++)
            {
                Assert.InRange(a[k].Start - notes[k].Start, -10, 10);
                Assert.InRange(a[k].Velocity, 59, 69);
                Assert.Equal(notes[k].Length, a[k].Length);
            }
        }

        [Fact]
        public void Velocity_scale_and_ramp()
        {
            var notes = new List<SongNote> { N(60, 0, 1, 100), N(60, 100, 101, 100), N(60, 200, 201, 100) };
            Assert.Equal(new[] { 127, 127, 127 }, NoteProcessing.ScaleVelocity(notes, 2, 0).Select(n => n.Velocity));
            Assert.Equal(new[] { 40, 75, 110 }, NoteProcessing.RampVelocity(notes, 40, 110).Select(n => n.Velocity));
        }

        [Fact]
        public void Rule_selects_matching_notes()
        {
            var notes = new List<SongNote> { N(48, 0, 10, 50), N(60, 10, 20, 90), N(64, 20, 30, 120), N(72, 30, 40, 90) };
            var rule = new NoteRule(LowPitch: 50, HighPitch: 80, LowVelocity: 80, HighVelocity: 127);
            Assert.Equal(new[] { 60, 64, 72 }, rule.Apply(notes).Select(n => n.Number));
            var everySecond = new NoteRule(EveryNth: 2, NthOffset: 1);
            Assert.Equal(new[] { 60, 72 }, everySecond.Apply(notes).Select(n => n.Number));
        }
    }
}
