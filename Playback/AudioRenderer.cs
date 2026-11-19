using Melanchall.DryWetMidi.Core;
using MeltySynth;
using nMIDI.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace nMIDI.Playback
{
    /// <summary>What to render: the tracks that sound, the mixer settings and the range.</summary>
    public sealed class RenderOptions
    {
        /// <summary>The SoundFont (.sf2) that plays the notes.</summary>
        public required string SoundFontPath { get; init; }

        public int SampleRate { get; init; } = 44100;

        /// <summary>Track indexes that sound (mute and solo already applied).</summary>
        public required ISet<int> AudibleTracks { get; init; }

        /// <summary>Mixer values that replace the song's controllers, per track index: controller → value.</summary>
        public IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> MixerOverrides { get; init; } =
            new Dictionary<int, IReadOnlyDictionary<int, int>>();

        public long StartTick { get; init; }

        /// <summary>End of the range, or -1 for the end of the song.</summary>
        public long EndTick { get; init; } = -1;

        /// <summary>Seconds added after the range so that the last notes and the reverb can die away.</summary>
        public double TailSeconds { get; init; } = 2.5;
    }

    /// <summary>Rendered stereo audio, 32-bit float, -1 to 1.</summary>
    public sealed class RenderedAudio
    {
        public RenderedAudio(float[] left, float[] right, int sampleRate)
        {
            Left = left;
            Right = right;
            SampleRate = sampleRate;
        }

        public float[] Left { get; }
        public float[] Right { get; }
        public int SampleRate { get; }
        public int Length => Left.Length;

        /// <summary>Interleaved 16-bit little-endian PCM, as WAV and the MP3 encoder take it.</summary>
        public byte[] ToPcm16()
        {
            var bytes = new byte[Length * 4];
            for (int i = 0, o = 0; i < Length; i++)
            {
                short l = (short)Math.Round(Math.Clamp(Left[i], -1f, 1f) * 32767);
                short r = (short)Math.Round(Math.Clamp(Right[i], -1f, 1f) * 32767);
                bytes[o++] = (byte)l; bytes[o++] = (byte)(l >> 8);
                bytes[o++] = (byte)r; bytes[o++] = (byte)(r >> 8);
            }
            return bytes;
        }
    }

    /// <summary>
    /// Plays the song through a SoundFont synthesizer (MeltySynth) into memory, faster than real time. The song's
    /// events are sent at their exact sample, so the result matches playback. Controllers and programs set before
    /// the range are applied first, so a range that starts mid-song sounds the same as it does there.
    /// </summary>
    public static class AudioRenderer
    {
        private const int Block = 64;

        /// <summary>
        /// Render the song. Call it on the UI thread or with a song nobody edits meanwhile; to render in the
        /// background, take <see cref="Song.Playback"/> and the tracks' channels on the UI thread and use the other overload.
        /// </summary>
        public static RenderedAudio Render(Song song, RenderOptions options, IProgress<double>? progress = null, CancellationToken cancel = default) =>
            Render(song.Playback, song.Tracks.Select(t => t.Channels).ToList(), options, progress, cancel);

        /// <summary>Render playback data (it never changes once built, so this can run on any thread).</summary>
        public static RenderedAudio Render(PlaybackData playback, IReadOnlyList<IReadOnlyList<int>> trackChannels, RenderOptions options,
                                           IProgress<double>? progress = null, CancellationToken cancel = default)
        {
            var synth = new Synthesizer(options.SoundFontPath, new SynthesizerSettings(options.SampleRate)
            {
                EnableReverbAndChorus = true,
            });

            var timeline = playback.Timeline;
            long endTick = options.EndTick >= 0 ? options.EndTick : playback.LengthTicks;
            long startTick = Math.Clamp(options.StartTick, 0, endTick);
            double startUs = timeline.TickToTime(startTick), endUs = timeline.TickToTime(endTick);
            int rate = options.SampleRate;
            int total = (int)Math.Ceiling((endUs - startUs) / 1e6 * rate + options.TailSeconds * rate);
            total = Math.Max(total, Block);

            // Each track's channels, and which controllers the mixer replaces on them.
            var overridden = new Dictionary<(int Track, int Cc), int>();
            foreach (var (track, ccs) in options.MixerOverrides)
                foreach (var (cc, value) in ccs) overridden[(track, cc)] = value;

            var events = playback.Events.Where(e => options.AudibleTracks.Contains(e.Track)).ToList();

            // Before the range: controllers, programs and pitch bend only (no notes).
            int index = 0;
            for (; index < events.Count && events[index].Tick < startTick; index++)
            {
                var e = events[index];
                if (e.Event is NoteOnEvent or NoteOffEvent) continue;
                Send(synth, e, overridden);
            }
            // The mixer values, on every channel of the track.
            foreach (var ((track, cc), value) in overridden)
            {
                if (!options.AudibleTracks.Contains(track) || track >= trackChannels.Count) continue;
                foreach (int channel in trackChannels[track])
                    synth.ProcessMidiMessage(channel, 0xB0, cc, value);
            }

            var left = new float[total];
            var right = new float[total];
            int pos = 0;
            int lastReport = 0;
            bool released = false;
            while (pos < total)
            {
                cancel.ThrowIfCancellationRequested();
                // Send every event that falls before the end of this block, at the block start.
                // Blocks are 64 samples (1.5 ms at 44.1 kHz), finer than MIDI timing can tell.
                double blockUs = startUs + (double)(pos + Block) / rate * 1e6;
                while (index < events.Count && events[index].Tick <= endTick && events[index].Time < blockUs)
                {
                    var e = events[index++];
                    if (e.Tick >= endTick && e.Event is NoteOnEvent) continue;
                    Send(synth, e, overridden);
                }
                // At the end of the range, release what is still held (once).
                if (!released && startUs + (double)pos / rate * 1e6 >= endUs)
                {
                    synth.NoteOffAll(false);
                    released = true;
                }

                int n = Math.Min(Block, total - pos);
                synth.Render(left.AsSpan(pos, n), right.AsSpan(pos, n));
                pos += n;

                if (pos - lastReport > rate)
                {
                    lastReport = pos;
                    progress?.Report((double)pos / total);
                }
            }

            // Keep the peak just under full scale: quieten a mix that would clip, and lift a quiet one a little.
            float peak = 0;
            for (int i = 0; i < total; i++) peak = Math.Max(peak, Math.Max(Math.Abs(left[i]), Math.Abs(right[i])));
            if (peak > 0)
            {
                float gain = Math.Min(0.89f / peak, 4f);
                for (int i = 0; i < total; i++) { left[i] *= gain; right[i] *= gain; }
            }
            progress?.Report(1);
            return new RenderedAudio(left, right, rate);
        }

        private static void Send(Synthesizer synth, SongEvent e, Dictionary<(int Track, int Cc), int> overridden)
        {
            switch (e.Event)
            {
                case NoteOnEvent on:
                    synth.ProcessMidiMessage(on.Channel, 0x90, on.NoteNumber, on.Velocity);
                    break;
                case NoteOffEvent off:
                    synth.ProcessMidiMessage(off.Channel, 0x80, off.NoteNumber, off.Velocity);
                    break;
                case ControlChangeEvent cc:
                    if (overridden.ContainsKey((e.Track, cc.ControlNumber))) return;
                    synth.ProcessMidiMessage(cc.Channel, 0xB0, cc.ControlNumber, cc.ControlValue);
                    break;
                case ProgramChangeEvent pc:
                    synth.ProcessMidiMessage(pc.Channel, 0xC0, pc.ProgramNumber, 0);
                    break;
                case PitchBendEvent pb:
                    synth.ProcessMidiMessage(pb.Channel, 0xE0, pb.PitchValue & 0x7F, pb.PitchValue >> 7);
                    break;
                case ChannelAftertouchEvent at:
                    synth.ProcessMidiMessage(at.Channel, 0xD0, at.AftertouchValue, 0);
                    break;
                case NoteAftertouchEvent poly:
                    synth.ProcessMidiMessage(poly.Channel, 0xA0, poly.NoteNumber, poly.AftertouchValue);
                    break;
            }
        }
    }
}
