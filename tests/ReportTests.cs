using Melanchall.DryWetMidi.Core;
using nMIDI.Model;
using nMIDI.Theory;
using System.IO;
using System.Linq;
using Xunit;

namespace nMIDI.Tests
{
    public class ReportTests
    {
        [Fact]
        public void ReportComparesHandsFingersAndCorrections()
        {
            // RH: C major scale up (chart fingering 1 2 3 1 2 3 4 5), LH: whole notes in the bass.
            int[] scale = { 60, 62, 64, 65, 67, 69, 71, 72 };
            var rh = new SongTrack("Piano RH", 0);
            rh.SetNotes(scale.Select((p, i) => new SongNote(p, 0, i * 240, i * 240 + 220, 80)));
            var lh = new SongTrack("Piano LH", 0);
            lh.SetNotes(Enumerable.Range(0, 4).Select(i => new SongNote(36 + i * 2, 0, i * 480, i * 480 + 460, 70)));
            // Written fingers: right for the first three, one wrong on purpose (a 5 on D).
            rh.Meta = new[]
            {
                new SongMeta(0, new LyricEvent("1")),
                new SongMeta(240, new LyricEvent("5")),
                new SongMeta(480, new LyricEvent("R3")),
            };
            var song = Song.CreateEmpty();
            song.SetTracks(new[] { rh, lh });
            song.Rebuild();
            var group = Assert.Single(song.Fingering.Groups);
            Assert.True(group.HandsGiven);

            var r = FingeringReport.Build(song, group);
            Assert.Equal(12, r.Notes);
            Assert.Equal(new Score(12, 12), r.HandAgreement);           // the hands are far apart
            Assert.Equal(new Score(2, 3), r.FingerMatch);
            Assert.Equal(3, r.BothRight.Total);
            Assert.Equal(12, r.Stability.Hit);
            Assert.Single(r.Differences);
            Assert.Equal(240, r.Differences[0].Note.Start);
            Assert.Contains("says 5", r.Differences[0].Why);

            // A correction that the engine would not choose.
            var d = rh.Notes[1];
            rh.SetFingerLocks(new[] { new FingerLock(d.Number, d.Start, Hand.Right, 5) });
            song.Rebuild();
            var r2 = FingeringReport.Build(song, song.Fingering.Groups[0]);
            Assert.Equal(new Score(0, 1), r2.Corrections);
            Assert.Equal(new Score(3, 3), r2.FingerMatch);                // now the shown finger is the written 5
        }

        [Fact]
        public void PigFileIsMatchedToTheNotes()
        {
            var rh = new SongTrack("Piano RH", 0);
            rh.SetNotes(new[] { 60, 62, 64 }.Select((p, i) => new SongNote(p, 0, 480 + i * 240, 480 + i * 240 + 220, 80)));
            var song = Song.CreateEmpty();
            song.SetTracks(new[] { rh });
            song.Rebuild();
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllLines(path, new[]
                {
                    "//Version: PianoFingering_v170101",
                    "0 0.000 0.200 C4 80 80 0 1",
                    "1 0.250 0.450 D4 80 80 0 2",
                    "2 0.500 0.700 E4 80 80 0 3_1",
                });
                var group = song.Fingering.Groups[0];
                var truth = FingeringReport.MatchPig(path, group, out int total);
                Assert.Equal(3, total);
                Assert.Equal(3, truth.Count);
                Assert.Equal(new TruthFinger(Hand.Right, 3), truth[rh.Notes[2]]);
                var r = FingeringReport.Build(song, group, truth);
                Assert.Equal(3, r.FingerMatch.Total);
                Assert.Equal(3, r.FingerMatch.Hit);
            }
            finally { File.Delete(path); }
        }
    }
}
