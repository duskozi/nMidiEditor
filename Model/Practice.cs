using nMIDI.Theory;
using System;
using System.Collections.Generic;
using System.Linq;

namespace nMIDI.Model
{
    /// <summary>Which hand the player practises.</summary>
    public enum PracticeHands { Both, Right, Left }

    /// <summary>A note the player has to play, with its hand and finger.</summary>
    public readonly record struct PracticeNote(SongNote Note, int Track, Hand Hand, int Finger);

    /// <summary>The keys that start together (a single note or a chord): one step of wait mode.</summary>
    public sealed class PracticeStep
    {
        public PracticeStep(long tick, IReadOnlyList<PracticeNote> notes)
        {
            Tick = tick;
            Notes = notes;
            Keys = notes.Select(n => n.Note.Number).Distinct().OrderBy(k => k).ToArray();
        }

        public long Tick { get; }
        public IReadOnlyList<PracticeNote> Notes { get; }

        /// <summary>The distinct keys of the step, low to high.</summary>
        public int[] Keys { get; }
    }

    /// <summary>
    /// The part to practise: the notes of the chosen tracks and hand, grouped into steps. A note's hand comes from
    /// the fingering; notes without one are split at middle C (below it the left hand).
    /// </summary>
    public sealed class PracticeScript
    {
        public PracticeScript(IEnumerable<PracticeNote> notes, PracticeHands hands, long chordWindow)
        {
            var chosen = notes.Where(n => Plays(n, hands)).OrderBy(n => n.Note.Start).ThenBy(n => n.Note.Number).ToList();
            Notes = chosen;
            var steps = new List<PracticeStep>();
            int i = 0;
            while (i < chosen.Count)
            {
                long start = chosen[i].Note.Start;
                int j = i;
                // Keys that start within the window of the first one belong to the same chord (a rolled or
                // slightly loose chord in a recorded file).
                while (j < chosen.Count && chosen[j].Note.Start - start <= chordWindow) j++;
                steps.Add(new PracticeStep(start, chosen.GetRange(i, j - i)));
                i = j;
            }
            Steps = steps;
        }

        /// <summary>The notes the player plays (the player leaves them out of the playback).</summary>
        public IReadOnlyList<PracticeNote> Notes { get; }

        public IReadOnlyList<PracticeStep> Steps { get; }

        public static Hand HandOf(PracticeNote n) =>
            n.Hand != Hand.None ? n.Hand : n.Note.Number < 60 ? Hand.Left : Hand.Right;

        private static bool Plays(PracticeNote n, PracticeHands hands) => hands switch
        {
            PracticeHands.Right => HandOf(n) == Hand.Right,
            PracticeHands.Left => HandOf(n) == Hand.Left,
            _ => true,
        };

        /// <summary>The first step at or after the tick, or Steps.Count.</summary>
        public int StepAt(long tick)
        {
            int lo = 0, hi = Steps.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (Steps[mid].Tick < tick) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// A copy of the song without the practised notes, for the player: the computer plays the rest
        /// (the other hand, the other tracks) and the player plays these.
        /// </summary>
        public Song Accompaniment(Song song)
        {
            var remove = Notes.GroupBy(n => n.Track).ToDictionary(g => g.Key, g => g.Select(n => n.Note).ToHashSet(ReferenceEqualityComparer.Instance));
            var copy = new Song((int)song.TicksPerQuarter)
            {
                Tempos = song.Tempos,
                TimeSignatures = song.TimeSignatures,
                Conductor = song.Conductor,
            };
            var tracks = new List<SongTrack>();
            foreach (var t in song.Tracks)
            {
                var c = new SongTrack(t.Name, t.Channel) { Meta = t.Meta };
                c.SetNotes(remove.TryGetValue(t.Index, out var set) ? t.Notes.Where(n => !set.Contains(n)) : t.Notes);
                c.SetControls(t.Controls);
                tracks.Add(c);
            }
            copy.SetTracks(tracks);
            copy.Rebuild();
            return copy;
        }
    }

    /// <summary>What a key press did.</summary>
    public enum PressResult { Correct, Wrong, Ignored }

    /// <summary>
    /// One run through the part. In wait mode the current step waits until all of its keys have been pressed, in
    /// any order; playing ahead is fine. In play-along mode the song does not wait: a key pressed near a note's start
    /// is a hit (early or late by so many ticks), a note whose window passes unplayed is a miss.
    /// </summary>
    public sealed class PracticeSession
    {
        private readonly PracticeScript _script;
        private readonly HashSet<int> _pressed = new();
        private readonly bool[] _played;
        private int _nextUnjudged;
        private bool _stepClean = true;

        public PracticeSession(PracticeScript script, bool wait, long window, long fromTick = 0, long toTick = long.MaxValue)
        {
            _script = script;
            Wait = wait;
            Window = Math.Max(1, window);
            ToTick = toTick;
            Current = script.StepAt(fromTick);
            FirstStep = Current;
            _played = new bool[script.Notes.Count];
            _nextUnjudged = _firstNote = FirstNoteAt(fromTick);
        }

        public bool Wait { get; }

        /// <summary>How far from a note's start a key still counts, in ticks (play-along).</summary>
        public long Window { get; }

