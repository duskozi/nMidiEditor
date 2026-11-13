using nMIDI.Model;
using System.Collections.Generic;
using Xunit;

namespace nMIDI.Tests
{
    public class MixerTests
    {
        [Fact]
        public void Controls_follow_the_song_until_the_user_moves_them()
        {
            var mix = new MixerChannel();
            var changes = new List<(int CC, int Value)>();
            mix.ControlChanged += (_, cc, value) => changes.Add((cc, value));

            mix.Follow(MixerChannel.PanCC, 20);
            Assert.Equal(20, mix.Get(MixerChannel.PanCC));
            Assert.False(mix.HasOverrides);
            Assert.Empty(changes);   // following the song sends nothing

            mix.Pan = 1;
            Assert.Equal(127, mix.Get(MixerChannel.PanCC));
            Assert.True(mix.IsOverridden(MixerChannel.PanCC));
            mix.Follow(MixerChannel.PanCC, 30);   // ignored while overridden
            Assert.Equal(127, mix.Get(MixerChannel.PanCC));

            mix.Reset();
            Assert.False(mix.HasOverrides);
            Assert.Equal((MixerChannel.PanCC, -1), changes[^1]);
        }

        [Fact]
        public void Volume_fader_uses_the_general_midi_curve()
        {
            Assert.Equal(127, MixerChannel.FromDb(0));
            Assert.Equal(0, MixerChannel.FromDb(MixerChannel.MinDb));
            Assert.Equal(100, MixerChannel.FromDb(MixerChannel.ToDb(100)));
            Assert.Equal(-12, MixerChannel.ToDb(MixerChannel.FromDb(-12)), 0);
        }
    }
}
