using System.Windows;

namespace nMIDI.Controls.Skinning;

/// <summary>Keys for skinned styles of standard WPF controls, usable in your own UI.</summary>
public static class DawStyleKeys
{
    private static ComponentResourceKey K(string id) => new(typeof(DawStyleKeys), id);

    /// <summary>Compact skinned ComboBox (used by <see cref="Controls.TrackHeader"/> for the device selector).</summary>
    public static ComponentResourceKey ComboBox { get; } = K(nameof(ComboBox));

    /// <summary>Focus rectangle in the skin's focus colour, shown when a control is focused from the keyboard.</summary>
    public static ComponentResourceKey FocusVisual { get; } = K(nameof(FocusVisual));

    /// <summary>Small flat icon button (used for the hide button of <see cref="Controls.DawPanel"/>).</summary>
    public static ComponentResourceKey IconButton { get; } = K(nameof(IconButton));
}
