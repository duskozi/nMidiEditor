using System;
using System.Collections.Generic;
using System.Linq;

namespace nMIDI.Model
{
    /// <summary>The three piano pedals and their controller numbers.</summary>
    public enum Pedal { Sustain = 64, Sostenuto = 66, Soft = 67 }

    /// <summary>One stretch of time in which a pedal has the same value (value ≥ 64 counts as down).</summary>
    public readonly record struct PedalSpan(long Start, long End, int Value)
    {
        public bool IsDown => Value >= 64;
    }

    /// <summary>
    /// Piano pedal data (CC 64 sustain, 66 sostenuto, 67 soft) of one or more tracks, ready to draw:
    /// the value at any tick, and the stretches in which a pedal is pressed.
    /// </summary>
    public sealed class PedalTimeline
    {
        public static readonly Pedal[] All = { Pedal.Sustain, Pedal.Sostenuto, Pedal.Soft };

        private readonly Dictionary<Pedal, (long[] Ticks, int[] Values)> _data = new();

        public PedalTimeline(IEnumerable<SongTrack> tracks)
        {
            var list = tracks.ToList();
            foreach (var pedal in All)
            {
                var events = list.SelectMany(t => t.Controls)
                                 .Where(c => c.Kind == CtrlKind.Controller && c.Number == (int)pedal)
                                 .OrderBy(c => c.Tick)
                                 .ToList();
                if (events.Count > 0) _data[pedal] = (events.Select(c => c.Tick).ToArray(), events.Select(c => c.Value).ToArray());
            }
        }

        /// <summary>True when any of the tracks has data for any pedal.</summary>
        public bool IsEmpty => _data.Count == 0;

        public bool Has(Pedal pedal) => _data.ContainsKey(pedal);

        /// <summary>The pedal's value at the tick (0 before its first event).</summary>
        public int ValueAt(Pedal pedal, long tick)
        {
            if (!_data.TryGetValue(pedal, out var d)) return 0;
            int i = Array.BinarySearch(d.Ticks, tick);
            if (i < 0) i = ~i - 1;
            else while (i + 1 < d.Ticks.Length && d.Ticks[i + 1] == tick) i++;
            return i >= 0 ? d.Values[i] : 0;
        }

        /// <summary>
        /// Stretches with a value above zero between <paramref name="from"/> and <paramref name="to"/>. A pedal still
        /// pressed after its last event lasts until <paramref name="songEnd"/>.
        /// </summary>
        public IEnumerable<PedalSpan> Spans(Pedal pedal, long from, long to, long songEnd)
        {
            if (!_data.TryGetValue(pedal, out var d)) yield break;
            var (ticks, values) = d;
            int start = Array.BinarySearch(ticks, from);
            if (start < 0) start = ~start - 1;
            start = Math.Max(0, start);
            for (int i = start; i < ticks.Length && ticks[i] <= to; i++)
            {
                // Of several events at the same tick, the last one wins.
                if (i + 1 < ticks.Length && ticks[i + 1] == ticks[i]) continue;
                if (values[i] <= 0) continue;
                long end = i + 1 < ticks.Length ? ticks[i + 1] : Math.Max(songEnd, ticks[i]);
                if (end < from) continue;
                yield return new PedalSpan(ticks[i], end, values[i]);
            }
        }

        public static string Name(Pedal pedal) => pedal switch
        {
            Pedal.Sustain => "Sustain",
            Pedal.Sostenuto => "Sostenuto",
            _ => "Soft",
        };

        public static string ShortName(Pedal pedal) => pedal switch
        {
            Pedal.Sustain => "SUS",
            Pedal.Sostenuto => "SOS",
            _ => "SOFT",
        };
    }
}
