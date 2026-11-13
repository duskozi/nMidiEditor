using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using nMIDI.Model;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace nMIDI.Tests
{
    public class SongTests
    {
        /// <summary>A format 1 file: conductor (tempo, 3/4, marker), piano with notes, CC and bend, and bass.</summary>
        private static MidiFile MakeFile()
        {
            static TrackChunk Chunk(params (long Delta, MidiEvent Event)[] events)
            {
                var chunk = new TrackChunk();
                foreach (var (delta, e) in events)
                {
                    e.DeltaTime = delta;
                    chunk.Events.Add(e);
                }
                return chunk;
            }

            var conductor = Chunk(
                (0, new SequenceTrackNameEvent("My song")),
                (0, new SetTempoEvent(600_000)),
                (0, new TimeSignatureEvent(3, 4)),
                (960, new MarkerEvent("Verse")),
                (480, new SetTempoEvent(500_000)));

            var piano = Chunk(
                (0, new SequenceTrackNameEvent("Piano RH")),
                (0, new ProgramChangeEvent((SevenBitNumber)0)),
                (0, new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)100)),
                (0, new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90)),
                (0, new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)80)),
                (480, new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)40)),
                (0, new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)0)),   // note-on with velocity 0 = note-off
                (0, new PitchBendEvent(9000)),
                (240, new NoteOnEvent((SevenBitNumber)67, (SevenBitNumber)100)),
                (240, new NoteOffEvent((SevenBitNumber)67, (SevenBitNumber)0)));

            var bass = Chunk(
                (0, new SequenceTrackNameEvent("Bass")),
                (0, new ProgramChangeEvent((SevenBitNumber)33) { Channel = (FourBitNumber)1 }),
                (0, new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)100) { Channel = (FourBitNumber)1 }),
                (1440, new NoteOffEvent((SevenBitNumber)36, (SevenBitNumber)0) { Channel = (FourBitNumber)1 }));

            return new MidiFile(conductor, piano, bass) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
        }

        private static string TempPath() => Path.Combine(Path.GetTempPath(), $"nmidi-test-{Guid.NewGuid():N}.mid");

        [Fact]
        public void Load_reads_tracks_notes_controls_and_conductor()
        {
            var song = Song.FromFile(MakeFile());

            Assert.Equal(480, song.TicksPerQuarter);
            Assert.Equal(2, song.Tracks.Count);
            Assert.Equal("Piano RH", song.Tracks[0].Name);
            Assert.Equal("Bass", song.Tracks[1].Name);
            Assert.Equal(1, song.Tracks[1].Channel);
            Assert.Equal("Finger Bass", song.Tracks[1].Instrument);

            var piano = song.Tracks[0].Notes;
            Assert.Equal(3, piano.Count);
            Assert.Equal(new SongNote(60, 0, 0, 480, 90, 40), piano[0]);
            Assert.Equal(new SongNote(64, 0, 0, 480, 80, 0), piano[1]);
            Assert.Equal(new SongNote(67, 0, 720, 960, 100, 0), piano[2]);

            Assert.Contains(song.Tracks[0].Controls, c => c.Kind == CtrlKind.Controller && c.Number == 7 && c.Value == 100);
            Assert.Contains(song.Tracks[0].Controls, c => c.Kind == CtrlKind.PitchBend && c.Value == 9000 && c.Tick == 480);

            Assert.Equal(2, song.Tempos.Count);
            Assert.Equal(100, song.Tempos[0].Bpm, 3);
            Assert.Equal(new TimeSignatureChange(0, 3, 4), song.TimeSignatures[0]);
            Assert.Equal(3, song.BeatsPerBar);
            Assert.Contains(song.Conductor, m => m.Event is MarkerEvent { Text: "Verse" } && m.Tick == 960);
            Assert.Equal(1440, song.LengthTicks);
        }

        [Fact]
        public void Save_and_load_round_trip_keeps_everything()
        {
            var song = Song.FromFile(MakeFile());
            string path = TempPath();
            try
            {
                song.Save(path);
                var again = Song.Load(path);

                Assert.Equal(song.Tracks.Count, again.Tracks.Count);
                for (int i = 0; i < song.Tracks.Count; i++)
                {
                    Assert.Equal(song.Tracks[i].Name, again.Tracks[i].Name);
                    Assert.Equal(song.Tracks[i].Notes, again.Tracks[i].Notes);
                    Assert.Equal(song.Tracks[i].Controls, again.Tracks[i].Controls);
                }
                Assert.Equal(song.Tempos, again.Tempos);
                Assert.Equal(song.TimeSignatures, again.TimeSignatures);
                Assert.Contains(again.Conductor, m => m.Event is MarkerEvent { Text: "Verse" } && m.Tick == 960);
                Assert.Contains(again.Conductor, m => m.Event is SequenceTrackNameEvent { Text: "My song" });
                Assert.False(File.Exists(path + ".saving"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Edits_survive_saving()
        {
            var song = Song.FromFile(MakeFile());
            var track = song.Tracks[0];
            var moved = NoteOps.Move(track.Notes.Take(1).ToList(), 960, 2);
            track.Notes = NoteOps.Replace(track.Notes, track.Notes.Take(1), moved);
            track.Name = "Right hand";
            song.Rebuild();

            string path = TempPath();
            try
            {
                song.Save(path);
                var again = Song.Load(path);
                Assert.Equal("Right hand", again.Tracks[0].Name);
                Assert.Contains(new SongNote(62, 0, 960, 1440, 90, 40), again.Tracks[0].Notes);
                Assert.DoesNotContain(again.Tracks[0].Notes, n => n.Number == 60);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Format0_is_split_by_channel_and_can_be_saved_as_format0()
        {
            var song = Song.FromFile(MakeFile());
            string path = TempPath();
            try
            {
                song.Save(path, format: 0);
                var file = MidiFile.Read(path);
                Assert.Single(file.GetTrackChunks());

                var again = Song.Load(path);
                Assert.Equal(0, again.SourceFormat);
                Assert.Equal(2, again.Tracks.Count);
                Assert.Equal(song.Tracks[0].Notes, again.Tracks[0].Notes);
                Assert.Equal(song.Tracks[1].Notes, again.Tracks[1].Notes);
                Assert.Equal(song.Tempos, again.Tempos);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Playback_events_put_controllers_then_note_offs_before_note_ons()
        {
            var song = new Song(480);
            var track = new SongTrack("T", 0);
            track.SetNotes(new[] { new SongNote(60, 0, 0, 480, 100), new SongNote(60, 0, 480, 960, 100) });
            track.SetControls(new[] { new SongCtrl(480, 0, CtrlKind.Program, 0, 5) });
            song.SetTracks(new[] { track });
            song.Rebuild();

            var at480 = song.Events.Where(e => e.Tick == 480).Select(e => e.Event).ToList();
            Assert.IsType<ProgramChangeEvent>(at480[0]);
            Assert.IsType<NoteOffEvent>(at480[1]);
            Assert.IsType<NoteOnEvent>(at480[2]);
            Assert.Equal(960, song.LengthTicks);
        }

        [Fact]
        public void Bars_follow_time_signature_changes()
        {
            var song = new Song(480);
            song.TimeSignatures = new[] { new TimeSignatureChange(0, 4, 4), new TimeSignatureChange(1920 * 2, 3, 4) };
            song.SetTracks(Array.Empty<SongTrack>());
            song.Rebuild();

            Assert.Equal((0L, 0L, 0L), song.ToBarBeat(0));
            Assert.Equal((1L, 1L, 10L), song.ToBarBeat(1920 + 490));
            Assert.Equal((2L, 0L, 0L), song.ToBarBeat(3840));
            Assert.Equal((3L, 0L, 0L), song.ToBarBeat(3840 + 1440));
            Assert.Equal("  4.1.000", song.FormatBars(3840 + 1440));
        }

        [Fact]
        public void FromBarBeat_is_the_inverse_of_ToBarBeat()
        {
            var song = new Song(480);
            song.TimeSignatures = new[] { new TimeSignatureChange(0, 4, 4), new TimeSignatureChange(3840, 3, 4), new TimeSignatureChange(3840 + 1440 * 2, 6, 8) };
            song.SetTracks(Array.Empty<SongTrack>());
            song.Rebuild();
            foreach (long tick in new long[] { 0, 1, 479, 1920, 2410, 3840, 3840 + 1440 + 5, 3840 + 2880, 3840 + 2880 + 1441, 20000 })
            {
                var (bar, beat, t) = song.ToBarBeat(tick);
                Assert.Equal(tick, song.FromBarBeat(bar, beat, t));
            }
        }

        [Fact]
        public void Tempo_timeline_converts_both_ways()
        {
            var song = Song.FromFile(MakeFile());
            // 600 000 µs per quarter for 1440 ticks (3 quarters), then 500 000.
            Assert.Equal(1_800_000, song.Timeline.TickToTime(1440), 3);
            Assert.Equal(2_300_000, song.Timeline.TickToTime(1920), 3);
            Assert.Equal(1920, song.Timeline.TimeToTick(2_300_000), 3);
        }

        [Fact]
        public void Empty_song_has_one_track_and_can_be_saved()
        {
            var song = Song.CreateEmpty();
            Assert.Single(song.Tracks);
            Assert.Equal(0, song.LengthTicks);

            song.Tracks[0].SetNotes(new[] { new SongNote(60, 0, 0, 480, 100) });
            song.Rebuild();
            string path = TempPath();
            try
            {
                song.Save(path);
                var again = Song.Load(path);
                Assert.Single(again.Tracks);
                Assert.Equal("Track 1", again.Tracks[0].Name);
                Assert.Single(again.Tracks[0].Notes);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
