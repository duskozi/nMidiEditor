using Melanchall.DryWetMidi.Core;
using nMIDI.Model;
using System.Linq;
using Xunit;

namespace nMIDI.Tests
{
    public class InstrumentTests
    {
        [Fact]
        public void GeneralMidiHasSixteenFamiliesOfEight()
        {
            var gm = InstrumentCatalog.GeneralMidi;
            Assert.Equal(128, gm.Patches.Count);
            Assert.Equal(16, gm.Groups.Count);
            Assert.Equal("Guitar", gm.Patches[24].Group);
            Assert.False(gm.SendsBank);
        }

        [Fact]
        public void NamesKnownSounds()
        {
            Assert.Equal("Nylon Guitar", InstrumentCatalog.Name(null, null, 24, drums: false));
            Assert.Equal("Nylon Guitar (variation)", InstrumentCatalog.Name(8, 0, 24, drums: false));
            Assert.Equal("Jazz kit", InstrumentCatalog.Name(null, null, 32, drums: true));
            Assert.Equal("TR-808 kit", InstrumentCatalog.Name(0, 0, 25, drums: true));
            Assert.Equal("Analog Kit", InstrumentCatalog.Name(127, 0, 25, drums: true));
            Assert.Equal("Kit 4", InstrumentCatalog.Name(null, null, 3, drums: true));
            Assert.Equal("Bank 120/0 · 1 Standard kit", InstrumentCatalog.Label(new InstrumentChoice(120, 0, 0), drums: true));
        }

        [Fact]
        public void SoundFontPresetsBecomeBanks()
        {
            try
            {
                InstrumentCatalog.SetSoundFont("test.sf2", new[] { (0, 0, "Grand"), (8, 4, "Funky EP"), (128, 0, "Std"), (128, 16, "Rock") });
                var banks = InstrumentCatalog.SoundFontBanks;
                Assert.Equal(3, banks.Count);
                Assert.True(banks[2].Drums);
                Assert.Equal("Funky EP", InstrumentCatalog.Name(8, 0, 4, drums: false));
                Assert.Equal("Rock", InstrumentCatalog.Name(null, null, 16, drums: true));
                Assert.Equal("Drum kits (GS)", InstrumentCatalog.Banks(drums: true)[0].Name);
            }
            finally
            {
                InstrumentCatalog.SetSoundFont(null, System.Array.Empty<(int, int, string)>());
            }
        }

        [Fact]
        public void KeyboardSoundComesFromTheTrackThenFromASetupTrack()
        {
            var setup = new SongTrack("Setup", 2);
            setup.SetControls(new[] { new SongCtrl(0, 2, CtrlKind.Program, 0, 48), new SongCtrl(0, 3, CtrlKind.Program, 0, 33) });
            var strings = new SongTrack("Strings", 2);
            var own = new SongTrack("Flute", 3);
            own.SetControls(new[] { new SongCtrl(0, 3, CtrlKind.Program, 0, 73) });
            var piano = new SongTrack("Piano", 5);
            var all = new[] { setup, strings, own, piano };

            Assert.Equal(48, SongOps.KeyboardSoundAt(strings, all, 0)?.Program);   // from the setup track
            Assert.Equal(73, SongOps.KeyboardSoundAt(own, all, 0)?.Program);       // its own program wins
            Assert.Null(SongOps.KeyboardSoundAt(piano, all, 0));                   // none: the default piano
        }

        [Fact]
        public void SetInstrumentPutsBankSelectBeforeTheProgram()
        {
            var t = new SongTrack("Drums", 9);
            t.SetControls(new[] { new SongCtrl(0, 9, CtrlKind.Controller, 7, 100), new SongCtrl(0, 9, CtrlKind.Program, 0, 0) });
            SongOps.SetInstrument(t, 9, 0, new InstrumentChoice(120, 0, 25));
            var at0 = t.Controls.Where(c => c.Tick == 0).ToList();
            Assert.Equal(4, at0.Count);
            Assert.Equal((CtrlKind.Controller, 0, 120), (at0[0].Kind, at0[0].Number, at0[0].Value));
            Assert.Equal((CtrlKind.Controller, 32, 0), (at0[1].Kind, at0[1].Number, at0[1].Value));
            Assert.Equal((CtrlKind.Program, 25), (at0[2].Kind, at0[2].Value));
            Assert.Equal(new InstrumentChoice(120, 0, 25), SongOps.InstrumentAt(t, 9, 1000));

            // Choosing a sound without bank select removes the one at that tick.
            SongOps.SetInstrument(t, 9, 0, new InstrumentChoice(null, null, 32));
            Assert.Equal(new InstrumentChoice(null, null, 32), SongOps.InstrumentAt(t, 9, 0));
        }

        [Fact]
        public void BankSelectIsSentAndSavedBeforeTheProgramChange()
        {
            var song = new Song(480);
            var t = new SongTrack("Piano", 0);
            // Deliberately out of order: program first, then bank select, at the same tick.
            t.SetControls(new[] { new SongCtrl(0, 0, CtrlKind.Program, 0, 4), new SongCtrl(0, 0, CtrlKind.Controller, 0, 8) });
            t.SetNotes(new[] { new SongNote(60, 0, 0, 480, 100) });
            song.SetTracks(new[] { t });
            song.Rebuild();
            var events = song.Playback.Events.Select(e => e.Event).ToList();
            Assert.IsType<ControlChangeEvent>(events[0]);
            Assert.IsType<ProgramChangeEvent>(events[1]);

            var chunk = song.ToMidiFile().Chunks.OfType<TrackChunk>().Last();
            var channelEvents = chunk.Events.OfType<ChannelEvent>().ToList();
            Assert.IsType<ControlChangeEvent>(channelEvents[0]);
            Assert.IsType<ProgramChangeEvent>(channelEvents[1]);
        }

        [Fact]
        public void ErasingAProgramTakesItsBankSelect()
        {
            var t = new SongTrack("P", 0);
            SongOps.SetInstrument(t, 0, 960, new InstrumentChoice(8, 0, 4));
            t.SetControls(t.Controls.Append(new SongCtrl(960, 0, CtrlKind.Controller, 7, 90)));
            var gone = SongOps.ProgramsWithBanks(t, 900, 1000);
            Assert.Equal(3, gone.Count);
            Assert.DoesNotContain(gone, c => c.Number == 7 && c.Kind == CtrlKind.Controller);
        }
    }
}
