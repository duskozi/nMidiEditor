using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using nMIDI.Model;
using System.IO;
using System.Linq;
using Xunit;

namespace nMIDI.Tests
{
    public class RecordTests
    {
        [Fact]
        public void ControlFromMapsInputEvents()
        {
            var cc = Song.ControlFrom(120, new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)127) { Channel = (FourBitNumber)2 });
            Assert.Equal(new SongCtrl(120, 2, CtrlKind.Controller, 64, 127), cc);
            var bend = Song.ControlFrom(0, new PitchBendEvent(8192));
            Assert.Equal(CtrlKind.PitchBend, bend!.Kind);
            Assert.Null(Song.ControlFrom(0, new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100)));
        }

        [Fact]
        public void SingleTrackExportKeepsTempoAndNotes()
        {
            var song = Song.CreateEmpty();
            song.Tempos = new[] { new TempoChange(0, TempoChange.FromBpm(90)) };
            var track = new SongTrack("Lead", 3);
            track.SetNotes(new[] { new SongNote(64, 3, 480, 960, 90), new SongNote(67, 3, 960, 1440, 80) });
            track.SetControls(new[] { new SongCtrl(0, 3, CtrlKind.Program, 0, 40) });
            var export = new Song((int)song.TicksPerQuarter) { Tempos = song.Tempos, TimeSignatures = song.TimeSignatures };
            export.SetTracks(new[] { track });
            export.Rebuild();

            string path = Path.Combine(Path.GetTempPath(), $"nmidi-export-{System.Guid.NewGuid():N}.mid");
            try
            {
                export.Save(path, 1);
                var back = Song.Load(path);
                var lead = back.Tracks.Single(t => t.Notes.Count > 0);
                Assert.Equal(new[] { 64, 67 }, lead.Notes.Select(n => n.Number));
                Assert.Equal(3, lead.Channel);
                Assert.Equal(90, back.Tempos[0].Bpm, 1);
                Assert.Contains(lead.Controls, c => c.Kind == CtrlKind.Program && c.Value == 40);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
