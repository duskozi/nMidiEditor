using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using nMIDI.Theory;

namespace nMIDI.Piano
{
    /// <summary>A pressed (or pedal-sustained) key. Hand: 0 none, 1 left, 2 right. Finger 1–5 (0 = unknown).</summary>
    public readonly record struct KeyHighlight(int Note, Color Color, double Intensity, int Hand = 0, int Finger = 0);

    /// <summary>What is written on pressed keys.</summary>
    public enum KeyLabelMode { Fingers, Notes, None }

    public static class HandColors
    {
        public static readonly Color Left = Palette.C("#3B82F6");
        public static readonly Color Right = Palette.C("#F97316");
        public static Color Of(int hand) => hand == 1 ? Left : Right;
    }

    /// <summary>
    /// The big piano at the bottom of the window, drawn by hand with a DrawingContext: ivory keys
    /// with a front lip, glossy black keys, a red felt strip, and pressed keys lit in their track's
    /// colour with the finger number or note name on them. Click or drag over the keys to hear them.
    /// </summary>
    public sealed class PianoKeyboard : FrameworkElement
    {
        private readonly Dictionary<int, KeyHighlight> _pressed = new();
        private int _low = 21, _high = 108;
        private int _mouseNote = -1;

        public event Action<int>? NoteDown;
        public event Action<int>? NoteUp;

        private KeyLabelMode _labelMode = KeyLabelMode.Fingers;
        public KeyLabelMode LabelMode
        {
            get => _labelMode;
            set { if (_labelMode != value) { _labelMode = value; InvalidateVisual(); } }
        }

        public KeyboardLayout Layout { get; private set; } = new(21, 108, 100);

        public PianoKeyboard()
        {
            SnapsToDevicePixels = true;
            ClipToBounds = true;
        }

        public void SetRange(int low, int high)
        {
            if (low == _low && high == _high) return;
            _low = low;
            _high = high;
            InvalidateVisual();
        }

