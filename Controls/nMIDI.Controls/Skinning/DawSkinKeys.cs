// Resource keys of the skin colours. Every key has a value in Skins/Graphite.xaml.
using System.Windows;

namespace nMIDI.Controls.Skinning;

/// <summary>
/// Resource keys every skin defines. Use them in your own XAML with
/// <c>{DynamicResource {x:Static nc:DawSkinKeys.Accent}}</c> so your UI follows the active skin.
/// </summary>
public static class DawSkinKeys
{
    private static ComponentResourceKey K(string id) => new(typeof(DawSkinKeys), id);

    /// <summary>App/window background.</summary>
    public static ComponentResourceKey WindowBackground { get; } = K(nameof(WindowBackground));

    /// <summary>Panels, racks, strips.</summary>
    public static ComponentResourceKey PanelBackground { get; } = K(nameof(PanelBackground));

    /// <summary>Raised surfaces: buttons, headers.</summary>
    public static ComponentResourceKey PanelRaised { get; } = K(nameof(PanelRaised));

    /// <summary>Hairline borders between surfaces.</summary>
    public static ComponentResourceKey PanelBorder { get; } = K(nameof(PanelBorder));

    /// <summary>Primary text.</summary>
    public static ComponentResourceKey Text { get; } = K(nameof(Text));

    /// <summary>Labels, secondary text.</summary>
    public static ComponentResourceKey TextDim { get; } = K(nameof(TextDim));

    /// <summary>Brand/accent colour: value arcs, selections.</summary>
    public static ComponentResourceKey Accent { get; } = K(nameof(Accent));

    /// <summary>Text drawn on top of Accent.</summary>
    public static ComponentResourceKey AccentForeground { get; } = K(nameof(AccentForeground));

    /// <summary>Keyboard focus ring.</summary>
    public static ComponentResourceKey Focus { get; } = K(nameof(Focus));

    /// <summary>Knob cap fill.</summary>
    public static ComponentResourceKey KnobBody { get; } = K(nameof(KnobBody));

    /// <summary>Knob cap outline.</summary>
    public static ComponentResourceKey KnobBodyBorder { get; } = K(nameof(KnobBodyBorder));

    /// <summary>Unlit part of the value ring.</summary>
    public static ComponentResourceKey KnobTrack { get; } = K(nameof(KnobTrack));

    /// <summary>Pointer line on the cap.</summary>
    public static ComponentResourceKey KnobIndicator { get; } = K(nameof(KnobIndicator));

    /// <summary>Fader slot.</summary>
    public static ComponentResourceKey FaderGroove { get; } = K(nameof(FaderGroove));

    /// <summary>Level fill below the fader cap.</summary>
    public static ComponentResourceKey FaderFill { get; } = K(nameof(FaderFill));

    /// <summary>Fader cap.</summary>
    public static ComponentResourceKey FaderThumb { get; } = K(nameof(FaderThumb));

    /// <summary>Fader cap outline.</summary>
    public static ComponentResourceKey FaderThumbBorder { get; } = K(nameof(FaderThumbBorder));

    /// <summary>Centre line on the fader cap.</summary>
    public static ComponentResourceKey FaderThumbLine { get; } = K(nameof(FaderThumbLine));

    /// <summary>Meter background.</summary>
    public static ComponentResourceKey MeterBackground { get; } = K(nameof(MeterBackground));

    /// <summary>Meter zone below WarningDb.</summary>
    public static ComponentResourceKey MeterSafe { get; } = K(nameof(MeterSafe));

    /// <summary>Meter zone WarningDb..DangerDb.</summary>
    public static ComponentResourceKey MeterWarning { get; } = K(nameof(MeterWarning));

    /// <summary>Meter zone above DangerDb.</summary>
    public static ComponentResourceKey MeterDanger { get; } = K(nameof(MeterDanger));

