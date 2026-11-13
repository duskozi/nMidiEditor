using System.Windows;

namespace nMIDI.Controls.Skinning;

/// <summary>
/// Geometry keys for transport icons (24×24 design box). Defaults live in the theme; override any of them
/// in your skin or App resources to restyle the icons.
/// </summary>
public static class DawIconKeys
{
    private static ComponentResourceKey K(string id) => new(typeof(DawIconKeys), id);

    /// <summary>Key of the play icon (a Geometry).</summary>
    public static ComponentResourceKey Play { get; } = K(nameof(Play));
    /// <summary>Key of the pause icon (a Geometry).</summary>
    public static ComponentResourceKey Pause { get; } = K(nameof(Pause));
    /// <summary>Key of the stop icon (a Geometry).</summary>
    public static ComponentResourceKey Stop { get; } = K(nameof(Stop));
    /// <summary>Key of the record icon (a Geometry).</summary>
    public static ComponentResourceKey Record { get; } = K(nameof(Record));
    /// <summary>Key of the rewind icon (a Geometry).</summary>
    public static ComponentResourceKey Rewind { get; } = K(nameof(Rewind));
    /// <summary>Key of the fast-forward icon (a Geometry).</summary>
    public static ComponentResourceKey FastForward { get; } = K(nameof(FastForward));
    /// <summary>Key of the go-to-start icon (a Geometry).</summary>
    public static ComponentResourceKey ToStart { get; } = K(nameof(ToStart));
    /// <summary>Key of the go-to-end icon (a Geometry).</summary>
    public static ComponentResourceKey ToEnd { get; } = K(nameof(ToEnd));
    /// <summary>Key of the loop icon (a Geometry).</summary>
    public static ComponentResourceKey Loop { get; } = K(nameof(Loop));
    /// <summary>Key of the metronome icon (a Geometry).</summary>
    public static ComponentResourceKey Metronome { get; } = K(nameof(Metronome));
    /// <summary>Key of the eye icon (a Geometry).</summary>
    public static ComponentResourceKey Eye { get; } = K(nameof(Eye));
    /// <summary>Key of the close icon (a Geometry).</summary>
    public static ComponentResourceKey Close { get; } = K(nameof(Close));
    /// <summary>Key of the open icon (a Geometry).</summary>
    public static ComponentResourceKey Open { get; } = K(nameof(Open));
    /// <summary>Key of the chevron pointing down icon (a Geometry).</summary>
    public static ComponentResourceKey ChevronDown { get; } = K(nameof(ChevronDown));
}
