using nMIDI.Lanes;
using nMIDI.Model;
using nMIDI.Playback;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace nMIDI
{
    /// <summary>
    /// A second window that shows every track in its own lane, one under the other, like the
    /// arrange view of a DAW. It works on the same tracks as the main window, so mute, solo and
    /// the eye button stay in sync, and it follows the same player.
    /// </summary>
    public partial class TracksWindow : Window
    {
        private readonly MainWindow _main;
        private bool _syncingScrollBar;
        private bool _syncingLaneZoom;
        private long _shownTick = -1;

        public TracksWindow(MainWindow main)
        {
            _main = main;
            InitializeComponent();
            DataContext = this;
            Owner = main;
            tglPauseMain.IsChecked = main.PauseNoteViewWithLanes;

            ruler.Axis = Axis;
            ruler.Seek += (_, tick) => _main.SeekTo(tick);

            Axis.Changed += (_, _) => OnAxisChanged();
            overlay.SizeChanged += (_, e) => Axis.SetWidth(e.NewSize.Width);
            hscroll.ViewportChanged += (_, e) =>
            {
                if (_syncingScrollBar || !e.IsUserChange) return;
                Axis.SetRange(hscroll.ViewportStart * Axis.ContentTicks, hscroll.ViewportLength * Axis.ContentTicks);
            };

            vzoom.MinViewportLength = 0.02;
            vzoom.ViewportChanged += (_, e) =>
            {
                if (_syncingLaneZoom || !e.IsUserChange) return;
                OnVerticalZoomBar(vzoom.ViewportStart, vzoom.ViewportLength);
            };

            PreviewKeyDown += OnPreviewKeyDown;
            CompositionTarget.Rendering += OnFrame;
            Closed += (_, _) => CompositionTarget.Rendering -= OnFrame;

            ShowSong(main.CurrentSong);
        }

        /// <summary>The selected range of a track, shared by every lane.</summary>
        public LaneRange Range { get; } = new();

        /// <summary>
        /// Range keys (Del, Ctrl+C, Ctrl+X, Ctrl+V, Ctrl+D, Esc) work on the selected range of a lane;
        /// everything else goes to the main window, so Space, Home, undo and the rest work here too.
        /// </summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var track = Range.Track?.Track;
            bool has = !Range.IsEmpty && track != null;
            switch (e.Key)
            {
                case Key.Delete or Key.Back when has:
                    _main.LaneRangeCommand(LaneRangeOp.Delete, track!, Range.Start, Range.End);
                    break;
                case Key.C when ctrl && has:
                    _main.LaneRangeCommand(LaneRangeOp.Copy, track!, Range.Start, Range.End);
                    break;
                case Key.X when ctrl && has:
                    _main.LaneRangeCommand(LaneRangeOp.Cut, track!, Range.Start, Range.End);
                    break;
                case Key.D when ctrl && has:
                    _main.LaneRangeCommand(LaneRangeOp.Duplicate, track!, Range.Start, Range.End);
                    Range.Set(Range.Track, Range.End, Range.End + (Range.End - Range.Start));
                    break;
                case Key.V when ctrl:
                    var target = track ?? _main.ActiveTrack;
                    if (target != null) _main.LaneRangeCommand(LaneRangeOp.Paste, target, 0, 0);
                    break;
                case Key.Escape when has:
                    Range.Clear();
                    break;
                default:
                    _main.OnPreviewKeyDown(sender, e);
                    return;
            }
            e.Handled = true;
        }

        private void OnLaneRangeMoved(object? sender, long by)
        {
            if (Range.IsEmpty || Range.Track?.Track is not { } track) return;
            _main.LaneRangeCommand(LaneRangeOp.Move, track, Range.Start, Range.End, by);
            Range.Set(Range.Track, Range.Start + by, Range.End + by);
        }

        /// <summary>The visible part of the song, shared by the ruler, the lanes and the scroll bar.</summary>
        public TimeAxis Axis { get; } = new();

        /// <summary>The main window's tracks; the lanes are made from these.</summary>
        public ObservableCollection<TrackViewModel> Tracks => _main.Tracks;

        /// <summary>Show a newly loaded song (or nothing) from the start.</summary>
        public void ShowSong(Song? song)
        {
            Range.Clear();
            txtEmpty.Visibility = song == null ? Visibility.Visible : Visibility.Collapsed;
            txtSong.Text = song?.Name ?? "";
            if (song == null) return;

            // Room after the last note, as in the main window.
            double content = Math.Max(song.LengthTicks + song.TicksPerBar * 8, song.TicksPerBar * 16);
            Axis.SetSong(content, song.TicksPerBeat, song.BeatsPerBar);
            scroller.ScrollToTop();
        }

        /// <summary>The song got longer (or shorter) after an edit: keep the view where it is.</summary>
        public void UpdateLength(double contentTicks) => Axis.SetContent(contentTicks);

        private void OnAxisChanged()
        {
            _syncingScrollBar = true;
            try
            {
                hscroll.MinViewportLength = Axis.MinLength / Axis.ContentTicks;
                hscroll.SetViewport(Axis.Start / Axis.ContentTicks, Axis.Length / Axis.ContentTicks);
            }
            finally
            {
                _syncingScrollBar = false;
            }
            UpdatePlayhead(_main.Player.PositionTicks);
        }

        // --- Playhead ---

        private void OnFrame(object? sender, EventArgs e)
        {
            UpdateTransport();
            if (_main.CurrentSong is not { } song) return;
            double tick = _main.Player.PositionTicks;
            UpdatePlayhead(tick);

            if ((long)tick != _shownTick)
            {
                _shownTick = (long)tick;
                txtTime.Text = Song.FormatTime(_main.Player.Position);
                txtBars.Text = song.FormatBars(_shownTick);
            }

            // Page-turn follow, like the main window: jump when the line is about to leave the view.
            if (btnFollow.IsChecked == true && _main.Player.State == PlayerState.Playing)
            {
                double x = Axis.TickToX(tick);
                if (x < 0 || x >= Axis.Width - Math.Max(6, Axis.Width * 0.015)) Axis.ScrollTo(tick, 0.02);
            }
        }

        private void UpdatePlayhead(double tick)
        {
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double x = Math.Round(Axis.TickToX(tick) * ppd) / ppd + 0.5 / ppd;
            playheadLine.X1 = playheadLine.X2 = x;
            playheadGlow.X1 = playheadGlow.X2 = x;
        }

        private void OnLaneSeek(object? sender, double tick) => _main.SeekTo(tick);

        // --- Transport: the main window does the work, these buttons only mirror the player's state ---

        private void OnPlay(object sender, RoutedEventArgs e) => _main.Play();
        private void OnPause(object sender, RoutedEventArgs e) => _main.Pause();
        private void OnStop(object sender, RoutedEventArgs e) => _main.OnStop(sender, e);
        private void OnPauseMainClick(object sender, RoutedEventArgs e) => _main.PauseNoteViewWithLanes = tglPauseMain.IsChecked == true;
        private void OnRewind(object sender, RoutedEventArgs e) => _main.OnRewind(sender, e);
        private void OnPanic(object sender, RoutedEventArgs e) => _main.OnPanic(sender, e);

        private void UpdateTransport()
        {
            var state = _main.Player.State;
            bool loaded = _main.CurrentSong != null;
            btnPlay.IsChecked = state == PlayerState.Playing;
            btnPause.IsChecked = state == PlayerState.Paused;
            btnStop.IsChecked = false;
            btnRewind.IsChecked = false;
            btnPanic.IsChecked = false;
            btnPlay.IsEnabled = btnPause.IsEnabled = btnStop.IsEnabled = btnRewind.IsEnabled = loaded;
        }

        // --- Zoom and scroll ---

        private void OnFit(object sender, RoutedEventArgs e) => Axis.SetRange(0, Axis.ContentTicks);

        /// <summary>
        /// Ctrl+wheel zooms the time around the mouse, Ctrl+Shift+wheel makes every lane taller or lower,
        /// Shift+wheel scrolls sideways, and the plain wheel scrolls the lanes.
        /// </summary>
        private void OnLanesWheel(object sender, MouseWheelEventArgs e)
        {
            var mods = Keyboard.Modifiers;
            if (mods.HasFlag(ModifierKeys.Control) && mods.HasFlag(ModifierKeys.Shift))
            {
                ScaleLanes(e.Delta > 0 ? 1.15 : 1 / 1.15);
                e.Handled = true;
            }
            else if (mods.HasFlag(ModifierKeys.Control))
            {
                double x = e.GetPosition(overlay).X;
                Axis.ZoomAt(e.Delta > 0 ? 1.25 : 1 / 1.25, Math.Clamp(x, 0, Axis.Width));
                e.Handled = true;
            }
            else if (mods.HasFlag(ModifierKeys.Shift))
            {
                Axis.SetRange(Axis.Start - e.Delta / 120.0 * Axis.Length * 0.1, Axis.Length);
                e.Handled = true;
            }
        }

        // The vertical zoom bar shows which part of the lane stack is visible, as a fraction of its height.
        private void OnLanesScrolled(object sender, ScrollChangedEventArgs e)
        {
            double extent = scroller.ExtentHeight;
            _syncingLaneZoom = true;
            try
            {
                if (extent <= 0 || scroller.ViewportHeight >= extent) vzoom.SetViewport(0, 1);
                else vzoom.SetViewport(scroller.VerticalOffset / extent, scroller.ViewportHeight / extent);
            }
            finally
            {
                _syncingLaneZoom = false;
            }
        }

        /// <summary>
        /// The user moved the vertical bar. If its thumb changed length, every lane is scaled so the
        /// visible part matches the new length; then the lanes scroll to the thumb's position.
        /// </summary>
        private void OnVerticalZoomBar(double start, double length)
        {
            double extent = scroller.ExtentHeight, viewport = scroller.ViewportHeight;
            if (extent <= 0 || viewport <= 0) return;

            double oldLength = Math.Min(1, viewport / extent);
            if (Math.Abs(length - oldLength) > 1e-4)
            {
                ScaleLanes(oldLength / length);
                scroller.UpdateLayout();
                extent = scroller.ExtentHeight;
            }
            scroller.ScrollToVerticalOffset(start * extent);
        }

        /// <summary>Make every lane taller (factor above 1) or lower, keeping their sizes relative to each other.</summary>
        private void ScaleLanes(double factor)
        {
            foreach (var t in Tracks) t.LaneHeight *= factor;
        }

        // --- Lane height ---

        private void OnLaneResize(object sender, DragDeltaEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TrackViewModel vm) vm.LaneHeight += e.VerticalChange;
        }

        private void OnLaneResetHeight(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TrackViewModel vm) vm.LaneHeight = TrackViewModel.DefaultLaneHeight;
            e.Handled = true;
        }

        private void OnGrowLanes(object sender, RoutedEventArgs e) => ScaleLanes(1.25);

        private void OnShrinkLanes(object sender, RoutedEventArgs e) => ScaleLanes(1 / 1.25);

        // --- Track buttons: the same right-click behaviour as in the main window ---

        private void OnMuteRightClick(object sender, MouseButtonEventArgs e) => _main.OnMuteRightClick(sender, e);
        private void OnSoloRightClick(object sender, MouseButtonEventArgs e) => _main.OnSoloRightClick(sender, e);
        private void OnShowRightClick(object sender, MouseButtonEventArgs e) => _main.OnShowRightClick(sender, e);
    }
}
