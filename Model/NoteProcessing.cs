using System;
using System.Collections.Generic;
using System.Linq;

namespace nMIDI.Model
{
    /// <summary>A scale: its name and the pitch classes it contains, counted from the key.</summary>
    public sealed record Scale(string Name, int[] Steps)
    {
        public static readonly Scale[] All =
        [
            new("Major", [0, 2, 4, 5, 7, 9, 11]),
            new("Natural minor", [0, 2, 3, 5, 7, 8, 10]),
            new("Harmonic minor", [0, 2, 3, 5, 7, 8, 11]),
            new("Melodic minor", [0, 2, 3, 5, 7, 9, 11]),
            new("Dorian", [0, 2, 3, 5, 7, 9, 10]),
            new("Phrygian", [0, 1, 3, 5, 7, 8, 10]),
            new("Lydian", [0, 2, 4, 6, 7, 9, 11]),
            new("Mixolydian", [0, 2, 4, 5, 7, 9, 10]),
            new("Locrian", [0, 1, 3, 5, 6, 8, 10]),
            new("Major pentatonic", [0, 2, 4, 7, 9]),
            new("Minor pentatonic", [0, 3, 5, 7, 10]),
            new("Blues", [0, 3, 5, 6, 7, 10]),
            new("Chromatic", [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]),
        ];

        public override string ToString() => Name;

        /// <summary>True if the note is in this scale in the given key (0 = C … 11 = B).</summary>
        public bool Contains(int key, int note) => Steps.Contains(((note - key) % 12 + 12) % 12);

        /// <summary>The nearest note of the scale (ties go down), kept inside 0–127.</summary>
        public int Nearest(int key, int note)
        {
            for (int d = 0; d < 12; d++)
            {
                if (note - d >= 0 && Contains(key, note - d)) return note - d;
                if (note + d <= 127 && Contains(key, note + d)) return note + d;
            }
            return note;
        }

        /// <summary>Move a note by scale steps (diatonic transposition); a note outside the scale is first moved to the nearest one.</summary>
        public int TransposeSteps(int key, int note, int steps)
        {
            int n = Nearest(key, note);
            int direction = Math.Sign(steps);
            for (int i = 0; i < Math.Abs(steps); i++)
            {
                int next = n + direction;
                while (next is >= 0 and <= 127 && !Contains(key, next)) next += direction;
                if (next is < 0 or > 127) break;
                n = next;
            }
            return n;
        }
    }

    public enum QuantizeMode { StartKeepLength, StartAndEnd, EndOnly }

    public enum LengthMode { Set, Scale, Legato, RemoveOverlaps }

    /// <summary>Note processing commands as pure functions: each returns new notes in the same order.</summary>
    public static class NoteProcessing
    {
        /// <summary>The grid line for a tick, with every second line moved later by the swing amount.</summary>
        private static double SwungGridLine(double tick, long grid, double swing)
        {
            long index = (long)Math.Round(tick / grid);
            double line = index * grid;
            if (swing > 0 && (index & 1) == 1) line += swing * grid / 2;
            return line;
        }

        /// <summary>
        /// Move notes towards the grid. <paramref name="strength"/> 0–1 (1 = all the way);
        /// <paramref name="swing"/> 0–1 moves every second grid line later by up to half a grid
        /// (about 0.33 gives a triplet feel).
        /// </summary>
        public static List<SongNote> Quantize(IEnumerable<SongNote> notes, long grid, QuantizeMode mode, double strength = 1, double swing = 0)
        {
            if (grid <= 0) return notes.ToList();
            strength = Math.Clamp(strength, 0, 1);
            swing = Math.Clamp(swing, 0, 1);
            var result = new List<SongNote>();
            foreach (var n in notes)
            {
                long start = n.Start, end = n.End;
                if (mode != QuantizeMode.EndOnly)
                {
                    double target = SwungGridLine(n.Start, grid, swing);
                    start = Math.Max(0, (long)Math.Round(n.Start + (target - n.Start) * strength));
                    end = mode == QuantizeMode.StartKeepLength ? start + n.Length : end;
                }
                if (mode != QuantizeMode.StartKeepLength)
                {
                    double target = Math.Round(n.End / (double)grid) * grid;
                    end = (long)Math.Round(n.End + (target - n.End) * strength);
                    if (end <= start) end = start + Math.Max(1, (long)Math.Round(grid * strength));
                    if (end <= start) end = start + 1;
                }
                result.Add(n with { Start = start, End = end });
            }
            return result;
        }

        /// <summary>Transpose by semitones, optionally moving every result to the nearest note of a scale.</summary>
        public static List<SongNote> Transpose(IEnumerable<SongNote> notes, int semitones, Scale? keepIn = null, int key = 0) =>
            notes.Select(n =>
            {
                int pitch = Math.Clamp(n.Number + semitones, 0, 127);
                if (keepIn != null) pitch = keepIn.Nearest(key, pitch);
                return n with { Number = pitch };
            }).ToList();

        /// <summary>Transpose by steps of a scale (diatonic), e.g. +2 turns C–E into E–G in C major.</summary>
        public static List<SongNote> TransposeInScale(IEnumerable<SongNote> notes, int steps, Scale scale, int key) =>
            notes.Select(n => n with { Number = scale.TransposeSteps(key, n.Number, steps) }).ToList();

