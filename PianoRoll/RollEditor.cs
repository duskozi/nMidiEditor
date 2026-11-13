using NinePatchWpf;
using nMIDI.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

namespace nMIDI.PianoRoll
{
    public enum EditTool { Select, Draw, Erase, Split }

    /// <summary>What the piano roll editor needs from the window.</summary>
    public interface IRollHost
    {
        Song? Song { get; }

        /// <summary>The track that is edited (new notes go here).</summary>
        SongTrack? ActiveTrack { get; }

        /// <summary>Every edited track: the active one first, then the ones edited with it.</summary>
        IReadOnlyList<SongTrack> EditTracks { get; }

        /// <summary>Tracks whose notes are shown; clicking one of their notes makes that track active.</summary>
        IEnumerable<SongTrack> ShownTracks { get; }

        /// <summary>Grid in ticks, 0 = no snapping.</summary>
        long SnapTicks { get; }

        /// <summary>Length of a newly drawn note.</summary>
        long NewNoteLength { get; }

        int NewVelocity { get; }

        /// <summary>The user drew or resized a note to this length.</summary>
        void RememberNoteLength(long ticks);

        /// <summary>Make an undoable edit: record the song, run <paramref name="change"/>, then refresh everything.</summary>
        void Commit(string label, Action change);

        /// <summary>Play a note briefly on the active track's port and channel.</summary>
        void Audition(int note, int velocity);

        void ActivateTrack(SongTrack track);

        /// <summary>Redraw the notes (selection or drag preview changed).</summary>
        void RefreshNotes();

        void ShowContextMenu();

        /// <summary>The selection changed.</summary>
        void SelectionChanged();

        /// <summary>The pitch a drawn note gets (the nearest note of the scale, when snapping to a scale).</summary>
        int SnapPitch(int note);
    }

    /// <summary>
    /// Mouse editing in the piano roll: select, draw, move, copy, resize, erase and split notes.
    /// Only the active track is edited. While a drag is going on, <see cref="Hidden"/> and
    /// <see cref="Preview"/> describe what the notes will look like, and the window draws that.
    /// </summary>
    public sealed class RollEditor
    {
        private enum Drag { None, Pending, Move, ResizeEnd, ResizeStart, Rubber, Draw, Erase }
        private enum Edge { None, Left, Right }

        private const double DragThreshold = 4;

        private readonly IRollHost _host;
        private NinePatchZoomViewer _viewer = null!;
        private FrameworkElement _surface = null!;
        private Rectangle _rubber = null!;

        private Drag _drag;
        private Drag _pendingAs;           // what a pending press turns into when the mouse moves
        private Point _downPoint;
        private double _downTick;
        private int _downNote;
        private SongNote? _anchor;         // the note that was pressed
        private bool _toggleOnClick;       // Ctrl+click without moving toggles the note
        private bool _selectOnlyOnClick;   // a click without moving on a selected note selects only it
        private bool _copy;                // Ctrl+drag copies
        private int _lastPreviewNote = -1;
        private HashSet<SongNote> _rubberBase = NoteOps.NewSet();
        private readonly HashSet<SongNote> _erased = NoteOps.NewSet();

        public RollEditor(IRollHost host) => _host = host;

        public EditTool Tool { get; set; } = EditTool.Select;

        /// <summary>Selected notes of the active track.</summary>
        public HashSet<SongNote> Selection { get; private set; } = NoteOps.NewSet();

        /// <summary>Notes of the active track not to draw while dragging (they are shown moved in <see cref="Preview"/>).</summary>
        public HashSet<SongNote> Hidden { get; } = NoteOps.NewSet();

        /// <summary>Notes to draw, selected, on top of the active track while dragging.</summary>
        public List<SongNote> Preview { get; } = new();

        public bool IsDragging => _drag != Drag.None;

