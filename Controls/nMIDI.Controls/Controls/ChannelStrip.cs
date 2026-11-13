using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace nMIDI.Controls;

/// <summary>
/// Mixer channel strip composed from the library's parts: name + colour, pan knob, volume fader with
/// dB scale, stereo meter, Mute / Solo / Arm. Everything is two-way bindable; re-template it freely.
/// Template parts <c>PART_MeterLeft</c>/<c>PART_MeterRight</c> enable <see cref="PushLevels"/>.
/// </summary>
[TemplatePart(Name = PartMeterLeft, Type = typeof(LevelMeter))]
[TemplatePart(Name = PartMeterRight, Type = typeof(LevelMeter))]
public class ChannelStrip : Control
{
    /// <summary>Template part name of the left level meter.</summary>
    public const string PartMeterLeft = "PART_MeterLeft";
    /// <summary>Template part name of the right level meter.</summary>
    public const string PartMeterRight = "PART_MeterRight";

    // Read from the audio thread in PushLevels, written on the UI thread in OnApplyTemplate.
    private volatile LevelMeter? _left;
    private volatile LevelMeter? _right;

    static ChannelStrip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ChannelStrip), new FrameworkPropertyMetadata(typeof(ChannelStrip)));
        FocusableProperty.OverrideMetadata(typeof(ChannelStrip), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Identifies the <see cref="TrackName"/> dependency property.</summary>
    public static readonly DependencyProperty TrackNameProperty = DependencyProperty.Register(
        nameof(TrackName), typeof(string), typeof(ChannelStrip), new FrameworkPropertyMetadata("Track"));
    /// <summary>Name shown at the top of the strip.</summary>
    public string TrackName { get => (string)GetValue(TrackNameProperty); set => SetValue(TrackNameProperty, value); }

    /// <summary>Identifies the <see cref="TrackColor"/> dependency property.</summary>
    public static readonly DependencyProperty TrackColorProperty = DependencyProperty.Register(
        nameof(TrackColor), typeof(Brush), typeof(ChannelStrip), new FrameworkPropertyMetadata(null));
    /// <summary>Track colour, shown as the strip's top bar and the pan arc.</summary>
    public Brush? TrackColor { get => (Brush?)GetValue(TrackColorProperty); set => SetValue(TrackColorProperty, value); }

    /// <summary>Identifies the <see cref="Volume"/> dependency property.</summary>
    public static readonly DependencyProperty VolumeProperty = DependencyProperty.Register(
        nameof(Volume), typeof(double), typeof(ChannelStrip),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    /// <summary>Fader level in dB (-70 = -∞ … +6).</summary>
    public double Volume { get => (double)GetValue(VolumeProperty); set => SetValue(VolumeProperty, value); }

    /// <summary>Identifies the <see cref="Pan"/> dependency property.</summary>
    public static readonly DependencyProperty PanProperty = DependencyProperty.Register(
        nameof(Pan), typeof(double), typeof(ChannelStrip),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    /// <summary>-1 (left) … +1 (right).</summary>
    public double Pan { get => (double)GetValue(PanProperty); set => SetValue(PanProperty, value); }

    /// <summary>Identifies the <see cref="IsMuted"/> dependency property.</summary>
    public static readonly DependencyProperty IsMutedProperty = RegisterFlag(nameof(IsMuted));
    /// <summary>Whether the track is muted.</summary>
    public bool IsMuted { get => (bool)GetValue(IsMutedProperty); set => SetValue(IsMutedProperty, value); }

    /// <summary>Identifies the <see cref="IsSoloed"/> dependency property.</summary>
    public static readonly DependencyProperty IsSoloedProperty = RegisterFlag(nameof(IsSoloed));
    /// <summary>Whether the track is soloed.</summary>
    public bool IsSoloed { get => (bool)GetValue(IsSoloedProperty); set => SetValue(IsSoloedProperty, value); }

    /// <summary>Identifies the <see cref="IsArmed"/> dependency property.</summary>
    public static readonly DependencyProperty IsArmedProperty = RegisterFlag(nameof(IsArmed));
    /// <summary>Whether the track is armed for recording.</summary>
    public bool IsArmed { get => (bool)GetValue(IsArmedProperty); set => SetValue(IsArmedProperty, value); }

    /// <summary>Identifies the <see cref="ShowArmButton"/> dependency property.</summary>
    public static readonly DependencyProperty ShowArmButtonProperty = DependencyProperty.Register(
        nameof(ShowArmButton), typeof(bool), typeof(ChannelStrip), new FrameworkPropertyMetadata(true));
    /// <summary>Hide Arm on busses / master.</summary>
    public bool ShowArmButton { get => (bool)GetValue(ShowArmButtonProperty); set => SetValue(ShowArmButtonProperty, value); }

    /// <summary>Identifies the <see cref="PeakLeft"/> dependency property.</summary>
    public static readonly DependencyProperty PeakLeftProperty = RegisterLevel(nameof(PeakLeft));
    /// <summary>Left peak level as linear gain (1 = 0 dBFS).</summary>
    public double PeakLeft { get => (double)GetValue(PeakLeftProperty); set => SetValue(PeakLeftProperty, value); }

    /// <summary>Identifies the <see cref="PeakRight"/> dependency property.</summary>
    public static readonly DependencyProperty PeakRightProperty = RegisterLevel(nameof(PeakRight));
    /// <summary>Right peak level as linear gain (1 = 0 dBFS).</summary>
    public double PeakRight { get => (double)GetValue(PeakRightProperty); set => SetValue(PeakRightProperty, value); }

    /// <summary>Identifies the <see cref="RmsLeft"/> dependency property.</summary>
    public static readonly DependencyProperty RmsLeftProperty = RegisterLevel(nameof(RmsLeft));
    /// <summary>Left RMS level as linear gain (1 = 0 dBFS).</summary>
    public double RmsLeft { get => (double)GetValue(RmsLeftProperty); set => SetValue(RmsLeftProperty, value); }

    /// <summary>Identifies the <see cref="RmsRight"/> dependency property.</summary>
    public static readonly DependencyProperty RmsRightProperty = RegisterLevel(nameof(RmsRight));
    /// <summary>Right RMS level as linear gain (1 = 0 dBFS).</summary>
    public double RmsRight { get => (double)GetValue(RmsRightProperty); set => SetValue(RmsRightProperty, value); }

    private static DependencyProperty RegisterFlag(string name) => DependencyProperty.Register(
        name, typeof(bool), typeof(ChannelStrip), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private static DependencyProperty RegisterLevel(string name) => DependencyProperty.Register(
        name, typeof(double), typeof(ChannelStrip), new FrameworkPropertyMetadata(0.0));

    /// <inheritdoc/>
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _left = GetTemplateChild(PartMeterLeft) as LevelMeter;
        _right = GetTemplateChild(PartMeterRight) as LevelMeter;
    }

    /// <summary>Thread-safe: forwards audio-thread levels to both meters (see <see cref="LevelMeter.PushLevels"/>).</summary>
    public void PushLevels(float peakLeft, float rmsLeft, float peakRight, float rmsRight)
    {
        _left?.PushLevels(peakLeft, rmsLeft);
        _right?.PushLevels(peakRight, rmsRight);
    }
}
