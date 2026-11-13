using System;

namespace nMIDI.Lanes
{
    /// <summary>
    /// The horizontal view shared by every lane, the ruler and the scroll bar of the track lanes
    /// window: which ticks are visible and how wide the lane area is.
    /// </summary>
    public sealed class TimeAxis
    {
        private double _start;
        private double _length = 1;

        /// <summary>Raised after the visible range or the width changes.</summary>
        public event EventHandler? Changed;

        /// <summary>Length of the song in ticks, plus a little room after the end.</summary>
        public double ContentTicks { get; private set; } = 1;

        public long TicksPerBeat { get; private set; } = 480;
        public int BeatsPerBar { get; private set; } = 4;

        /// <summary>First visible tick.</summary>
        public double Start => _start;

        /// <summary>Number of visible ticks.</summary>
        public double Length => _length;

        public double End => _start + _length;

        /// <summary>Width of the lane area in DIPs.</summary>
        public double Width { get; private set; } = 1;

        /// <summary>The closest zoom: one beat across the whole view.</summary>
        public double MinLength => Math.Min(ContentTicks, TicksPerBeat);

        public double PixelsPerTick => Width / _length;

        public void SetSong(double contentTicks, long ticksPerBeat, int beatsPerBar)
        {
            ContentTicks = Math.Max(1, contentTicks);
            TicksPerBeat = Math.Max(1, ticksPerBeat);
            BeatsPerBar = Math.Max(1, beatsPerBar);
            _start = 0;
            _length = ContentTicks;
            Changed?.Invoke(this, EventArgs.Empty);   // always: the beat grid may differ even if the length does not
        }

        /// <summary>Change the song length (after an edit) without moving the view.</summary>
        public void SetContent(double contentTicks)
        {
            contentTicks = Math.Max(1, contentTicks);
            if (contentTicks == ContentTicks) return;
            ContentTicks = contentTicks;
            _length = Math.Clamp(_length, MinLength, ContentTicks);
            _start = Math.Clamp(_start, 0, ContentTicks - _length);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void SetWidth(double width)
        {
            if (width <= 0 || width == Width) return;
            Width = width;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Show this range of ticks, kept inside the song.</summary>
        public void SetRange(double start, double length)
        {
            length = Math.Clamp(length, MinLength, ContentTicks);
            start = Math.Clamp(start, 0, ContentTicks - length);
            if (start == _start && length == _length) return;
            _start = start;
            _length = length;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Zoom by a factor (above 1 = closer), keeping the tick under <paramref name="x"/> in place.</summary>
        public void ZoomAt(double factor, double x)
        {
            double tick = XToTick(x);
            double length = Math.Clamp(_length / factor, MinLength, ContentTicks);
            SetRange(tick - x / Width * length, length);
        }

        /// <summary>Scroll so that <paramref name="tick"/> sits at this fraction of the width (0 = left edge).</summary>
        public void ScrollTo(double tick, double fraction) => SetRange(tick - _length * fraction, _length);

        public double TickToX(double tick) => (tick - _start) * PixelsPerTick;

        public double XToTick(double x) => _start + x / PixelsPerTick;
    }
}