        public void Attach(NinePatchZoomViewer viewer, FrameworkElement surface, Rectangle rubber)
        {
            _viewer = viewer;
            _surface = surface;
            _rubber = rubber;
            viewer.PreviewMouseLeftButtonDown += OnLeftDown;
            viewer.PreviewMouseRightButtonDown += OnRightDown;
            viewer.PreviewMouseMove += OnMove;
            viewer.PreviewMouseLeftButtonUp += OnLeftUp;
            viewer.PreviewMouseRightButtonUp += OnRightUp;
            surface.LostMouseCapture += (_, _) => { if (_drag != Drag.None) CancelDrag(); };
        }

        // --- Selection ---

        public void SetSelection(IEnumerable<SongNote> notes)
        {
            Selection = NoteOps.NewSet(notes);
            _host.SelectionChanged();
            _host.RefreshNotes();
        }

        public void ClearSelection()
        {
            if (Selection.Count == 0) return;
            SetSelection(Array.Empty<SongNote>());
        }

        /// <summary>Keep only selected notes that are still in the active track (after undo, or a track change).</summary>
        public void ValidateSelection()
        {
            int before = Selection.Count;
            var present = NoteOps.NewSet(_host.EditTracks.SelectMany(t => t.Notes));
            Selection.RemoveWhere(n => !present.Contains(n));
            if (Selection.Count != before) _host.SelectionChanged();
        }

        /// <summary>The selected notes, sorted, or an empty list.</summary>
        public List<SongNote> SelectedNotes()
        {
            var tracks = _host.EditTracks;
            if (tracks.Count == 1) return tracks[0].Notes.Where(Selection.Contains).ToList();
            return SongTrack.Sort(tracks.SelectMany(t => t.Notes.Where(Selection.Contains)));
        }

        // --- Coordinates and hit testing ---

        private bool IsInside(Point p) =>
            p.X >= 0 && p.Y >= 0 && p.X < _viewer.ViewportSize.Width && p.Y < _viewer.ViewportSize.Height;

        private double TickAt(Point p) => Math.Max(0, _viewer.ViewToWorldX(p.X));

        private int NoteAt(Point p) => Math.Clamp(PianoRollStyle.MaxNote - (int)Math.Floor(_viewer.ViewToWorldY(p.Y)), 0, 127);

        private static bool AltDown => Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        /// <summary>The grid, or none while Alt is held.</summary>
        private long Grid => AltDown ? 0 : _host.SnapTicks;

        private (SongTrack Track, SongNote Note, Edge Edge)? HitTest(Point p, bool activeOnly)
        {
            var edit = _host.EditTracks;
            var tracks = new List<SongTrack>(edit);
            if (!activeOnly) tracks.AddRange(_host.ShownTracks.Where(t => !edit.Contains(t)).Reverse());   // later tracks draw on top

            int key = NoteAt(p);
            foreach (var track in tracks)
            {
                SongNote? best = null;
                Edge bestEdge = Edge.None;
                foreach (var n in track.Notes)
                {
                    if (n.Number != key) continue;
                    double x1 = _viewer.WorldToViewX(n.Start);
                    if (x1 > p.X) break;   // sorted by start
                    double x2 = Math.Max(x1 + 3, _viewer.WorldToViewX(n.End));
                    if (p.X > x2) continue;

                    double width = x2 - x1;
                    double grip = Math.Min(6, width / 3);
                    var edge = p.X - x1 <= grip && width >= 9 ? Edge.Left : x2 - p.X <= grip ? Edge.Right : Edge.None;
                    best = n;   // the latest starting note wins: it is drawn on top
                    bestEdge = edge;
                }
                if (best != null) return (track, best, bestEdge);
            }
            return null;
        }

        // --- Mouse ---

        private void OnLeftDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(_surface);
            if (!IsInside(p) || _host.Song == null || _host.ActiveTrack == null) return;
            e.Handled = true;
            _viewer.Focus();

