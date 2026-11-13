using nMIDI.Model;
using nMIDI.Theory;
using System.Diagnostics;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace nMIDI.Tests
{
    public class FingeringTests
    {
        private readonly ITestOutputHelper _out;
        public FingeringTests(ITestOutputHelper output) => _out = output;

        private static SongTrack Fingered(string name, params SongNote[] notes)
        {
            var song = Song.CreateEmpty();
            var track = new SongTrack(name, 0);
            track.SetNotes(notes);
            song.SetTracks(new[] { track });
            song.Rebuild();
            return track;
        }

        [Fact]
        public void RightHandScaleUsesTheChartFingering()
        {
            int[] c = { 60, 62, 64, 65, 67, 69, 71, 72, 74, 76, 77, 79, 81, 83, 84 };
            var track = Fingered("Piano RH", c.Select((n, i) => new SongNote(n, 0, i * 240, i * 240 + 220, 80)).ToArray());
            var fingers = track.FingeringNow().Select(f => f.Finger).ToArray();
            Assert.Equal(new[] { 1, 2, 3, 1, 2, 3, 4, 1, 2, 3, 1, 2, 3, 4, 5 }, fingers);
            Assert.All(track.FingeringNow(), f => Assert.Equal(Hand.Right, f.Hand));
        }

        [Fact]
        public void UnnamedTrackIsSplitBetweenHands()
        {
            var notes = Enumerable.Range(0, 16).SelectMany(i => new[]
            {
                new SongNote(36 + (i % 2) * 7, 0, i * 480, i * 480 + 460, 80),
                new SongNote(72 + (i % 4), 0, i * 480, i * 480 + 460, 80),
            }).ToArray();
            var track = Fingered("Piano", notes);
            for (int i = 0; i < track.Notes.Count; i++)
                Assert.Equal(track.Notes[i].Number < 60 ? Hand.Left : Hand.Right, track.FingeringNow()[i].Hand);
            Assert.All(track.FingeringNow(), f => Assert.InRange(f.Finger, 1, 5));
        }

        [Fact]
        public void FingeringArrivesInTheBackground()
        {
            var track = Fingered("Piano RH", new SongNote(60, 0, 0, 200, 80), new SongNote(64, 0, 240, 440, 80), new SongNote(67, 0, 480, 680, 80));
            var clock = Stopwatch.StartNew();
            while (track.Fingering.Any(f => f.Finger == 0) && clock.ElapsedMilliseconds < 5000) System.Threading.Thread.Sleep(10);
            Assert.All(track.Fingering, f => Assert.InRange(f.Finger, 1, 5));

            // After an edit, the unchanged notes keep their finger until the new fingering is ready.
            var kept = track.Notes[0];
            var before = track.Fingering[0];
            track.SetNotes(track.Notes.Append(new SongNote(72, 0, 720, 920, 80)));
            int index = track.Notes.ToList().IndexOf(kept);
            var now = track.Fingering[index];
            Assert.True(now.Finger == before.Finger || now.Finger > 0);
        }

        [Fact]
        public void DrumTrackHasNoFingers()
        {
            var song = Song.CreateEmpty();
            var track = new SongTrack("Drums", 9);
            track.SetNotes(new[] { new SongNote(36, 9, 0, 100, 100) });
            song.SetTracks(new[] { track });
            song.Rebuild();
            Assert.Equal(0, track.FingeringNow()[0].Finger);
        }

        [Fact]
        public void LongTwoHandPartIsFastEnough()
        {
            // About 4,000 notes: an Alberti bass under a melody with chords, at 120 BPM.
            var notes = new System.Collections.Generic.List<SongNote>();
            var rnd = new System.Random(3);
            for (int bar = 0; bar < 250; bar++)
            {
                int root = 48 + new[] { 0, 5, 7, 9 }[bar % 4];
                for (int k = 0; k < 8; k++)
                {
                    long t = (bar * 8 + k) * 240L;
                    notes.Add(new SongNote(root + new[] { 0, 7, 4, 7 }[k % 4], 0, t, t + 230, 70));
                }
                for (int k = 0; k < 4; k++)
                {
                    long t = (bar * 4 + k) * 480L;
                    int top = 72 + rnd.Next(12);
                    notes.Add(new SongNote(top, 0, t, t + 470, 90));
                    if (k == 0) { notes.Add(new SongNote(top - 4, 0, t, t + 470, 80)); notes.Add(new SongNote(top - 7, 0, t, t + 470, 80)); }
                }
            }
            var clock = Stopwatch.StartNew();
            var track = Fingered("Piano", notes.ToArray());
            _ = track.FingeringNow();
            clock.Stop();
            _out.WriteLine($"{notes.Count} notes fingered in {clock.ElapsedMilliseconds} ms");
            Assert.True(clock.ElapsedMilliseconds < 5000, $"took {clock.ElapsedMilliseconds} ms");
        }
    }
}
