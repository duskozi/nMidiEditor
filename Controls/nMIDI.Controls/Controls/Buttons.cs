using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace nMIDI.Controls;

/// <summary>The meaning of an <see cref="LedToggleButton"/>, which picks its lit colour.</summary>
public enum LedRole
{
    /// <summary>Any toggle: lit in the accent colour.</summary>
    Generic,
    /// <summary>Mute.</summary>
    Mute,
    /// <summary>Solo.</summary>
    Solo,
    /// <summary>Record arm.</summary>
    RecordArm,
    /// <summary>Input monitoring.</summary>
    Monitor,
}

/// <summary>
/// Small latching button that lights up when checked: Mute / Solo / Arm / Monitor and generic toggles.
/// <see cref="Role"/> picks the skin colour; set <see cref="LitBrush"/> to override per instance.
/// </summary>
public class LedToggleButton : ToggleButton
{
    static LedToggleButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(LedToggleButton), new FrameworkPropertyMetadata(typeof(LedToggleButton)));
    }

    /// <summary>Identifies the <see cref="Role"/> dependency property.</summary>
    public static readonly DependencyProperty RoleProperty = DependencyProperty.Register(
        nameof(Role), typeof(LedRole), typeof(LedToggleButton), new FrameworkPropertyMetadata(LedRole.Generic));

    /// <summary>What the button stands for; picks the lit colour from the skin.</summary>
    public LedRole Role
    {
        get => (LedRole)GetValue(RoleProperty);
        set => SetValue(RoleProperty, value);
    }

    /// <summary>Identifies the <see cref="LitBrush"/> dependency property.</summary>
    public static readonly DependencyProperty LitBrushProperty = DependencyProperty.Register(
        nameof(LitBrush), typeof(Brush), typeof(LedToggleButton), new FrameworkPropertyMetadata(null));

    /// <summary>Fill used while the button is on; set by <see cref="Role"/> unless given.</summary>
    public Brush? LitBrush
    {
        get => (Brush?)GetValue(LitBrushProperty);
        set => SetValue(LitBrushProperty, value);
    }
}

/// <summary>The transport functions a <see cref="TransportButton"/> can show.</summary>
public enum TransportKind
{
    /// <summary>Play.</summary>
    Play,
    /// <summary>Pause.</summary>
    Pause,
    /// <summary>Stop.</summary>
    Stop,
    /// <summary>Record.</summary>
    Record,
    /// <summary>Rewind (move back).</summary>
    Rewind,
    /// <summary>Fast forward (move ahead).</summary>
    FastForward,
    /// <summary>Go to the start.</summary>
    ToStart,
    /// <summary>Go to the end.</summary>
    ToEnd,
    /// <summary>Loop on or off.</summary>
    Loop,
    /// <summary>Metronome on or off.</summary>
    Metronome,
}

/// <summary>
/// Transport button with a skinnable vector icon chosen by <see cref="Kind"/>.
/// Play / Record / Loop / Metronome latch (IsChecked); the others are momentary.
/// </summary>
public class TransportButton : ToggleButton
{
    static TransportButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(TransportButton), new FrameworkPropertyMetadata(typeof(TransportButton)));
    }

    /// <summary>Creates a <see cref="TransportButton"/>.</summary>
    public TransportButton() => AutomationProperties.SetName(this, Kind.ToString());

    /// <summary>Identifies the <see cref="Kind"/> dependency property.</summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(TransportKind), typeof(TransportButton),
        new FrameworkPropertyMetadata(TransportKind.Play, OnKindChanged));

    /// <summary>Which transport function the button shows; picks its icon.</summary>
    public TransportKind Kind
    {
        get => (TransportKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Identifies the <see cref="Icon"/> dependency property.</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(TransportButton), new FrameworkPropertyMetadata(null));

    /// <summary>Icon geometry (24×24 design box). Set by the style from the skin's icon keys.</summary>
    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Identifies the <see cref="LitBrush"/> dependency property.</summary>
    public static readonly DependencyProperty LitBrushProperty = DependencyProperty.Register(
        nameof(LitBrush), typeof(Brush), typeof(TransportButton), new FrameworkPropertyMetadata(null));

    /// <summary>Fill used while the button is on (checked).</summary>
    public Brush? LitBrush
    {
        get => (Brush?)GetValue(LitBrushProperty);
        set => SetValue(LitBrushProperty, value);
    }

    private static readonly DependencyPropertyKey IsLatchingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsLatching), typeof(bool), typeof(TransportButton), new FrameworkPropertyMetadata(true));

    /// <summary>Identifies the <see cref="IsLatching"/> dependency property.</summary>
    public static readonly DependencyProperty IsLatchingProperty = IsLatchingPropertyKey.DependencyProperty;

    /// <summary>True for kinds that toggle on click.</summary>
    public bool IsLatching => (bool)GetValue(IsLatchingProperty);

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var kind = (TransportKind)e.NewValue;
        bool latching = kind is TransportKind.Play or TransportKind.Record or TransportKind.Loop or TransportKind.Metronome;
        d.SetValue(IsLatchingPropertyKey, latching);
        // Icon-only button: give screen readers a name unless the app set one.
        if (string.IsNullOrEmpty(AutomationProperties.GetName(d)) || AutomationProperties.GetName(d) == e.OldValue?.ToString())
            AutomationProperties.SetName(d, kind.ToString());
    }

    /// <inheritdoc/>
    protected override void OnToggle()
    {
        if (IsLatching) base.OnToggle();
    }
}
