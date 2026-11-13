using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace nMIDI.Piano
{
    /// <summary>
    /// Pitch bend and modulation wheels next to the keyboard, drawn like a keyboard's hardware wheels. They show
    /// the edited track's pitch bend and modulation (CC 1) at the playhead, or what comes in from the MIDI input.
    /// Drag a wheel up or down to send it; the pitch wheel springs back to the centre when released.
    /// </summary>
    public sealed class WheelPanel : FrameworkElement
    {
        public const int BendCentre = 8192;

        private int _bend = BendCentre, _mod;
        private int _dragging = -1;          // 0 = pitch, 1 = mod
        private double _dragStartY;
        private int _dragStartValue;

        /// <summary>The user moved the pitch wheel (0–16383, 8192 = centre).</summary>
        public event Action<int>? BendChanged;
        /// <summary>The user moved the modulation wheel (0–127).</summary>
        public event Action<int>? ModChanged;

        public bool IsDragging => _dragging >= 0;

        public int Bend
        {
            get => _bend;
            set { value = Math.Clamp(value, 0, 16383); if (value != _bend && _dragging != 0) { _bend = value; InvalidateVisual(); } }
        }

        public int Mod
        {
            get => _mod;
            set { value = Math.Clamp(value, 0, 127); if (value != _mod && _dragging != 1) { _mod = value; InvalidateVisual(); } }
        }

        public WheelPanel()
        {
            Width = 78;
            SnapsToDevicePixels = true;
            ClipToBounds = true;
            Cursor = Cursors.SizeNS;
            ToolTip = "Pitch bend and modulation of the edited track at the playhead (or from the MIDI input).\n"
                    + "Drag a wheel to send it; the pitch wheel springs back when released. Double-click to reset.";
        }

        private Rect WheelRect(int index)
        {
            double h = ActualHeight;
            double slotW = 22, gap = (ActualWidth - 2 * slotW) / 3;
            double top = Math.Max(6, h * 0.06), bottom = h - 18;
            return new Rect(gap + index * (slotW + gap), top, slotW, Math.Max(10, bottom - top));
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 10 || h < 30) return;
            dc.DrawRectangle(Kit.Linear(Palette.C("#1A1D24"), Palette.C("#0B0C10")), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(Kit.Solid(Palette.C("#000000")), null, new Rect(w - 1, 0, 1, h));
            string bendText = _bend == BendCentre ? "0" : ((_bend - BendCentre) / 81.92).ToString("+0;-0", CultureInfo.InvariantCulture);
            DrawWheel(dc, WheelRect(0), (_bend - BendCentre) / 8192.0, centred: true, "PITCH", bendText);
            DrawWheel(dc, WheelRect(1), _mod / 127.0 * 2 - 1, centred: false, "MOD", _mod.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>One wheel. <paramref name="position"/> is −1 (bottom) … +1 (top).</summary>
        private void DrawWheel(DrawingContext dc, Rect slot, double position, bool centred, string label, string value)
        {
            position = Math.Clamp(position, -1, 1);
            // Slot in the case.
            var outer = slot; outer.Inflate(3, 3);
            dc.DrawRoundedRectangle(Kit.Solid(Palette.C("#050608")), Kit.Pen(Palette.C("#2A2E38"), 1), outer, 4, 4);

            // The wheel: a cylinder seen from the front, with ribs that move as it turns.
            dc.PushClip(new RectangleGeometry(slot, 3, 3));
            var drum = Kit.Linear(true, (Palette.C("#0A0B0E"), 0), (Palette.C("#2C3039"), 0.25), (Palette.C("#3A3F4A"), 0.5),
                                        (Palette.C("#2C3039"), 0.75), (Palette.C("#0A0B0E"), 1));
            dc.DrawRectangle(drum, null, slot);
            double mid = slot.Top + slot.Height / 2;
            double travel = slot.Height / 2 - 6;
            double markY = mid - position * travel;
            var rib = Kit.Pen(Palette.C("#1E2128"), 1);
            double phase = (markY - slot.Top) % 6;
            for (double y = slot.Top + phase; y < slot.Bottom; y += 6)
            {
                // Ribs bunch up towards the top and bottom edge, as on a real wheel.
                double t = (y - mid) / (slot.Height / 2);
                double yy = mid + Math.Sin(Math.Clamp(t, -1, 1) * Math.PI / 2) * slot.Height / 2;
                dc.DrawLine(rib, new Point(slot.Left + 2, yy), new Point(slot.Right - 2, yy));
            }
            // Position mark.
            var markColor = centred && Math.Abs(position) < 0.002 ? Palette.C("#9AA3B2") : Palette.C("#FF8A1F");
            dc.DrawRectangle(Kit.Solid(markColor), null, new Rect(slot.Left + 1, markY - 2, slot.Width - 2, 4));
            dc.Pop();
            if (centred)
            {
                var tick = Kit.Pen(Palette.C("#6B7280"), 1);
                dc.DrawLine(tick, new Point(slot.Left - 6, mid), new Point(slot.Left - 3, mid));
                dc.DrawLine(tick, new Point(slot.Right + 3, mid), new Point(slot.Right + 6, mid));
            }

            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            var l = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 8.5, Kit.Solid(Palette.C("#8D919A")), ppd);
            dc.DrawText(l, new Point(slot.Left + (slot.Width - l.Width) / 2, slot.Bottom + 4));
            if (slot.Height > 50)
            {
                var v = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 8.5, Kit.Solid(Palette.C("#C8CCD4")), ppd);
                double vy = position > 0 ? slot.Bottom - v.Height - 2 : slot.Top + 2;
                dc.DrawText(v, new Point(slot.Left + (slot.Width - v.Width) / 2, vy));
            }
        }

        private int HitWheel(Point p)
        {
            for (int i = 0; i < 2; i++)
            {
                var r = WheelRect(i);
                r.Inflate(8, 8);
                if (r.Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var p = e.GetPosition(this);
            int wheel = HitWheel(p);
            if (wheel < 0) return;
            e.Handled = true;
            if (e.ClickCount == 2)
            {
                if (wheel == 0) SetBend(BendCentre); else SetMod(0);
                return;
            }
            _dragging = wheel;
            _dragStartY = p.Y;
            _dragStartValue = wheel == 0 ? _bend : _mod;
            CaptureMouse();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging < 0 || !IsMouseCaptured) return;
            var r = WheelRect(_dragging);
            double travel = Math.Max(10, r.Height - 12);
            double delta = (_dragStartY - e.GetPosition(this).Y) / travel;   // a full slot height = the whole range
            if (_dragging == 0) SetBend((int)Math.Round(_dragStartValue + delta * 16383));
            else SetMod((int)Math.Round(_dragStartValue + delta * 127));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_dragging < 0) return;
            int wheel = _dragging;
            _dragging = -1;
            if (IsMouseCaptured) ReleaseMouseCapture();
            if (wheel == 0) SetBend(BendCentre);   // the pitch wheel springs back
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (_dragging == 0) { _dragging = -1; SetBend(BendCentre); }
            _dragging = -1;
        }

        private void SetBend(int value)
        {
            value = Math.Clamp(value, 0, 16383);
            if (value == _bend) return;
            _bend = value;
            InvalidateVisual();
            BendChanged?.Invoke(value);
        }

        private void SetMod(int value)
        {
            value = Math.Clamp(value, 0, 127);
            if (value == _mod) return;
            _mod = value;
            InvalidateVisual();
            ModChanged?.Invoke(value);
        }
    }
}