        /// <summary>Change note lengths: set them, scale them, make them legato, or cut overlaps of the same key.</summary>
        public static List<SongNote> ChangeLength(IReadOnlyList<SongNote> notes, LengthMode mode, long ticks = 0, double factor = 1)
        {
            switch (mode)
            {
                case LengthMode.Set:
                    return notes.Select(n => n with { End = n.Start + Math.Max(1, ticks) }).ToList();

                case LengthMode.Scale:
                    return notes.Select(n => n with { End = n.Start + Math.Max(1, (long)Math.Round(n.Length * factor)) }).ToList();

                case LengthMode.Legato:
                {
                    // Each note lasts until the next note (of any key) starts; the last ones keep their length.
                    var starts = notes.Select(n => n.Start).Distinct().OrderBy(s => s).ToArray();
                    return notes.Select(n =>
                    {
                        int i = Array.BinarySearch(starts, n.Start);
                        return i + 1 < starts.Length ? n with { End = starts[i + 1] } : n;
                    }).ToList();
                }

                default:
                {
                    // A note that is still sounding when the next note of the same key and channel starts ends there.
                    var result = notes.ToList();
                    foreach (var group in Enumerable.Range(0, notes.Count).GroupBy(i => (notes[i].Number, notes[i].Channel)))
                    {
                        var order = group.OrderBy(i => notes[i].Start).ToList();
                        for (int k = 0; k + 1 < order.Count; k++)
                        {
                            var n = result[order[k]];
                            long next = notes[order[k + 1]].Start;
                            if (n.End > next && next > n.Start) result[order[k]] = n with { End = next };
                        }
                    }
                    return result;
                }
            }
        }

        /// <summary>Random changes of timing, velocity and length, each up to ± the given amount.</summary>
        public static List<SongNote> Humanize(IEnumerable<SongNote> notes, long timing, int velocity, long length, int seed)
        {
            var random = new Random(seed);
            int Spread(long amount) => amount <= 0 ? 0 : random.Next(-(int)amount, (int)amount + 1);
            return notes.Select(n =>
            {
                long start = Math.Max(0, n.Start + Spread(timing));
                long len = Math.Max(1, n.Length + Spread(length));
                return n with { Start = start, End = start + len, Velocity = Math.Clamp(n.Velocity + Spread(velocity), 1, 127) };
            }).ToList();
        }

        /// <summary>Play the notes backwards in the time span they cover (retrograde).</summary>
        public static List<SongNote> Reverse(IReadOnlyCollection<SongNote> notes)
        {
            if (notes.Count == 0) return new List<SongNote>();
            long first = notes.Min(n => n.Start), last = notes.Max(n => n.End);
            return notes.Select(n => n with { Start = first + last - n.End, End = first + last - n.Start }).ToList();
        }

        /// <summary>Mirror the pitches around a note (by default the middle of their range).</summary>
        public static List<SongNote> Invert(IReadOnlyCollection<SongNote> notes, int? axis = null)
        {
            if (notes.Count == 0) return new List<SongNote>();
            double a = axis ?? (notes.Min(n => n.Number) + notes.Max(n => n.Number)) / 2.0;
            return notes.Select(n => n with { Number = Math.Clamp((int)Math.Round(2 * a - n.Number), 0, 127) }).ToList();
        }

        /// <summary>Scale or set velocities: new = old × factor + add, or a ramp from one value to another over time.</summary>
        public static List<SongNote> ScaleVelocity(IEnumerable<SongNote> notes, double factor, int add) =>
            notes.Select(n => n with { Velocity = Math.Clamp((int)Math.Round(n.Velocity * factor) + add, 1, 127) }).ToList();

        public static List<SongNote> RampVelocity(IReadOnlyCollection<SongNote> notes, int from, int to)
        {
            if (notes.Count == 0) return new List<SongNote>();
            long first = notes.Min(n => n.Start), last = notes.Max(n => n.Start);
            return notes.Select(n =>
            {
                double f = last > first ? (n.Start - first) / (double)(last - first) : 0;
                return n with { Velocity = Math.Clamp((int)Math.Round(from + (to - from) * f), 1, 127) };
            }).ToList();
        }
    }

    /// <summary>A rule for "Select notes": every condition that is set must hold.</summary>
    public sealed record NoteRule(int LowPitch = 0, int HighPitch = 127, int LowVelocity = 1, int HighVelocity = 127,
                                  long MinLength = 0, long MaxLength = long.MaxValue, int Channel = -1,
                                  int EveryNth = 1, int NthOffset = 0, bool OnlyInScale = false, bool OnlyOutOfScale = false,
                                  Scale? Scale = null, int Key = 0)
    {
        /// <summary>The notes (in start order) that match the rule.</summary>
        public List<SongNote> Apply(IEnumerable<SongNote> notes)
        {
            var matching = notes.Where(n => n.Number >= LowPitch && n.Number <= HighPitch
                                         && n.Velocity >= LowVelocity && n.Velocity <= HighVelocity
                                         && n.Length >= MinLength && n.Length <= MaxLength
                                         && (Channel < 0 || n.Channel == Channel)
                                         && (!OnlyInScale || Scale == null || Scale.Contains(Key, n.Number))
                                         && (!OnlyOutOfScale || Scale == null || !Scale.Contains(Key, n.Number)))
                                .ToList();
            if (EveryNth <= 1) return matching;
            return matching.Where((_, i) => i % EveryNth == ((NthOffset % EveryNth) + EveryNth) % EveryNth).ToList();
        }
    }
}
