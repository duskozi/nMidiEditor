using nMIDI.Model;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace nMIDI.Piano
{
    /// <summary>
    /// The three piano pedals next to the keyboard (soft, sostenuto, sustain from left to right, as on a grand).
    /// A pedal goes down and lights up while it is pressed in the song at the playhead, or on the MIDI input.
    /// Hold the mouse on a pedal to press it.
    /// </summary>
    public sealed class PedalPanel : FrameworkElement
    {
        private static readonly Pedal[] Order = { Pedal.Soft, Pedal.Sostenuto, Pedal.Sustain };
        private readonly int[] _values = new int[3];
        private int _pressed = -1;

        /// <summary>The user pressed (127) or released (0) a pedal.</summary>
        public event Action<Pedal, int>? PedalChanged;

        public bool IsPressing => _pressed >= 0;

        public PedalPanel()
        {
            Width = 96;
            SnapsToDevicePixels = true;
            ClipToBounds = true;
            Cursor = Cursors.Hand;
            ToolTip = "Piano pedals of the edited tracks at the playhead (or from the MIDI input): soft, sostenuto and sustain.\n"
                    + "Hold the mouse on a pedal to press it.";
        }

        public int this[Pedal pedal]
        {
            get => _values[Array.IndexOf(Order, pedal)];
            set
            {
                int i = Array.IndexOf(Order, pedal);
                value = Math.Clamp(value, 0, 127);
                if (i == _pressed || _values[i] == value) return;
                _values[i] = value;
                InvalidateVisual();
            }
        }

        private Rect PedalRect(int i)
        {
            double w = ActualWidth, h = ActualHeight;
            double pw = 20, gap = (w - 3 * pw) / 4;
            double top = Math.Max(14, h * 0.28), bottom = h - 18;
            return new Rect(gap + i * (pw + gap), top, pw, Math.Max(12, bottom - top));
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 30 || h < 40) return;
            dc.DrawRectangle(Kit.Linear(Palette.C("#1A1D24"), Palette.C("#0B0C10")), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(Kit.Solid(Colors.Black), null, new Rect(0, 0, 1, h));

            var title = Kit.Text("PEDALS", 8.5, Palette.C("#8D919A"), this, bold: true);
            dc.DrawText(title, new Point((w - title.Width) / 2, 3));

            for (int i = 0; i < Order.Length; i++)
                DrawPedal(dc, PedalRect(i), Order[i], _values[i]);
        }

        private void DrawPedal(DrawingContext dc, Rect slot, Pedal pedal, int value)
        {
            var color = PianoRoll.PedalOverlay.ColorOf(pedal);
            bool down = value >= 64;

            // Lamp above the pedal: lit when down, dim for half-pedal.
            var lamp = down ? color : value > 0 ? color.WithAlpha(0.5) : Palette.C("#2A2E38");
            dc.DrawEllipse(Kit.Solid(lamp), null, new Point(slot.Left + slot.Width / 2, slot.Top - 7), 3, 3);

            // Opening in the case, then the brass foot, which sinks as it is pressed.
            var hole = slot;
            hole.Inflate(2, 2);
            dc.DrawRoundedRectangle(Kit.Solid(Palette.C("#050608")), Kit.Pen(Palette.C("#2A2E38"), 1), hole, 3, 3);

            double drop = value / 127.0 * slot.Height * 0.22;
            var foot = new Rect(slot.Left + 1, slot.Top + drop + 1, slot.Width - 2, slot.Height - drop - 2);
            var top = down ? Palette.Lerp(Palette.C("#E8C87A"), color, 0.45) : Palette.C("#C9B06E");
            var bottom = down ? Palette.Lerp(Palette.C("#7A5E22"), color, 0.35) : Palette.C("#6E582A");
            dc.DrawRoundedRectangle(Kit.Linear(top, bottom), null, foot, 4, 4);
            dc.DrawRoundedRectangle(Kit.Solid(Colors.White.WithAlpha(0.3)), null, new Rect(foot.Left + 2, foot.Top + 2, foot.Width - 4, 2), 1, 1);

            var label = Kit.Text(PedalTimeline.ShortName(pedal), 8, down ? color : Palette.C("#8D919A"), this, bold: true);
            dc.DrawText(label, new Point(slot.Left + (slot.Width - label.Width) / 2, slot.Bottom + 4));
        }

        private int HitPedal(Point p)
        {
            for (int i = 0; i < Order.Length; i++)
            {
                var r = PedalRect(i);
                r.Inflate(6, 10);
                if (r.Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            int i = HitPedal(e.GetPosition(this));
            if (i < 0) return;
            e.Handled = true;
            _pressed = i;
            _values[i] = 127;
            InvalidateVisual();
            CaptureMouse();
            PedalChanged?.Invoke(Order[i], 127);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_pressed < 0) return;
            if (IsMouseCaptured) ReleaseMouseCapture();
            Release();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            Release();
        }

        private void Release()
        {
            if (_pressed < 0) return;
            int i = _pressed;
            _pressed = -1;
            _values[i] = 0;
            InvalidateVisual();
            PedalChanged?.Invoke(Order[i], 0);
        }
    }
}