        public void SetState(IEnumerable<KeyHighlight> pressed)
        {
            _pressed.Clear();
            foreach (var k in pressed)
            {
                if (_pressed.TryGetValue(k.Note, out var ex) && ex.Intensity >= k.Intensity) continue;
                _pressed[k.Note] = k;
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 10 || h < 10) return;
            Layout = new KeyboardLayout(_low, _high, w);
            var L = Layout;

            // Case + felt strip
            dc.DrawRectangle(Kit.Linear(Palette.C("#1A1D24"), Palette.C("#0B0C10")), null, new Rect(0, 0, w, h));
            double felt = Math.Max(4, h * 0.035);
            double top = felt;
            double whiteH = h - top - 2;
            double blackH = whiteH * 0.63;

            // White keys
            for (int n = L.Low; n <= L.High; n++)
            {
                if (NoteHelper.IsBlackKey(n)) continue;
                double x = L.X(n), kw = L.W(n);
                bool down = _pressed.TryGetValue(n, out var hl);
                double lip = down ? whiteH * 0.025 : whiteH * 0.055;
                var body = new Rect(x + 0.6, top, kw - 1.2, whiteH - lip);
                var front = new Rect(x + 0.6, top + whiteH - lip - 3, kw - 1.2, lip + 3);

                if (down)
                {
                    var c = Palette.Lerp(Colors.White, hl.Color, 0.35 + 0.65 * hl.Intensity);   // full intensity = exactly the track colour
                    dc.DrawRoundedRectangle(Kit.Linear(false, (Palette.Darken(c, 0.05), 0), (Palette.Lighten(c, 0.25), 0.5), (Palette.Darken(c, 0.05), 1)),
                                            null, front, 3, 3);
                    var bodyFill = Kit.Linear(true, (Palette.Darken(c, 0.35), 0), (Palette.Darken(c, 0.05), 0.08), (c, 0.55), (Palette.Lighten(c, 0.2), 1));
                    dc.DrawRoundedRectangle(bodyFill, Kit.Pen(Palette.Darken(c, 0.4), 0.8), body, 2.5, 2.5);
                    // Inner glow
                    dc.DrawRectangle(Kit.Linear(hl.Color.WithAlpha(0.0), hl.Color.WithAlpha(0.35)), null,
                                     new Rect(body.X + 2, body.Bottom - body.Height * 0.4, body.Width - 4, body.Height * 0.4 - 2));
                }
                else
                {
                    dc.DrawRoundedRectangle(Kit.Linear(false, (Palette.C("#BDBDB6"), 0), (Palette.C("#E2E1DA"), 0.5), (Palette.C("#BDBDB6"), 1)),
                                            null, front, 3, 3);
                    var ivory = Kit.Linear(true,
                        (Palette.C("#CFCFC8"), 0), (Palette.C("#FBFBF7"), 0.06), (Palette.C("#F4F3EE"), 0.7), (Palette.C("#E6E5DE"), 1));
                    dc.DrawRoundedRectangle(ivory, Kit.Pen(Palette.C("#9C9C96"), 0.7), body, 2.5, 2.5);
                }

                // Labels
                int pc = NoteHelper.PC(n);
                if (down && kw > 11 && LabelMode != KeyLabelMode.None)
                {
                    DrawLabel(dc, n, hl, x + kw / 2, body.Bottom, kw, black: false);
                }
                else if (pc == 0 && kw > 9)
                {
                    var t = Kit.Text($"C{NoteHelper.Octave(n)}", Math.Min(11, kw * 0.42), Palette.C("#8D8D86"), this);
                    Kit.DrawCentered(dc, t, new Point(x + kw / 2, body.Bottom - t.Height * 0.9));
                }
            }

            // Shadow cast by the felt / case onto the keys.
            dc.DrawRectangle(Kit.Linear(Colors.Black.WithAlpha(0.45), Colors.Black.WithAlpha(0)), null, new Rect(0, top, w, whiteH * 0.06));

            // Black keys
            for (int n = L.Low; n <= L.High; n++)
            {
                if (!NoteHelper.IsBlackKey(n)) continue;
                double x = L.X(n), kw = L.W(n);
                bool down = _pressed.TryGetValue(n, out var hl);
                double kh = down ? blackH * 0.985 : blackH;
                var shadow = new Rect(x + 1.5, top, kw, kh + 4);
                dc.DrawRoundedRectangle(Kit.Solid(Colors.Black.WithAlpha(0.35)), null, shadow, 3, 3);

                var body = new Rect(x, top, kw, kh);
                if (down)
                {
                    var c = Palette.Lerp(Palette.C("#202020"), hl.Color, 0.45 + 0.55 * hl.Intensity);
                    dc.DrawRoundedRectangle(Kit.Linear(true, (Palette.Darken(c, 0.55), 0), (Palette.Darken(c, 0.15), 0.6), (c, 1)),
                                            Kit.Pen(Palette.Darken(c, 0.6), 0.8), body, 2.5, 2.5);
                    var cap = new Rect(x + kw * 0.14, top + 2, kw * 0.72, kh - kh * 0.1 - 2);
                    dc.DrawRoundedRectangle(Kit.Linear(true, (Palette.Darken(c, 0.25), 0), (Palette.Lighten(c, 0.15), 1)), null, cap, 2, 2);
                    if (kw > 8 && LabelMode != KeyLabelMode.None)
                        DrawLabel(dc, n, hl, x + kw / 2, top + kh, kw, black: true);
                }
                else
                {
                    dc.DrawRoundedRectangle(Kit.Linear(true, (Palette.C("#0C0C0C"), 0), (Palette.C("#1C1C1C"), 0.85), (Palette.C("#3A3A3A"), 1)),
                                            Kit.Pen(Palette.C("#000000"), 0.8), body, 2.5, 2.5);
                    // Glossy cap with bevel
                    var cap = new Rect(x + kw * 0.14, top + 2, kw * 0.72, kh - kh * 0.11 - 2);
                    var capFill = Kit.Linear(true, (Palette.C("#151515"), 0), (Palette.C("#2C2C2C"), 0.75), (Palette.C("#4A4A4A"), 1));
                    dc.DrawRoundedRectangle(capFill, null, cap, 2, 2);
                    dc.DrawRectangle(Kit.Linear(Colors.White.WithAlpha(0.18), Colors.White.WithAlpha(0)), null,
                                     new Rect(cap.X + 1, cap.Y + cap.Height * 0.08, cap.Width * 0.3, cap.Height * 0.8));
                }
            }

            // Felt strip on top of everything
            var feltFill = Kit.Linear(true, (Palette.C("#5A0C18"), 0), (Palette.C("#8E1A2A"), 0.5), (Palette.C("#3C0710"), 1));
            dc.DrawRectangle(feltFill, null, new Rect(0, 0, w, felt));
            dc.DrawRectangle(Kit.Solid(Colors.Black.WithAlpha(0.5)), null, new Rect(0, felt - 1, w, 1));
        }

