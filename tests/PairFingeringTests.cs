using nMIDI.Model;
using nMIDI.Theory;
using System.IO;
using System.Linq;
using Xunit;

namespace nMIDI.Tests
{
    public class PairFingeringTests
    {
        private static SongTrack Track(string name, int channel, params SongNote[] notes)
        {
            var t = new SongTrack(name, channel);
            t.SetNotes(notes);
            return t;
        }

        private static SongNote[] Line(int start, int count, int step = 2) =>
            Enumerable.Range(0, count).Select(i => new SongNote(start + (i % 5) * step, 0, i * 240, i * 240 + 220, 80)).ToArray();

        private static Song SongOf(params SongTrack[] tracks)
        {
            var song = Song.CreateEmpty();
            song.SetTracks(tracks);
            song.Rebuild();
            return song;
        }

        [Fact]
        public void TwoPianoTracksArePairedLowerIsLeft()
        {
            var upper = Track("Piano 1", 0, Line(67, 20));
            var lower = Track("Piano 2", 1, Line(43, 20));
            var song = SongOf(upper, lower);
            var group = Assert.Single(song.Fingering.Groups);
            Assert.Equal(2, group.Tracks.Length);
            Assert.All(upper.FingeringNow(), f => Assert.Equal(Hand.Right, f.Hand));
            Assert.All(lower.FingeringNow(), f => Assert.Equal(Hand.Left, f.Hand));
            Assert.All(lower.FingeringNow(), f => Assert.InRange(f.Finger, 1, 5));
        }

        [Fact]
        public void NonKeyboardTrackIsNotFingeredInAuto()
        {
            var flute = Track("Flute", 0, Line(72, 8));
            flute.SetControls(new[] { new SongCtrl(0, 0, CtrlKind.Program, 0, 73) });
            var song = SongOf(flute);
            Assert.Empty(song.Fingering.Groups);
            Assert.All(flute.FingeringNow(), f => Assert.Equal(0, f.Finger));

            flute.SetFingeringSetup(FingerMode.RightHand, -1);
            song.Rebuild();
            Assert.All(flute.FingeringNow(), f => Assert.Equal(Hand.Right, f.Hand));
            Assert.All(flute.FingeringNow(), f => Assert.InRange(f.Finger, 1, 5));
        }

        [Fact]
        public void OffModeAndChosenPair()
        {
            var a = Track("Melody", 0, Line(70, 10));
            var b = Track("Chords", 0, Line(60, 10));
            var c = Track("Bass", 0, Line(40, 10));
            var song = SongOf(a, b, c);
            Assert.Equal(3, song.Fingering.Groups.Count);      // three piano tracks: no automatic pair
            a.SetFingeringSetup(FingerMode.RightHand, 2);       // melody with the bass
            b.SetFingeringSetup(FingerMode.Off, -1);
            song.Rebuild();
            var g = Assert.Single(song.Fingering.Groups);
            Assert.Equal(new[] { a, c }, g.Tracks);
            Assert.All(b.FingeringNow(), f => Assert.Equal(0, f.Finger));
            Assert.All(c.FingeringNow(), f => Assert.Equal(Hand.Left, f.Hand));
        }

        [Fact]
        public void LockedFingerIsKeptAndSavedInTheFile()
        {
            var rh = Track("Piano RH", 0, Line(60, 12, 2));
            var song = SongOf(rh);
            var target = rh.Notes[4];
            int engine = rh.FingeringNow()[4].Finger;
            int wanted = engine % 5 + 1;
            rh.SetFingerLocks(new[] { new FingerLock(target.Number, target.Start, Hand.Right, wanted) });
            song.Rebuild();
            var f = rh.FingeringNow()[4];
            Assert.Equal(wanted, f.Finger);
            Assert.True(f.Locked);

            rh.SetFingeringSetup(FingerMode.RightHand, -1);
            string path = Path.Combine(Path.GetTempPath(), $"nmidi-lock-{System.Guid.NewGuid():N}.mid");
            try
            {
                song.Save(path);
                var loaded = Song.Load(path);
                var t = loaded.Tracks.Single(x => x.Notes.Count > 0);
                Assert.Equal(FingerMode.RightHand, t.FingeringInfo.Mode);
                var l = Assert.Single(t.FingeringInfo.Locks.Values);
                Assert.Equal((target.Number, target.Start, wanted), (l.Number, l.Tick, l.Finger));
                Assert.Equal(wanted, t.FingeringNow()[4].Finger);

                FingeringPrefs.SaveInFile = false;
                song.Save(path);
                Assert.Empty(Song.Load(path).Tracks.Single(x => x.Notes.Count > 0).FingeringInfo.Locks);
            }
            finally
            {
                FingeringPrefs.SaveInFile = true;
                File.Delete(path);
            }
        }

        [Fact]
        public void LocksMoveWithTheirNotes()
        {
            var rh = Track("Piano RH", 0, Line(60, 6));
            var song = SongOf(rh);
            var n = rh.Notes[2];
            rh.SetFingerLocks(new[] { new FingerLock(n.Number, n.Start, Hand.Right, 4) });
            var moved = n with { Number = n.Number + 2, Start = n.Start + 10, End = n.End + 10 };
            rh.Notes = NoteOps.Replace(rh.Notes, new[] { n }, new[] { moved });
            rh.MoveFingerLocks(new[] { n }, new[] { moved });
            Assert.True(rh.FingeringInfo.Locks.ContainsKey((moved.Number, moved.Start)));
        }

        [Fact]
        public void FingersWrittenInTheFileAreRead()
        {
            var rh = Track("Piano RH", 0, Line(60, 3));
            rh.Meta = new[]
            {
                new SongMeta(0, new Melanchall.DryWetMidi.Core.LyricEvent("1")),
                new SongMeta(240, new Melanchall.DryWetMidi.Core.TextEvent("R2")),
                new SongMeta(480, new Melanchall.DryWetMidi.Core.TextEvent("hello")),
            };
            var info = rh.FingeringInfo;
            Assert.Equal(2, info.FileFingers.Count);
            Assert.Equal((Hand.Right, 2), info.FileFingers[240]);
        }
    }
}
