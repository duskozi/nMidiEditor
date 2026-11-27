using nMIDI.PianoRoll;
using System;
using System.Linq;
using System.Windows;

namespace nMIDI
{
    /// <summary>
    /// The drum view: when the edited track is a drum track (channel 10) and Drums is on, the piano roll shows one
    /// tall, named row per drum (a basic kit plus every drum the track uses) instead of 128 piano keys, and drawn
    /// notes are fixed-length hits; dragging the draw tool paints a hit on every grid step.
    /// </summary>
    public partial class MainWindow
    {
        private bool DrumViewWanted => tglDrumView.IsChecked == true && _activeVm?.Track.IsDrums == true;

        /// <summary>Length of a drawn drum hit: the grid, or a 16th note without a grid.</summary>
        private long DrumHitLength => Math.Max(1, SnapTicks > 0 ? SnapTicks : (_song?.TicksPerQuarter ?? 480) / 4);

        /// <summary>Row height that fits the drum rows in the view, readable but not huge.</summary>
        private double DrumRowHeight()
        {
            double height = viewer.ViewportSize.Height;
            int rows = Math.Max(1, PianoRollStyle.Rows.Count);
            return height > 0 ? Math.Clamp(height / rows, 14, 32) : 18;
        }

        /// <summary>Use the piano or the drum rows for the edited track; called before the notes are drawn.</summary>
        private void UpdateRollRows()
        {
            var rows = RollRows.Piano;
            if (DrumViewWanted)
            {
                var used = Tracks.Where(v => (v == _activeVm || _coEdited.Contains(v)) && v.Track.IsDrums)
                                 .SelectMany(v => v.Track.Notes)
                                 .Select(n => n.Number);
                rows = RollRows.Drums(used);
            }

            var old = PianoRollStyle.Rows;
            if (rows.SameLayout(old)) return;
            PianoRollStyle.Rows = rows;
            viewer.ContentHeight = rows.Count;

            if (rows.IsDrums != old.IsDrums)
            {
                _editor.CancelDrag();
                if (rows.IsDrums)
                {
                    // Every drum in view, the bottom of the kit (bass drum) at the bottom.
                    viewer.SetView(double.NaN, double.NaN, DrumRowHeight(), 0);
                }
                else
                {
                    // Back to the piano rows: centre on the edited track's notes.
                    var notes = _activeVm?.Track.Notes;
                    double middle = notes is { Count: > 0 } ? (notes.Min(n => n.Number) + notes.Max(n => n.Number)) / 2.0 : 60;
                    double rowHeight = Math.Clamp(viewer.ViewportSize.Height / 40, 9, 16);
                    viewer.SetView(double.NaN, double.NaN, double.IsFinite(rowHeight) ? rowHeight : 12, double.NaN);
                    viewer.ScrollToY(MaxNote - middle, 0.5);
                }
            }
            pianoKeys.Width = rows.IsDrums ? 116 : 60;   // room for the drum names
            pianoKeys.ToolTip = rows.IsDrums
                ? "Click a drum to hear it and select all its hits in the edited track (Shift adds)"
                : "Click a key to hear it and select all its notes in the edited track (Shift adds)";
            pianoKeys.InvalidateVisual();
            fingerOverlay.InvalidateVisual();
            UpdatePlayhead(_player.PositionTicks);
        }
    }
}