            var mods = Keyboard.Modifiers;
            bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift);
            _downPoint = p;
            _downTick = TickAt(p);
            _downNote = NoteAt(p);
            _toggleOnClick = _selectOnlyOnClick = _copy = false;

            switch (Tool)
            {
                case EditTool.Erase:
                    BeginErase(p);
                    return;
                case EditTool.Split:
                    SplitAt(p);
                    return;
            }

            var hit = HitTest(p, activeOnly: Tool == EditTool.Draw);
            if (hit is { } h)
            {
                if (!_host.EditTracks.Contains(h.Track))
                {
                    _host.ActivateTrack(h.Track);
                    Selection.Clear();
                }

                _anchor = h.Note;
                if (ctrl && h.Edge == Edge.None)
                {
                    // Ctrl+click toggles; Ctrl+drag copies (the pressed note joins the selection for that).
                    _toggleOnClick = true;
                    _copy = true;
                }
                else if (!Selection.Contains(h.Note))
                {
                    if (!shift) Selection.Clear();
                    Selection.Add(h.Note);
                }
                else if (!shift) _selectOnlyOnClick = true;

                _pendingAs = h.Edge switch { Edge.Left => Drag.ResizeStart, Edge.Right => Drag.ResizeEnd, _ => Drag.Move };
                _drag = Drag.Pending;
                _host.Audition(h.Note.Number, h.Note.Velocity);
                _host.SelectionChanged();
                _host.RefreshNotes();
                _surface.CaptureMouse();
                return;
            }

            if (Tool == EditTool.Draw || e.ClickCount == 2)
            {
                BeginDraw(p);
                return;
            }

