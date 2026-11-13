using NinePatchWpf;
using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace nMIDI.Controls.Primitives;

/// <summary>
/// Nine-patch note skins built in memory, one per colour: a dark 1-pixel outline with rounded
/// corners, a light top edge and a shaded bottom edge. Only the middle stretches, so the edges
/// stay crisp at any zoom.
/// </summary>
internal sealed class NoteSkins : IDisposable
{
    private readonly Dictionary<(Color, bool), NinePatchImage> _cache = new();

    /// <summary>Track colours, chosen to stay distinct on a dark background.</summary>
    public static readonly Color[] TrackColors =
    [
        Color.FromRgb(0x4C, 0x9A, 0xFF),   // blue
        Color.FromRgb(0xFF, 0x8A, 0x3D),   // orange
        Color.FromRgb(0x3D, 0xD6, 0x8C),   // green
        Color.FromRgb(0xF2, 0x4E, 0x6E),   // red
        Color.FromRgb(0xB0, 0x7C, 0xFF),   // violet
        Color.FromRgb(0x2E, 0xD3, 0xE0),   // cyan
        Color.FromRgb(0xF5, 0xD0, 0x3B),   // yellow
        Color.FromRgb(0xFF, 0x7E, 0xC8),   // pink
        Color.FromRgb(0x9B, 0xD8, 0x4A),   // lime
        Color.FromRgb(0x6F, 0x8B, 0xFF),   // indigo
        Color.FromRgb(0xFF, 0xB0, 0x6A),   // apricot
        Color.FromRgb(0x5E, 0xC8, 0xB8),   // teal
    ];

    /// <summary>The skin of a note in this colour; a selected note is lighter, with a white outline.</summary>
    public NinePatchImage Get(Color color, bool selected = false)
    {
        if (!_cache.TryGetValue((color, selected), out var image))
            _cache[(color, selected)] = image = Create(color, selected);
        return image;
    }

    /// <summary>Like <see cref="Get"/>, but null when the native renderer cannot be used.</summary>
    public NinePatchImage? TryGet(Color color, bool selected = false)
    {
        if (!NineSkins.IsNativeAvailable) return null;
        try { return Get(color, selected); }
        catch (Exception ex) when (NineSkins.IsNativeFailure(ex))
        {
            NineSkins.MarkUnavailable(this, ex);
            return null;
        }
    }

    /// <summary>Colour of a muted, or not soloed, track: grey with a hint of the track colour.</summary>
    public static Color Dimmed(Color c)
    {
        byte grey = (byte)((c.R * 30 + c.G * 59 + c.B * 11) / 100);
        return Color.FromArgb(110, (byte)((grey * 3 + c.R) / 4 * 0.6), (byte)((grey * 3 + c.G) / 4 * 0.6), (byte)((grey * 3 + c.B) / 4 * 0.6));
    }

    /// <summary>Colour of the tracks that are not being edited: the same hue, mostly transparent.</summary>
    public static Color Faded(Color c) => Color.FromArgb((byte)(c.A * 0.38), c.R, c.G, c.B);

    /// <summary>Track colour scaled by velocity (soft notes darker), in 16 steps to keep the cache small.</summary>
    public static Color ByVelocity(Color c, int velocity)
    {
        double level = 0.35 + 0.65 * (Math.Clamp(velocity, 1, 127) / 8 * 8) / 127.0;
        return Color.FromArgb(c.A, (byte)(c.R * level), (byte)(c.G * level), (byte)(c.B * level));
    }

    private static NinePatchImage Create(Color color, bool selected)
    {
        if (selected)
            color = Color.FromArgb(255, Channel(color.R, 1.45), Channel(color.G, 1.45), Channel(color.B, 1.45));
        const int W = 16, H = 12;
        byte[] px = new byte[W * H * 4];

        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                bool edgeX = x == 0 || x == W - 1, edgeY = y == 0 || y == H - 1;
                double shade;
                double alpha = color.A / 255.0;

                if (edgeX && edgeY) alpha = 0;                    // rounded corner
                if (edgeX || edgeY) shade = 0.45;                 // outline
                else if (y == 1) shade = 1.35;                    // highlight
                else if (y == 2) shade = 1.12;
                else if (y == H - 2) shade = 0.78;                // bottom shade
                else shade = 1.0;

                // NinePatchImage takes straight (not premultiplied) RGBA.
                int i = (y * W + x) * 4;
                bool outline = (edgeX || edgeY) && !(edgeX && edgeY);
                if (selected && outline)
                {
                    px[i] = px[i + 1] = px[i + 2] = 255;   // white outline marks the selection
                    px[i + 3] = 255;
                    continue;
                }
                px[i] = Channel(color.R, shade);
                px[i + 1] = Channel(color.G, shade);
                px[i + 2] = Channel(color.B, shade);
                px[i + 3] = (byte)(alpha * 255);
            }
        }

        return new NinePatchImage(px, W, H, new NinePatchInfo
        {
            StretchLeft = 3,
            StretchRight = W - 3,
            StretchTop = 3,
            StretchBottom = H - 3,
        });
    }

    private static byte Channel(byte value, double shade) =>
        (byte)Math.Clamp(shade > 1 ? value + (255 - value) * (shade - 1) : value * shade, 0, 255);

    public void Clear()
    {
        foreach (var image in _cache.Values) image.Dispose();
        _cache.Clear();
    }

    public void Dispose() => Clear();
}
