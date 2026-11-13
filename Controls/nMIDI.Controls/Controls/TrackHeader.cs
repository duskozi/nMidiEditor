using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace nMIDI.Controls;

/// <summary>
/// Track header for a MIDI track list: colour, number, name, subtitle, activity light, Mute / Solo,
/// "show on piano" toggle and an output-device selector. Everything is two-way bindable.
/// Activity methods (<see cref="NoteOn"/> …) are thread-safe and forward to the embedded
/// <see cref="MidiActivityMonitor"/> (template part <c>PART_Activity</c>); they cost nothing while the header is hidden.
/// </summary>
[TemplatePart(Name = PartActivity, Type = typeof(MidiActivityMonitor))]
public class TrackHeader : Control
{
    /// <summary>Template part name of the activity LED.</summary>
    public const string PartActivity = "PART_Activity";

    private volatile MidiActivityMonitor? _activity;

    static TrackHeader()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(TrackHeader), new FrameworkPropertyMetadata(typeof(TrackHeader)));
        FocusableProperty.OverrideMetadata(typeof(TrackHeader), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Identifies the <see cref="TrackName"/> dependency property.</summary>
    public static readonly DependencyProperty TrackNameProperty = DependencyProperty.Register(
        nameof(TrackName), typeof(string), typeof(TrackHeader), new FrameworkPropertyMetadata("Track"));
    /// <summary>Track name.</summary>
    public string TrackName { get => (string)GetValue(TrackNameProperty); set => SetValue(TrackNameProperty, value); }

    /// <summary>Identifies the <see cref="TrackNumber"/> dependency property.</summary>
    public static readonly DependencyProperty TrackNumberProperty = DependencyProperty.Register(
        nameof(TrackNumber), typeof(int), typeof(TrackHeader), new FrameworkPropertyMetadata(1));
    /// <summary>Track number shown before the name.</summary>
    public int TrackNumber { get => (int)GetValue(TrackNumberProperty); set => SetValue(TrackNumberProperty, value); }

    /// <summary>Identifies the <see cref="Subtitle"/> dependency property.</summary>
    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(TrackHeader), new FrameworkPropertyMetadata(null));
    /// <summary>Second line, e.g. "Ch 1 · Grand Piano · 532 notes".</summary>
    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }

    /// <summary>Identifies the <see cref="TrackColor"/> dependency property.</summary>
    public static readonly DependencyProperty TrackColorProperty = DependencyProperty.Register(
        nameof(TrackColor), typeof(Brush), typeof(TrackHeader), new FrameworkPropertyMetadata(null));
    /// <summary>Track colour, shown as the bar on the left.</summary>
    public Brush? TrackColor { get => (Brush?)GetValue(TrackColorProperty); set => SetValue(TrackColorProperty, value); }

    /// <summary>Identifies the <see cref="IsMuted"/> dependency property.</summary>
    public static readonly DependencyProperty IsMutedProperty = RegisterFlag(nameof(IsMuted), false);
    /// <summary>Whether the track is muted.</summary>
    public bool IsMuted { get => (bool)GetValue(IsMutedProperty); set => SetValue(IsMutedProperty, value); }

    /// <summary>Identifies the <see cref="IsSoloed"/> dependency property.</summary>
    public static readonly DependencyProperty IsSoloedProperty = RegisterFlag(nameof(IsSoloed), false);
    /// <summary>Whether the track is soloed.</summary>
    public bool IsSoloed { get => (bool)GetValue(IsSoloedProperty); set => SetValue(IsSoloedProperty, value); }

    /// <summary>Identifies the <see cref="IsShownOnPiano"/> dependency property.</summary>
    public static readonly DependencyProperty IsShownOnPianoProperty = RegisterFlag(nameof(IsShownOnPiano), true);
    /// <summary>Whether this track's notes are drawn on the keyboard / piano roll.</summary>
    public bool IsShownOnPiano { get => (bool)GetValue(IsShownOnPianoProperty); set => SetValue(IsShownOnPianoProperty, value); }

    /// <summary>Identifies the <see cref="IsSelected"/> dependency property.</summary>
    public static readonly DependencyProperty IsSelectedProperty = RegisterFlag(nameof(IsSelected), false);
    /// <summary>Whether this is the selected track (drawn highlighted).</summary>
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }

    /// <summary>Identifies the <see cref="IsAudible"/> dependency property.</summary>
    public static readonly DependencyProperty IsAudibleProperty = DependencyProperty.Register(
        nameof(IsAudible), typeof(bool), typeof(TrackHeader), new FrameworkPropertyMetadata(true));
    /// <summary>False when muted or silenced by another track's solo; the header dims (set by the app).</summary>
    public bool IsAudible { get => (bool)GetValue(IsAudibleProperty); set => SetValue(IsAudibleProperty, value); }

    /// <summary>Identifies the <see cref="OutputDevices"/> dependency property.</summary>
    public static readonly DependencyProperty OutputDevicesProperty = DependencyProperty.Register(
        nameof(OutputDevices), typeof(IEnumerable), typeof(TrackHeader), new FrameworkPropertyMetadata(null));
    /// <summary>Output devices offered in the selector.</summary>
    public IEnumerable? OutputDevices { get => (IEnumerable?)GetValue(OutputDevicesProperty); set => SetValue(OutputDevicesProperty, value); }

    /// <summary>Identifies the <see cref="SelectedOutputDevice"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedOutputDeviceProperty = DependencyProperty.Register(
        nameof(SelectedOutputDevice), typeof(object), typeof(TrackHeader),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    /// <summary>The selected output device.</summary>
    public object? SelectedOutputDevice { get => GetValue(SelectedOutputDeviceProperty); set => SetValue(SelectedOutputDeviceProperty, value); }

    /// <summary>Identifies the <see cref="ShowDeviceSelector"/> dependency property.</summary>
    public static readonly DependencyProperty ShowDeviceSelectorProperty = DependencyProperty.Register(
        nameof(ShowDeviceSelector), typeof(bool), typeof(TrackHeader), new FrameworkPropertyMetadata(true));
    /// <summary>Whether the output device selector is shown.</summary>
    public bool ShowDeviceSelector { get => (bool)GetValue(ShowDeviceSelectorProperty); set => SetValue(ShowDeviceSelectorProperty, value); }

    /// <summary>Identifies the <see cref="ShowActivity"/> dependency property.</summary>
    public static readonly DependencyProperty ShowActivityProperty = DependencyProperty.Register(
        nameof(ShowActivity), typeof(bool), typeof(TrackHeader), new FrameworkPropertyMetadata(true));
    /// <summary>Whether the MIDI activity LED is shown.</summary>
    public bool ShowActivity { get => (bool)GetValue(ShowActivityProperty); set => SetValue(ShowActivityProperty, value); }

    private static DependencyProperty RegisterFlag(string name, bool def) => DependencyProperty.Register(
        name, typeof(bool), typeof(TrackHeader), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Identifies the <see cref="HeaderClicked"/> routed event.</summary>
    public static readonly RoutedEvent HeaderClickedEvent = EventManager.RegisterRoutedEvent(
        nameof(HeaderClicked), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(TrackHeader));

    /// <summary>Clicked on the header body (not a button) – select the track.</summary>
    public event RoutedEventHandler HeaderClicked { add => AddHandler(HeaderClickedEvent, value); remove => RemoveHandler(HeaderClickedEvent, value); }

    /// <inheritdoc/>
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _activity = GetTemplateChild(PartActivity) as MidiActivityMonitor;
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (e.Handled) return; // buttons/combo handle their own clicks
        SetCurrentValue(IsSelectedProperty, true);
        RaiseEvent(new RoutedEventArgs(HeaderClickedEvent, this));
    }

    /// <summary>The embedded activity light (null before the template is applied or when the template has none).</summary>
    public MidiActivityMonitor? Activity => _activity;

    // Thread-safe forwarding (the monitor itself is thread-safe and ignores input while hidden).
    /// <summary>Shows a note-on on the activity LED.</summary>
    public void NoteOn(int note, int velocity) => _activity?.NoteOn(0, note, velocity);
    /// <summary>Shows a note-off on the activity LED.</summary>
    public void NoteOff(int note) => _activity?.NoteOff(0, note);
    /// <summary>Shows a control change on the activity LED.</summary>
    public void ControlChange(int controller, int value) => _activity?.ControlChange(0, controller, value);
    /// <summary>Shows pitch bend on the activity LED.</summary>
    public void PitchBend(int value) => _activity?.PitchBend(0, value);
    /// <summary>Shows a program change on the activity LED.</summary>
    public void ProgramChange(int program) => _activity?.ProgramChange(0, program);
    /// <summary>Clears the activity LED's held notes.</summary>
    public void AllNotesOff() => _activity?.AllNotesOff(0);
}