            // Rubber band on empty space.
            _rubberBase = shift || ctrl ? NoteOps.NewSet(Selection) : NoteOps.NewSet();
            if (!shift && !ctrl && Selection.Count > 0)
            {
                Selection.Clear();
                _host.SelectionChanged();
                _host.RefreshNotes();
            }
            _pendingAs = Drag.Rubber;
            _drag = Drag.Pending;
            _surface.CaptureMouse();
        }

        private void OnRightDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(_surface);
            if (!IsInside(p) || _host.Song == null || _host.ActiveTrack == null) return;
            e.Handled = true;
            _viewer.Focus();
            if (_drag != Drag.None) return;

            if (Tool is EditTool.Draw or EditTool.Erase)
            {
                // Right-drag erases in the draw tool, as in most piano rolls.
                BeginErase(p);
                return;
            }

            var hit = HitTest(p, activeOnly: false);
            if (hit is { } h)
            {
                if (!_host.EditTracks.Contains(h.Track))
                {
                    _host.ActivateTrack(h.Track);
                    Selection.Clear();
                }
                if (!Selection.Contains(h.Note))
                {
                    Selection.Clear();
                    Selection.Add(h.Note);
                    _host.SelectionChanged();
                    _host.RefreshNotes();
                }
            }
        }

        private void OnRightUp(object sender, MouseButtonEventArgs e)
        {
            if (_drag == Drag.Erase)
            {
                e.Handled = true;
                EndErase();
                return;
            }
            var p = e.GetPosition(_surface);
            if (!IsInside(p) || _host.Song == null) return;
            e.Handled = true;
            if (Tool == EditTool.Select || Tool == EditTool.Split) _host.ShowContextMenu();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(_surface);
            if (_drag == Drag.None)
            {
                UpdateCursor(p);
                return;
            }
            e.Handled = true;

            if (_drag == Drag.Pending)
            {
                if (Math.Abs(p.X - _downPoint.X) < DragThreshold && Math.Abs(p.Y - _downPoint.Y) < DragThreshold) return;
                _drag = _pendingAs;
                if (_drag is Drag.Move or Drag.ResizeEnd or Drag.ResizeStart && _anchor != null)
                {
                    if (_toggleOnClick && !Selection.Contains(_anchor!))
                    {
                        Selection.Add(_anchor!);
                        _host.SelectionChanged();
                    }
                    _copy &= _drag == Drag.Move;
                    _lastPreviewNote = _anchor!.Number;
                }
            }

            switch (_drag)
            {
                case Drag.Move: UpdateMove(p); break;
                case Drag.ResizeEnd: UpdateResize(p, end: true); break;
                case Drag.ResizeStart: UpdateResize(p, end: false); break;
                case Drag.Rubber: UpdateRubber(p); break;
                case Drag.Draw: UpdateDraw(p); break;
                case Drag.Erase: EraseAt(p); break;
            }
        }

        private void OnLeftUp(object sender, MouseButtonEventArgs e)
        {
            if (_drag == Drag.None) return;
            e.Handled = true;
            var drag = _drag;
            _drag = Drag.None;
            if (_surface.IsMouseCaptured) _surface.ReleaseMouseCapture();

            switch (drag)
            {
                case Drag.Pending:
                    if (_anchor != null && _toggleOnClick)
                    {
                        if (!Selection.Remove(_anchor)) Selection.Add(_anchor);
                    }
                    else if (_anchor != null && _selectOnlyOnClick)
                    {
                        Selection.Clear();
                        Selection.Add(_anchor);
                    }
                    _host.SelectionChanged();
                    _host.RefreshNotes();
                    break;
                case Drag.Move:
                case Drag.ResizeEnd:
                case Drag.ResizeStart:
                case Drag.Draw:
                    CommitPreview(drag);
                    break;
                case Drag.Rubber:
                    _rubber.Visibility = Visibility.Collapsed;
                    break;
                case Drag.Erase:
                    _drag = Drag.Erase;
                    EndErase();
                    break;
            }
            _anchor = null;
            UpdateCursor(e.GetPosition(_surface));
        }

        /// <summary>Stop a drag without changing anything (Esc, or the mouse capture was lost).</summary>
        public void CancelDrag()
        {
            if (_drag == Drag.None) return;
            _drag = Drag.None;
            Hidden.Clear();
            Preview.Clear();
            _erased.Clear();
            _rubber.Visibility = Visibility.Collapsed;
            if (_surface.IsMouseCaptured) _surface.ReleaseMouseCapture();
            _host.RefreshNotes();
        }

        private void UpdateCursor(Point p)
        {
            if (!IsInside(p) || _host.Song == null)
            {
                _surface.Cursor = null;
                return;
            }
            _surface.Cursor = Tool switch
            {
                EditTool.Erase => Cursors.No,
                EditTool.Split => HitTest(p, activeOnly: true) != null ? Cursors.IBeam : null,
                _ => HitTest(p, activeOnly: Tool == EditTool.Draw) switch
                {
                    { Edge: not Edge.None } => Cursors.SizeWE,
                    not null => Cursors.SizeAll,
                    null => Tool == EditTool.Draw ? Cursors.Pen : null,
                },
            };
        }

        // --- Move, copy and resize ---

        private void UpdateMove(Point p)
        {
            if (_anchor == null) return;
            long grid = Grid;
            double rawStart = _anchor.Start + (TickAt(p) - _downTick);
            long dt = (grid > 0 ? NoteOps.Snap(rawStart, grid) : (long)Math.Round(rawStart)) - _anchor.Start;
            int dp = NoteAt(p) - _downNote;

            var selected = SelectedNotes();
            var moved = NoteOps.Move(selected, dt, dp);
            Hidden.Clear();
            if (!_copy) Hidden.UnionWith(selected);
            Preview.Clear();
            Preview.AddRange(moved);

            int anchorIndex = selected.FindIndex(n => ReferenceEquals(n, _anchor));
            if (anchorIndex >= 0 && moved[anchorIndex].Number != _lastPreviewNote)
            {
                _lastPreviewNote = moved[anchorIndex].Number;
                _host.Audition(_lastPreviewNote, _anchor.Velocity);
            }
            _host.RefreshNotes();
        }

        private void UpdateResize(Point p, bool end)
        {
            if (_anchor == null) return;
            long grid = Grid;
            long minLength = grid > 0 ? grid : 1;
            double delta = TickAt(p) - _downTick;
            var selected = SelectedNotes();
            List<SongNote> resized;
            if (end)
            {
                long newEnd = Math.Max(_anchor.Start + minLength, NoteOps.Snap(_anchor.End + delta, grid));
                resized = NoteOps.ResizeEnds(selected, newEnd - _anchor.End, minLength);
            }
            else
            {
                long newStart = Math.Clamp(NoteOps.Snap(_anchor.Start + delta, grid), 0, _anchor.End - minLength);
                resized = NoteOps.ResizeStarts(selected, newStart - _anchor.Start, minLength);
            }
            Hidden.Clear();
            Hidden.UnionWith(selected);
            Preview.Clear();
            Preview.AddRange(resized);
            _host.RefreshNotes();
        }

        private void CommitPreview(Drag drag)
        {
            var track = _host.ActiveTrack;
            var preview = Preview.ToList();
            var hidden = Hidden.ToList();
            Hidden.Clear();
            Preview.Clear();
            if (track == null || preview.Count == 0)
            {
                _host.RefreshNotes();
                return;
            }

            // Nothing moved: no edit, no undo step.
            var before = drag == Drag.Draw ? new List<SongNote>() : SelectedNotes();
            if (drag != Drag.Draw && before.Count == preview.Count && before.Zip(preview).All(x => x.First == x.Second))
            {
                _host.RefreshNotes();
                return;
            }

            if (drag is Drag.ResizeEnd or Drag.ResizeStart or Drag.Draw)
            {
                var anchorIndex = drag == Drag.Draw ? 0 : before.FindIndex(n => ReferenceEquals(n, _anchor));
                if (anchorIndex >= 0 && anchorIndex < preview.Count) _host.RememberNoteLength(preview[anchorIndex].Length);
            }

            string label = drag switch
            {
                Drag.Draw => "Draw note",
                Drag.Move when _copy => preview.Count == 1 ? "Copy note" : "Copy notes",
                Drag.Move => preview.Count == 1 ? "Move note" : "Move notes",
                _ => preview.Count == 1 ? "Resize note" : "Resize notes",
            };
            var tracks = _host.EditTracks;
            _host.Commit(label, () =>
            {
                if (drag == Drag.Draw) track.Notes = NoteOps.Replace(track.Notes, hidden, preview);
                else NoteOps.ReplaceInTracks(tracks, hidden, preview, before, track, moveLocks: drag == Drag.Move && !_copy);
            });
            SetSelection(preview);
        }

        // --- Draw ---

        private void BeginDraw(Point p)
        {
            var track = _host.ActiveTrack!;
            long grid = Grid;
            long start = NoteOps.SnapDown(TickAt(p), grid);
            int key = _host.SnapPitch(NoteAt(p));
            var note = new SongNote(key, track.Channel, start, start + Math.Max(1, _host.NewNoteLength), _host.NewVelocity);

            Selection.Clear();
            _host.SelectionChanged();
            Hidden.Clear();
            Preview.Clear();
            Preview.Add(note);
            _anchor = note;
            _drag = Drag.Draw;
            _surface.CaptureMouse();
            _host.Audition(key, note.Velocity);
            _host.RefreshNotes();
        }

        private void UpdateDraw(Point p)
        {
            if (_anchor == null) return;
            long grid = Grid;
            long minLength = grid > 0 ? grid : 1;
            double tick = TickAt(p);
            // Dragging to the right sets the length; a click keeps the default length.
            long end = tick > _anchor.Start + minLength / 2.0
                ? Math.Max(_anchor.Start + minLength, NoteOps.Snap(tick, grid))
                : _anchor.End;
            Preview.Clear();
            Preview.Add(_anchor with { End = Math.Max(end, _anchor.Start + 1) });
            _host.RefreshNotes();
        }

        // --- Erase and split ---

        private void BeginErase(Point p)
        {
            _erased.Clear();
            Hidden.Clear();
            Preview.Clear();
            _drag = Drag.Erase;
            _surface.CaptureMouse();
            EraseAt(p);
        }

        private void EraseAt(Point p)
        {
            if (HitTest(p, activeOnly: true) is { } h && _erased.Add(h.Note))
            {
                Hidden.Add(h.Note);
                _host.RefreshNotes();
            }
        }

        private void EndErase()
        {
            _drag = Drag.None;
            if (_surface.IsMouseCaptured) _surface.ReleaseMouseCapture();
            var track = _host.ActiveTrack;
            var erased = _erased.ToList();
            _erased.Clear();
            Hidden.Clear();
            if (track == null || erased.Count == 0)
            {
                _host.RefreshNotes();
                return;
            }
            var tracks = _host.EditTracks;
            _host.Commit(erased.Count == 1 ? "Erase note" : "Erase notes",
                         () => NoteOps.ReplaceInTracks(tracks, erased, Array.Empty<SongNote>(), null, track));
            ValidateSelection();
            _host.RefreshNotes();
        }

        private void SplitAt(Point p)
        {
            var track = _host.ActiveTrack;
            if (track == null || HitTest(p, activeOnly: true) is not { } h) return;
            long tick = NoteOps.Snap(TickAt(p), Grid);
            if (tick <= h.Note.Start || tick >= h.Note.End) tick = (long)Math.Round(TickAt(p));
            if (tick <= h.Note.Start || tick >= h.Note.End) return;

            // A selected note splits the whole selection at that point; otherwise only the clicked note.
            var targets = Selection.Contains(h.Note) ? SelectedNotes() : new List<SongNote> { h.Note };
            // Split each track's notes in that track.
            var parts = _host.EditTracks.Select(t => (Track: t, Notes: t.Notes.Where(n => targets.Contains(n, ReferenceEqualityComparer.Instance)).ToList()))
                                        .Where(x => x.Notes.Count > 0).Select(x => (x.Track, x.Notes, Split: NoteOps.Split(x.Notes, tick))).ToList();
            if (parts.All(x => x.Split.Count == x.Notes.Count)) return;
            _host.Commit("Split notes", () =>
            {
                foreach (var x in parts) x.Track.Notes = NoteOps.Replace(x.Track.Notes, x.Notes, x.Split);
            });
            SetSelection(parts.SelectMany(x => x.Split));
        }

        // --- Rubber band ---

        private void UpdateRubber(Point p)
        {
            var track = _host.ActiveTrack;
            if (track == null) return;
            var rect = new Rect(_downPoint, p);
            Canvas.SetLeft(_rubber, rect.X);
            Canvas.SetTop(_rubber, rect.Y);
            _rubber.Width = rect.Width;
            _rubber.Height = rect.Height;
            _rubber.Visibility = Visibility.Visible;

            double t1 = _viewer.ViewToWorldX(rect.Left), t2 = _viewer.ViewToWorldX(rect.Right);
            int high = Math.Clamp(PianoRollStyle.MaxNote - (int)Math.Floor(_viewer.ViewToWorldY(rect.Top)), 0, 127);
            int low = Math.Clamp(PianoRollStyle.MaxNote - (int)Math.Floor(_viewer.ViewToWorldY(rect.Bottom)), 0, 127);

            var selection = NoteOps.NewSet(_rubberBase);
            foreach (var t in _host.EditTracks)
                foreach (var n in t.Notes)
                {
                    if (n.Start > t2) break;
                    if (n.End >= t1 && n.Number >= low && n.Number <= high) selection.Add(n);
                }
            Selection = selection;
            _host.SelectionChanged();
            _host.RefreshNotes();
        }
    }
}
