using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace nMIDI.Controls;

/// <summary>
/// Complete transport: to-start, rewind, forward, stop, play, record, loop, metronome, position LCD and tempo.
/// State properties bind two-way; command properties let a view-model react to button presses.
/// Stop clears <see cref="IsPlaying"/> and <see cref="IsRecording"/>; a second Stop while stopped returns to zero
/// (the usual DAW behaviour).
/// </summary>
public class TransportBar : Control
{
    static TransportBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(TransportBar), new FrameworkPropertyMetadata(typeof(TransportBar)));
        FocusableProperty.OverrideMetadata(typeof(TransportBar), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Creates a <see cref="TransportBar"/>.</summary>
    public TransportBar()
    {
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnButtonClick));
    }

    #region State

    /// <summary>Identifies the <see cref="IsPlaying"/> dependency property.</summary>
    public static readonly DependencyProperty IsPlayingProperty = RegisterState(nameof(IsPlaying));
    /// <summary>Whether playback is running.</summary>
    public bool IsPlaying { get => (bool)GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }

    /// <summary>Identifies the <see cref="IsRecording"/> dependency property.</summary>
    public static readonly DependencyProperty IsRecordingProperty = RegisterState(nameof(IsRecording));
    /// <summary>Whether recording is on.</summary>
    public bool IsRecording { get => (bool)GetValue(IsRecordingProperty); set => SetValue(IsRecordingProperty, value); }

    /// <summary>Identifies the <see cref="IsLooping"/> dependency property.</summary>
    public static readonly DependencyProperty IsLoopingProperty = RegisterState(nameof(IsLooping));
    /// <summary>Whether the loop button is on.</summary>
    public bool IsLooping { get => (bool)GetValue(IsLoopingProperty); set => SetValue(IsLoopingProperty, value); }

    /// <summary>Identifies the <see cref="IsMetronomeOn"/> dependency property.</summary>
    public static readonly DependencyProperty IsMetronomeOnProperty = RegisterState(nameof(IsMetronomeOn));
    /// <summary>Whether the metronome button is on.</summary>
    public bool IsMetronomeOn { get => (bool)GetValue(IsMetronomeOnProperty); set => SetValue(IsMetronomeOnProperty, value); }

    private static DependencyProperty RegisterState(string name) => DependencyProperty.Register(
        name, typeof(bool), typeof(TransportBar),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Identifies the <see cref="Position"/> dependency property.</summary>
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(double), typeof(TransportBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Song position in beats.</summary>
    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

    /// <summary>Identifies the <see cref="Tempo"/> dependency property.</summary>
    public static readonly DependencyProperty TempoProperty = DependencyProperty.Register(
        nameof(Tempo), typeof(double), typeof(TransportBar),
        new FrameworkPropertyMetadata(120.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Tempo in beats per minute.</summary>
    public double Tempo { get => (double)GetValue(TempoProperty); set => SetValue(TempoProperty, value); }

    /// <summary>Identifies the <see cref="BeatsPerBar"/> dependency property.</summary>
    public static readonly DependencyProperty BeatsPerBarProperty = DependencyProperty.Register(
        nameof(BeatsPerBar), typeof(int), typeof(TransportBar), new FrameworkPropertyMetadata(4));

    /// <summary>Beats per bar, for the position display.</summary>
    public int BeatsPerBar { get => (int)GetValue(BeatsPerBarProperty); set => SetValue(BeatsPerBarProperty, value); }

    /// <summary>Identifies the <see cref="BeatUnit"/> dependency property.</summary>
    public static readonly DependencyProperty BeatUnitProperty = DependencyProperty.Register(
        nameof(BeatUnit), typeof(int), typeof(TransportBar), new FrameworkPropertyMetadata(4));

    /// <summary>Denominator of the time signature (display only).</summary>
    public int BeatUnit { get => (int)GetValue(BeatUnitProperty); set => SetValue(BeatUnitProperty, value); }

    #endregion

    #region Commands

    /// <summary>Identifies the <see cref="PlayCommand"/> dependency property.</summary>
    public static readonly DependencyProperty PlayCommandProperty = RegisterCommand(nameof(PlayCommand));
    /// <summary>Executed when Play is clicked; parameter is the new IsPlaying value.</summary>
    public ICommand? PlayCommand { get => (ICommand?)GetValue(PlayCommandProperty); set => SetValue(PlayCommandProperty, value); }

    /// <summary>Identifies the <see cref="StopCommand"/> dependency property.</summary>
    public static readonly DependencyProperty StopCommandProperty = RegisterCommand(nameof(StopCommand));
    /// <summary>Stop button.</summary>
    public ICommand? StopCommand { get => (ICommand?)GetValue(StopCommandProperty); set => SetValue(StopCommandProperty, value); }

    /// <summary>Identifies the <see cref="RecordCommand"/> dependency property.</summary>
    public static readonly DependencyProperty RecordCommandProperty = RegisterCommand(nameof(RecordCommand));
    /// <summary>Record button; the parameter is the new <see cref="IsRecording"/>.</summary>
    public ICommand? RecordCommand { get => (ICommand?)GetValue(RecordCommandProperty); set => SetValue(RecordCommandProperty, value); }

    /// <summary>Identifies the <see cref="RewindCommand"/> dependency property.</summary>
    public static readonly DependencyProperty RewindCommandProperty = RegisterCommand(nameof(RewindCommand));
    /// <summary>Rewind button. Without a command the bar moves <see cref="Position"/> back by <see cref="SeekStep"/>.</summary>
    public ICommand? RewindCommand { get => (ICommand?)GetValue(RewindCommandProperty); set => SetValue(RewindCommandProperty, value); }

    /// <summary>Identifies the <see cref="ForwardCommand"/> dependency property.</summary>
    public static readonly DependencyProperty ForwardCommandProperty = RegisterCommand(nameof(ForwardCommand));
    /// <summary>Fast-forward button. Without a command the bar moves <see cref="Position"/> on by <see cref="SeekStep"/>.</summary>
    public ICommand? ForwardCommand { get => (ICommand?)GetValue(ForwardCommandProperty); set => SetValue(ForwardCommandProperty, value); }

    /// <summary>Identifies the <see cref="ToStartCommand"/> dependency property.</summary>
    public static readonly DependencyProperty ToStartCommandProperty = RegisterCommand(nameof(ToStartCommand));
    /// <summary>Go-to-start button. Without a command the bar just sets <see cref="Position"/> to 0.</summary>
    public ICommand? ToStartCommand { get => (ICommand?)GetValue(ToStartCommandProperty); set => SetValue(ToStartCommandProperty, value); }

    private static DependencyProperty RegisterCommand(string name) => DependencyProperty.Register(
        name, typeof(ICommand), typeof(TransportBar), new FrameworkPropertyMetadata(null));

    #endregion

    /// <summary>Identifies the <see cref="SeekStep"/> dependency property.</summary>
    public static readonly DependencyProperty SeekStepProperty = DependencyProperty.Register(
        nameof(SeekStep), typeof(double), typeof(TransportBar), new FrameworkPropertyMetadata(4.0));

    /// <summary>Beats moved by rewind / forward when no command handles them (default one 4/4 bar).</summary>
    public double SeekStep { get => (double)GetValue(SeekStepProperty); set => SetValue(SeekStepProperty, value); }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TransportButton button) return;
        switch (button.Kind)
        {
            case TransportKind.Play:
                Execute(PlayCommand, IsPlaying);
                break;
            case TransportKind.Record:
                Execute(RecordCommand, IsRecording);
                break;
            case TransportKind.Stop:
                if (!IsPlaying && !IsRecording)
                    SetCurrentValue(PositionProperty, 0.0);
                SetCurrentValue(IsPlayingProperty, false);
                SetCurrentValue(IsRecordingProperty, false);
                Execute(StopCommand, null);
                break;
            case TransportKind.ToStart:
                SetCurrentValue(PositionProperty, 0.0);
                Execute(ToStartCommand, null);
                break;
            case TransportKind.Rewind:
                if (!Execute(RewindCommand, null))
                    SetCurrentValue(PositionProperty, System.Math.Max(0, Position - SeekStep));
                break;
            case TransportKind.FastForward:
                if (!Execute(ForwardCommand, null))
                    SetCurrentValue(PositionProperty, Position + SeekStep);
                break;
        }
    }

    private static bool Execute(ICommand? command, object? parameter)
    {
        if (command is null || !command.CanExecute(parameter)) return false;
        command.Execute(parameter);
        return true;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Space toggles play when focus is inside the bar (apps usually also bind a window-level shortcut).
        if (!e.Handled && e.Key == Key.Space)
        {
            SetCurrentValue(IsPlayingProperty, !IsPlaying);
            Execute(PlayCommand, IsPlaying);
            e.Handled = true;
        }
    }
}