        private void DrawLabel(DrawingContext dc, int n, KeyHighlight hl, double cx, double bottom, double kw, bool black)
        {
            if (hl.Intensity < 0.2) return;   // very soft notes stay unlabelled
            switch (LabelMode)
            {
                case KeyLabelMode.Fingers when hl.Finger > 0:
                {
                    // Finger number in a badge of the hand colour, with L / R above it.
                    var hc = HandColors.Of(hl.Hand);
                    double r = Math.Min(black ? 9 : 11, kw * (black ? 0.46 : 0.42));
                    var cpt = new Point(cx, bottom - r - (black ? 6 : 8));
                    dc.DrawEllipse(Kit.Glow(hc.WithAlpha(0.6)), null, cpt, r * 1.8, r * 1.8);
                    dc.DrawEllipse(Kit.Sphere(Palette.Lighten(hc, 0.45), hc), Kit.Pen(Colors.White, 1.4), cpt, r, r);
                    Kit.DrawCentered(dc, Kit.Text(hl.Finger.ToString(), r * 1.2, Colors.White, this, bold: true), cpt);
                    var hand = Kit.Text(hl.Hand == 1 ? "L" : "R", Math.Max(8, r * 0.8), black ? Colors.White : Palette.Darken(hc, 0.4), this, bold: true);
                    Kit.DrawCentered(dc, hand, new Point(cx, cpt.Y - r - hand.Height * 0.55));
                    break;
                }
                case KeyLabelMode.Notes:
                {
                    var t = Kit.Text(NoteHelper.Pretty(n), Math.Min(13, kw * 0.48), black ? Colors.White : Palette.Darken(hl.Color, 0.7), this, bold: true);
                    Kit.DrawCentered(dc, t, new Point(cx, bottom - t.Height * 0.9));
                    break;
                }
            }
        }

        // Mouse audition

        private int HitTestNote(Point p)
        {
            var L = Layout;
            double felt = Math.Max(4, ActualHeight * 0.035);
            double blackBottom = felt + (ActualHeight - felt - 2) * 0.63;
            if (p.Y <= blackBottom)
            {
                for (int n = L.Low; n <= L.High; n++)
                    if (NoteHelper.IsBlackKey(n) && p.X >= L.X(n) && p.X <= L.X(n) + L.W(n)) return n;
            }
            for (int n = L.Low; n <= L.High; n++)
                if (!NoteHelper.IsBlackKey(n) && p.X >= L.X(n) && p.X < L.X(n) + L.W(n)) return n;
            return -1;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            CaptureMouse();
            _mouseNote = HitTestNote(e.GetPosition(this));
            if (_mouseNote >= 0) NoteDown?.Invoke(_mouseNote);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!IsMouseCaptured) return;
            int n = HitTestNote(e.GetPosition(this));
            if (n == _mouseNote) return;
            if (_mouseNote >= 0) NoteUp?.Invoke(_mouseNote);
            _mouseNote = n;
            if (n >= 0) NoteDown?.Invoke(n);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (_mouseNote >= 0) NoteUp?.Invoke(_mouseNote);
            _mouseNote = -1;
            ReleaseMouseCapture();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            if (_mouseNote >= 0) NoteUp?.Invoke(_mouseNote);
            _mouseNote = -1;
        }
    }
}
