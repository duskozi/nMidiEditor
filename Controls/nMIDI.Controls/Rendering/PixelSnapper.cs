using System;
using System.Windows;
using System.Windows.Media;

namespace nMIDI.Controls.Primitives;


/// <summary>Device-pixel snapping for crisp OnRender drawing at any DPI.</summary>
internal readonly struct PixelSnapper
{
    private readonly double _scaleX;
    private readonly double _scaleY;

    public PixelSnapper(Visual visual)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(visual);
        _scaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
        _scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;
        PixelsPerDip = dpi.PixelsPerDip;
    }

    public double PixelsPerDip { get; }

    public double X(double x) => Math.Round(x * _scaleX) / _scaleX;

    public double Y(double y) => Math.Round(y * _scaleY) / _scaleY;

    public Rect Rect(double x, double y, double w, double h)
    {
        double l = X(x), t = Y(y), r = X(x + w), b = Y(y + h);
        return new Rect(l, t, Math.Max(0, r - l), Math.Max(0, b - t));
    }

    /// <summary>Half-pixel offset so a 1-device-pixel line lands exactly on a pixel row/column.</summary>
    public double LineX(double x) => (Math.Floor(x * _scaleX) + 0.5) / _scaleX;

    public double LineY(double y) => (Math.Floor(y * _scaleY) + 0.5) / _scaleY;

    public double OnePixel => 1.0 / _scaleX;
}

internal static class FrozenPens
{
    public static Pen? Create(Brush? brush, double thickness)
    {
        if (brush is null || thickness <= 0) return null;
        var pen = new Pen(brush, thickness);
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }
}