    /// <summary>Peak-hold line.</summary>
    public static ComponentResourceKey MeterPeakHold { get; } = K(nameof(MeterPeakHold));

    /// <summary>Clip LED lit.</summary>
    public static ComponentResourceKey MeterClipOn { get; } = K(nameof(MeterClipOn));

    /// <summary>Clip LED unlit.</summary>
    public static ComponentResourceKey MeterClipOff { get; } = K(nameof(MeterClipOff));

    /// <summary>Scale tick marks.</summary>
    public static ComponentResourceKey ScaleTick { get; } = K(nameof(ScaleTick));

    /// <summary>Scale labels.</summary>
    public static ComponentResourceKey ScaleText { get; } = K(nameof(ScaleText));

    /// <summary>Button face.</summary>
    public static ComponentResourceKey ButtonBackground { get; } = K(nameof(ButtonBackground));

    /// <summary>Button face on hover.</summary>
    public static ComponentResourceKey ButtonHover { get; } = K(nameof(ButtonHover));

    /// <summary>Button face while pressed.</summary>
    public static ComponentResourceKey ButtonPressed { get; } = K(nameof(ButtonPressed));

    /// <summary>Button outline.</summary>
    public static ComponentResourceKey ButtonBorder { get; } = K(nameof(ButtonBorder));

    /// <summary>Mute lit.</summary>
    public static ComponentResourceKey LedMute { get; } = K(nameof(LedMute));

    /// <summary>Solo lit.</summary>
    public static ComponentResourceKey LedSolo { get; } = K(nameof(LedSolo));

    /// <summary>Record-arm lit.</summary>
    public static ComponentResourceKey LedArm { get; } = K(nameof(LedArm));

    /// <summary>Input monitor lit.</summary>
    public static ComponentResourceKey LedMonitor { get; } = K(nameof(LedMonitor));

    /// <summary>Text on a lit LED button.</summary>
    public static ComponentResourceKey LedLitForeground { get; } = K(nameof(LedLitForeground));

    /// <summary>Play lit.</summary>
    public static ComponentResourceKey TransportPlay { get; } = K(nameof(TransportPlay));

    /// <summary>Record lit.</summary>
    public static ComponentResourceKey TransportRecord { get; } = K(nameof(TransportRecord));

    /// <summary>Time/tempo display background.</summary>
    public static ComponentResourceKey LcdBackground { get; } = K(nameof(LcdBackground));

    /// <summary>Time/tempo display outline.</summary>
    public static ComponentResourceKey LcdBorder { get; } = K(nameof(LcdBorder));

    /// <summary>LCD digits.</summary>
    public static ComponentResourceKey LcdText { get; } = K(nameof(LcdText));

    /// <summary>LCD captions.</summary>
    public static ComponentResourceKey LcdTextDim { get; } = K(nameof(LcdTextDim));

    /// <summary>White piano key.</summary>
    public static ComponentResourceKey KeyWhite { get; } = K(nameof(KeyWhite));

    /// <summary>Black piano key.</summary>
    public static ComponentResourceKey KeyBlack { get; } = K(nameof(KeyBlack));

    /// <summary>Key outlines.</summary>
    public static ComponentResourceKey KeyBorder { get; } = K(nameof(KeyBorder));

    /// <summary>Key held by the user.</summary>
    public static ComponentResourceKey KeyPressed { get; } = K(nameof(KeyPressed));

    /// <summary>Key lit by incoming MIDI / playback.</summary>
    public static ComponentResourceKey KeyHighlight { get; } = K(nameof(KeyHighlight));

    /// <summary>Note grid rows (white keys).</summary>
    public static ComponentResourceKey GridBackground { get; } = K(nameof(GridBackground));

    /// <summary>Note grid rows (black keys).</summary>
    public static ComponentResourceKey GridBlackRow { get; } = K(nameof(GridBlackRow));

    /// <summary>Bar lines.</summary>
    public static ComponentResourceKey GridBarLine { get; } = K(nameof(GridBarLine));