        public long ToTick { get; }

        public int FirstStep { get; }

        /// <summary>The step being waited for (wait mode), or Steps.Count when the part is done.</summary>
        public int Current { get; private set; }

        public PracticeStep? CurrentStep => Current < _script.Steps.Count && _script.Steps[Current].Tick < ToTick ? _script.Steps[Current] : null;

        public PracticeStep? NextStep => Current + 1 < _script.Steps.Count && _script.Steps[Current + 1].Tick < ToTick ? _script.Steps[Current + 1] : null;

        /// <summary>Keys of the current step already pressed.</summary>
        public IReadOnlyCollection<int> Pressed => _pressed;

        public bool IsDone => Wait ? CurrentStep == null : _nextUnjudged >= _played.Length || _script.Notes[_nextUnjudged].Note.Start >= ToTick;

        // Score
        public int Steps { get; private set; }
        public int CleanSteps { get; private set; }
        public int WrongKeys { get; private set; }
        public int Hits { get; private set; }
        public int Misses { get; private set; }
        public long TotalOffset { get; private set; }

        /// <summary>Steps (wait) or notes (play-along) to play in the range.</summary>
        public int Total => Wait
            ? _script.Steps.Skip(FirstStep).TakeWhile(s => s.Tick < ToTick).Count()
            : _script.Notes.Skip(_firstNote).TakeWhile(n => n.Note.Start < ToTick).Count();

        private readonly int _firstNote;

        /// <summary>0–100: clean steps (wait) or hits less wrong keys (play-along), out of the total.</summary>
        public int Accuracy
        {
            get
            {
                int total = Wait ? Steps : Hits + Misses;
                if (total == 0) return 100;
                double good = Wait ? CleanSteps : Math.Max(0, Hits - WrongKeys * 0.5);
                return (int)Math.Round(100 * good / total);
            }
        }

        private int FirstNoteAt(long tick)
        {
            int i = 0;
            while (i < _script.Notes.Count && _script.Notes[i].Note.Start < tick - Window) i++;
            return i;
        }

        /// <summary>A key went down at the tick.</summary>
        public PressResult Press(int key, long tick)
        {
            if (Wait)
            {
                var step = CurrentStep;
                if (step == null) return PressResult.Ignored;
                if (Array.IndexOf(step.Keys, key) >= 0)
                {
                    _pressed.Add(key);
                    if (_pressed.Count == step.Keys.Length)
                    {
                        Steps++;
                        if (_stepClean) CleanSteps++;
                        _pressed.Clear();
                        _stepClean = true;
                        Current++;
                    }
                    return PressResult.Correct;
                }
                WrongKeys++;
                _stepClean = false;
                return PressResult.Wrong;
            }

            // Play-along: the nearest unplayed note of this key within the window.
            int best = -1;
            long bestDistance = long.MaxValue;
            for (int i = _nextUnjudged; i < _played.Length; i++)
            {
                var n = _script.Notes[i].Note;
                if (n.Start > tick + Window) break;
                if (_played[i] || n.Number != key) continue;
                long d = Math.Abs(tick - n.Start);
                if (d <= Window && d < bestDistance) { best = i; bestDistance = d; }
            }
            if (best < 0)
            {
                WrongKeys++;
                return PressResult.Wrong;
            }
            _played[best] = true;
            Hits++;
            TotalOffset += tick - _script.Notes[best].Note.Start;
            return PressResult.Correct;
        }

        /// <summary>Play-along: notes whose window has passed unplayed are misses. Call as the song moves on.</summary>
        public void Advance(long tick)
        {
            if (Wait) return;
            while (_nextUnjudged < _played.Length)
            {
                var n = _script.Notes[_nextUnjudged].Note;
                if (n.Start >= ToTick || n.Start + Window >= tick) break;
                if (!_played[_nextUnjudged]) Misses++;
                _nextUnjudged++;
            }
        }

        /// <summary>Play-along: the notes to play next (not yet played, starting at or after a little before the tick).</summary>
        public IEnumerable<PracticeNote> Upcoming(long tick, long ahead)
        {
            for (int i = _nextUnjudged; i < _played.Length; i++)
            {
                var n = _script.Notes[i];
                if (n.Note.Start > tick + ahead || n.Note.Start >= ToTick) yield break;
                if (!_played[i] && n.Note.End > tick - Window) yield return n;
            }
        }

        /// <summary>A one-line score, e.g. "32 of 40 steps clean · 5 wrong keys · 80 %".</summary>
        public string Summary(double ticksPerMs)
        {
            if (Wait)
                return $"{CleanSteps} of {Steps} steps clean · {WrongKeys} wrong key{(WrongKeys == 1 ? "" : "s")} · {Accuracy} %";
            string timing = "";
            if (Hits > 0 && ticksPerMs > 0)
            {
                double ms = TotalOffset / (double)Hits / ticksPerMs;
                timing = Math.Abs(ms) < 15 ? " · on time" : ms < 0 ? $" · {-ms:0} ms early" : $" · {ms:0} ms late";
            }
            return $"{Hits} hit{(Hits == 1 ? "" : "s")} · {Misses} missed · {WrongKeys} wrong{timing} · {Accuracy} %";
        }
    }
}
