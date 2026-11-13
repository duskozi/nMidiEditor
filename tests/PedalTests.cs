using nMIDI.Model;
using System.Linq;
using Xunit;

namespace nMIDI.Tests
{
    public class PedalTests
    {
        private static SongTrack Track(params SongCtrl[] controls)
        {
            var t = new SongTrack("P", 0);
            t.SetControls(controls);
            return t;
        }

        private static SongCtrl Cc(long tick, int number, int value) => new(tick, 0, CtrlKind.Controller, number, value);

        [Fact]
        public void Value_at_the_playhead_is_the_last_event()
        {
            var p = new PedalTimeline(new[] { Track(Cc(100, 64, 127), Cc(400, 64, 0), Cc(400, 64, 127), Cc(800, 64, 0)) });
            Assert.True(p.Has(Pedal.Sustain));
            Assert.False(p.Has(Pedal.Soft));
            Assert.Equal(0, p.ValueAt(Pedal.Sustain, 50));
            Assert.Equal(127, p.ValueAt(Pedal.Sustain, 100));
            Assert.Equal(127, p.ValueAt(Pedal.Sustain, 400));   // re-pedal at the same tick: the last event wins
            Assert.Equal(0, p.ValueAt(Pedal.Sustain, 900));
        }

        [Fact]
        public void Spans_cover_pressed_stretches_and_merge_tracks()
        {
            var p = new PedalTimeline(new[] { Track(Cc(0, 64, 127), Cc(480, 64, 0)), Track(Cc(960, 67, 127), Cc(1000, 1, 50)) });
            var sus = p.Spans(Pedal.Sustain, 0, 2000, 3000).ToList();
            Assert.Single(sus);
            Assert.Equal(new PedalSpan(0, 480, 127), sus[0]);
            var soft = p.Spans(Pedal.Soft, 0, 2000, 3000).ToList();
            Assert.Equal(new PedalSpan(960, 3000, 127), Assert.Single(soft));   // held to the end of the song
            Assert.Empty(p.Spans(Pedal.Sustain, 600, 900, 3000));
            Assert.True(new PedalTimeline(new[] { Track(Cc(0, 7, 100)) }).IsEmpty);
        }
    }
}