    /// <summary>Beat lines.</summary>
    public static ComponentResourceKey GridBeatLine { get; } = K(nameof(GridBeatLine));

    /// <summary>Subdivision lines.</summary>
    public static ComponentResourceKey GridSubLine { get; } = K(nameof(GridSubLine));

    /// <summary>MIDI note.</summary>
    public static ComponentResourceKey NoteFill { get; } = K(nameof(NoteFill));

    /// <summary>Selected MIDI note.</summary>
    public static ComponentResourceKey NoteSelectedFill { get; } = K(nameof(NoteSelectedFill));

    /// <summary>MIDI note outline.</summary>
    public static ComponentResourceKey NoteBorder { get; } = K(nameof(NoteBorder));

    /// <summary>Note name inside a note.</summary>
    public static ComponentResourceKey NoteText { get; } = K(nameof(NoteText));

    /// <summary>Playhead line.</summary>
    public static ComponentResourceKey Playhead { get; } = K(nameof(Playhead));

    /// <summary>Timeline ruler.</summary>
    public static ComponentResourceKey RulerBackground { get; } = K(nameof(RulerBackground));

    /// <summary>Bar numbers.</summary>
    public static ComponentResourceKey RulerText { get; } = K(nameof(RulerText));

    /// <summary>Marquee selection fill.</summary>
    public static ComponentResourceKey Selection { get; } = K(nameof(Selection));

    /// <summary>Empty step.</summary>
    public static ComponentResourceKey StepOff { get; } = K(nameof(StepOff));

    /// <summary>Active step.</summary>
    public static ComponentResourceKey StepOn { get; } = K(nameof(StepOn));

    /// <summary>Alternate beat-group background.</summary>
    public static ComponentResourceKey StepGroupBackground { get; } = K(nameof(StepGroupBackground));

    /// <summary>Current-step column highlight.</summary>
    public static ComponentResourceKey StepPlayhead { get; } = K(nameof(StepPlayhead));

    /// <summary>XY pad / envelope background.</summary>
    public static ComponentResourceKey PadBackground { get; } = K(nameof(PadBackground));

    /// <summary>XY pad / envelope grid.</summary>
    public static ComponentResourceKey PadGrid { get; } = K(nameof(PadGrid));

    /// <summary>XY pad puck and envelope handles.</summary>
    public static ComponentResourceKey PadPuck { get; } = K(nameof(PadPuck));

    /// <summary>XY pad crosshair.</summary>
    public static ComponentResourceKey PadCrosshair { get; } = K(nameof(PadCrosshair));

    /// <summary>Envelope curve.</summary>
    public static ComponentResourceKey EnvelopeStroke { get; } = K(nameof(EnvelopeStroke));

    /// <summary>Area under the envelope curve.</summary>
    public static ComponentResourceKey EnvelopeFill { get; } = K(nameof(EnvelopeFill));

    /// <summary>Pitch/mod wheel slot.</summary>
    public static ComponentResourceKey WheelSlot { get; } = K(nameof(WheelSlot));

    /// <summary>Pitch/mod wheel cylinder (horizontal shading).</summary>
    public static ComponentResourceKey WheelBody { get; } = K(nameof(WheelBody));

    /// <summary>Grip ribs on the wheel.</summary>
    public static ComponentResourceKey WheelRib { get; } = K(nameof(WheelRib));

    /// <summary>Position marker and centre detent.</summary>
    public static ComponentResourceKey WheelMarker { get; } = K(nameof(WheelMarker));

    /// <summary>Pedal plate at rest.</summary>
    public static ComponentResourceKey PedalBody { get; } = K(nameof(PedalBody));

    /// <summary>Pedal depth fill / pressed.</summary>
    public static ComponentResourceKey PedalLit { get; } = K(nameof(PedalLit));

