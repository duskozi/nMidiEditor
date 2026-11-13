using System;
using System.Collections.Generic;
using System.Linq;

namespace nMIDI.Model
{
    /// <summary>
    /// Undo and redo for a song. Before every edit the window calls <see cref="Record"/>, which keeps
    /// a snapshot of the song. A snapshot holds only references: tracks never change their note and
    /// controller lists in place, so unchanged tracks share their lists with every snapshot.
    /// </summary>
    public sealed class EditHistory
    {
        private sealed record TrackState(SongTrack Track, string Name, int Channel,
                                         IReadOnlyList<SongNote> Notes, IReadOnlyList<SongCtrl> Controls, IReadOnlyList<SongMeta> Meta);

        private sealed record Snapshot(string Label, int Id, TrackState[] Tracks,
                                       IReadOnlyList<TempoChange> Tempos, IReadOnlyList<TimeSignatureChange> Signatures,
                                       IReadOnlyList<SongMeta> Conductor);

        private const int Limit = 500;

        private readonly List<Snapshot> _undo = new();
        private readonly List<Snapshot> _redo = new();
        private Song? _song;
        private int _nextId = 1;
        private int _currentId;
        private int _savedId;

        /// <summary>Raised after Record, Undo, Redo, Clear and MarkSaved.</summary>
        public event EventHandler? Changed;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>Name of the edit that Undo would take back.</summary>
        public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;

        /// <summary>Name of the edit that Redo would do again.</summary>
        public string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

        /// <summary>True when the song differs from the last saved (or loaded) state.</summary>
        public bool IsDirty => _currentId != _savedId;

        /// <summary>Start a new history for a song that was just loaded or created.</summary>
        public void Reset(Song song)
        {
            _song = song;
            _undo.Clear();
            _redo.Clear();
            _currentId = _savedId = 0;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Remember the song as it is now, before an edit called <paramref name="label"/>.</summary>
        public void Record(string label)
        {
            if (_song == null) return;
            _undo.Add(Take(_song, label, _currentId));
            if (_undo.Count > Limit) _undo.RemoveAt(0);
            _redo.Clear();
            _currentId = _nextId++;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Take back the last edit. Returns its name, or null if there was nothing to undo.</summary>
        public string? Undo()
        {
            if (_song == null || _undo.Count == 0) return null;
            var snapshot = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(Take(_song, snapshot.Label, _currentId));
            Restore(_song, snapshot);
            _currentId = snapshot.Id;
            Changed?.Invoke(this, EventArgs.Empty);
            return snapshot.Label;
        }

        /// <summary>Do the last undone edit again. Returns its name, or null.</summary>
        public string? Redo()
        {
            if (_song == null || _redo.Count == 0) return null;
            var snapshot = _redo[^1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(Take(_song, snapshot.Label, _currentId));
            Restore(_song, snapshot);
            _currentId = snapshot.Id;
            Changed?.Invoke(this, EventArgs.Empty);
            return snapshot.Label;
        }

        /// <summary>Drop the last <see cref="Record"/> when the edit turned out to change nothing.</summary>
        public void Cancel()
        {
            if (_undo.Count == 0) return;
            _currentId = _undo[^1].Id;
            _undo.RemoveAt(_undo.Count - 1);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void MarkSaved()
        {
            _savedId = _currentId;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private static Snapshot Take(Song song, string label, int id) => new(
            label, id,
            song.Tracks.Select(t => new TrackState(t, t.Name, t.Channel, t.Notes, t.Controls, t.Meta)).ToArray(),
            song.Tempos, song.TimeSignatures, song.Conductor);

        private static void Restore(Song song, Snapshot s)
        {
            foreach (var t in s.Tracks)
            {
                t.Track.Name = t.Name;
                t.Track.Channel = t.Channel;
                t.Track.Notes = t.Notes;
                t.Track.Controls = t.Controls;
                t.Track.Meta = t.Meta;
            }
            song.SetTracks(s.Tracks.Select(t => t.Track));
            song.Tempos = s.Tempos;
            song.TimeSignatures = s.Signatures;
            song.Conductor = s.Conductor;
            song.Rebuild();
        }
    }
}
