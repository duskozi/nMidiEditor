using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using NinePatchWpf;

namespace nMIDI.Controls.Primitives;

/// <summary>
/// Many nine-patches drawn into one bitmap with a single native call (<see cref="NinePatchCanvas"/>), for
/// controls that draw lots of small equal parts: step cells, activity LEDs, LED meter segments, notes.
/// <para>Usage per frame: <see cref="Begin"/>, any number of <see cref="Add(NinePatchImage, Rect)"/>,
/// then <see cref="Render"/>. The first <see cref="Render"/> after a size change must happen inside OnRender
/// (it draws the bitmap); after that the bitmap can be refreshed from a timer with
/// <see cref="Render"/>(null) without invalidating the element, which skips WPF layout and render passes.</para>
/// </summary>
internal sealed class NineBatch : IDisposable
{
    private readonly NinePatchCanvas _canvas = new() { Scaling = NinePatchScaling.Bilinear };
    private readonly List<NinePatchElement> _items = new(256);
    private readonly List<(FrameSpec Spec, Rect Rect)> _fallback = new();   // drawn as vectors without the native renderer
    private double _scaleX = 1, _scaleY = 1;
    private Size _size;

    /// <summary>Largest width or height of the batch bitmap, in device pixels.</summary>
    public const int MaxPixels = 8192;

    /// <summary>Items in the batch being built.</summary>
    public int Count => _items.Count;

    /// <summary>Whether the bitmap still has the size of the last <see cref="Begin"/> drawn by OnRender.</summary>
    public bool IsAttached { get; private set; }

    /// <summary>Start a batch covering <paramref name="size"/> DIPs of <paramref name="owner"/>.</summary>
    public void Begin(Visual owner, Size size)
    {
        var dpi = NinePainter.Dpi(owner);
        _scaleX = dpi.DpiScaleX;
        _scaleY = dpi.DpiScaleY;
        // Cap the bitmap (a 16k × 16k batch would be 1 GB); parts beyond it are not drawn.
        int pw = Math.Clamp((int)Math.Ceiling(size.Width * _scaleX - 0.01), 1, MaxPixels);
        int ph = Math.Clamp((int)Math.Ceiling(size.Height * _scaleY - 0.01), 1, MaxPixels);
        if (pw != _canvas.Width || ph != _canvas.Height || size != _size) IsAttached = false;
        _size = size;
        _items.Clear();
        _fallback.Clear();
        if (!NineSkins.IsNativeAvailable) return;
        try { _canvas.Resize(pw, ph, dpi.PixelsPerInchX, dpi.PixelsPerInchY); }
        catch (Exception ex) when (NineSkins.IsNativeFailure(ex)) { NineSkins.MarkUnavailable(owner, ex); }
    }

    /// <summary>
    /// Add one part; <paramref name="rect"/> is in DIPs relative to the batch origin, snapped to device pixels.
    /// When <paramref name="image"/> is null (no native renderer), <paramref name="fallback"/> is drawn instead.
    /// </summary>
    public void Add(NinePatchImage? image, Rect rect, in FrameSpec fallback)
    {
        if (image is null)
        {
            if (rect.Width > 0 && rect.Height > 0) _fallback.Add((fallback, rect));
            return;
        }
        Add(image, rect);
    }

    /// <summary>Add one part; <paramref name="rect"/> is in DIPs relative to the batch origin, snapped to device pixels.</summary>
    public void Add(NinePatchImage? image, Rect rect)
    {
        if (image is null || rect.Width <= 0 || rect.Height <= 0) return;
        int x0 = (int)Math.Round(rect.X * _scaleX), y0 = (int)Math.Round(rect.Y * _scaleY);
        int x1 = (int)Math.Round(rect.Right * _scaleX), y1 = (int)Math.Round(rect.Bottom * _scaleY);
        if (x1 <= x0 || y1 <= y0) return;
        _items.Add(new NinePatchElement { Source = image, X = x0, Y = y0, Width = x1 - x0, Height = y1 - y0 });
    }

    /// <summary>Add a generated frame.</summary>
    public void Add(in FrameSpec spec, Rect rect)
    {
        var fitted = spec;
        int minPixels = (int)Math.Floor(Math.Min(rect.Width * _scaleX, rect.Height * _scaleY));
        NineSkins.TryFit(ref fitted, minPixels);   // draws even when it does not fully fit: tiny parts barely show
        Add(NineSkins.TryGet(fitted), rect, fitted);
    }

    /// <summary>
    /// Render the batch. With a <paramref name="dc"/> (inside OnRender) the bitmap is also drawn at
    /// <paramref name="origin"/>; with null it is only refreshed in place.
    /// </summary>
    public void Render(DrawingContext? dc, Point origin = default)
    {
        if (_fallback.Count > 0 || !NineSkins.IsNativeAvailable)
        {
            // No native renderer: draw the parts as vectors (this needs OnRender).
            if (dc is null) return;
            dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
            foreach (var (spec, rect) in _fallback) NinePainter.DrawVector(dc, spec, rect);
            dc.Pop();
            IsAttached = false;
            return;
        }
        try
        {
            _canvas.RenderBatch(_items);
        }
        catch (Exception ex) when (NineSkins.IsNativeFailure(ex))
        {
            if (ex is NinePatchException) NinePatchDiagnostics.Report(this, "nMIDI.Controls: cannot render a nine-patch batch.", ex);
            else NineSkins.MarkUnavailable(this, ex);
            return;
        }
        if (dc is not null && _canvas.Bitmap is { } bmp)
        {
            dc.DrawImage(bmp, new Rect(origin, _size));
            IsAttached = true;
        }
    }

    public void Dispose() => _canvas.Dispose();
}