    /// <summary>MIDI note activity LED / note map (when no lane colour is set).</summary>
    public static ComponentResourceKey ActivityNote { get; } = K(nameof(ActivityNote));

    /// <summary>MIDI control activity LED (CC, pitch bend, program, aftertouch).</summary>
    public static ComponentResourceKey ActivityControl { get; } = K(nameof(ActivityControl));

    /// <summary>Unlit activity LED and empty note map.</summary>
    public static ComponentResourceKey ActivityOff { get; } = K(nameof(ActivityOff));

    /// <summary>Alternate row background in the activity monitor.</summary>
    public static ComponentResourceKey ActivityRowAlt { get; } = K(nameof(ActivityRowAlt));

    /// <summary>Background of the selected track header.</summary>
    public static ComponentResourceKey TrackSelected { get; } = K(nameof(TrackSelected));

    /// <summary>Controller lane curve and points.</summary>
    public static ComponentResourceKey LaneValue { get; } = K(nameof(LaneValue));

    /// <summary>Area under the controller curve.</summary>
    public static ComponentResourceKey LaneFill { get; } = K(nameof(LaneFill));

    /// <summary>Corner radius for buttons, LCDs, panels.</summary>
    public static ComponentResourceKey CornerRadius { get; } = K(nameof(CornerRadius));

    /// <summary>UI font.</summary>
    public static ComponentResourceKey FontFamily { get; } = K(nameof(FontFamily));

    /// <summary>Monospaced font for the LCD and value boxes.</summary>
    public static ComponentResourceKey LcdFontFamily { get; } = K(nameof(LcdFontFamily));

    /// <summary>Thickness of the knob value ring.</summary>
    public static ComponentResourceKey KnobArcThickness { get; } = K(nameof(KnobArcThickness));

    /// <summary>All keys, for tooling and skin validation.</summary>
    public static System.Collections.Generic.IReadOnlyList<ComponentResourceKey> All { get; } = new[]
    {
        WindowBackground,
        PanelBackground,
        PanelRaised,
        PanelBorder,
        Text,
        TextDim,
        Accent,
        AccentForeground,
        Focus,
        KnobBody,
        KnobBodyBorder,
        KnobTrack,
        KnobIndicator,
        FaderGroove,
        FaderFill,
        FaderThumb,
        FaderThumbBorder,
        FaderThumbLine,
        MeterBackground,
        MeterSafe,
        MeterWarning,
        MeterDanger,
        MeterPeakHold,
        MeterClipOn,
        MeterClipOff,
        ScaleTick,
        ScaleText,
        ButtonBackground,
        ButtonHover,
        ButtonPressed,
        ButtonBorder,
        LedMute,
        LedSolo,
        LedArm,
        LedMonitor,
        LedLitForeground,
        TransportPlay,
        TransportRecord,
        LcdBackground,
        LcdBorder,
        LcdText,
        LcdTextDim,
        KeyWhite,
        KeyBlack,
        KeyBorder,
        KeyPressed,
        KeyHighlight,
        GridBackground,
        GridBlackRow,
        GridBarLine,
        GridBeatLine,
        GridSubLine,
        NoteFill,
        NoteSelectedFill,
        NoteBorder,
        NoteText,
        Playhead,
        RulerBackground,
        RulerText,
        Selection,
        StepOff,
        StepOn,
        StepGroupBackground,
        StepPlayhead,
        PadBackground,
        PadGrid,
        PadPuck,
        PadCrosshair,
        EnvelopeStroke,
        EnvelopeFill,
        WheelSlot,
        WheelBody,
        WheelRib,
        WheelMarker,
        PedalBody,
        PedalLit,
        ActivityNote,
        ActivityControl,
        ActivityOff,
        ActivityRowAlt,
        TrackSelected,
        LaneValue,
        LaneFill,
        CornerRadius,
        FontFamily,
        LcdFontFamily,
        KnobArcThickness,
    };
}
