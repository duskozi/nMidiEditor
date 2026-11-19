using nMIDI.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace nMIDI
{
    /// <summary>
    /// The mixer window's link to the player, and pausing the note view while another window is in use.
    /// </summary>
    public partial class MainWindow
    {
        private MixerWindow? _mixerWindow;
        private bool _noteViewPaused;
        private bool _elementsDirty;

        /// <summary>Open the mixer window, or bring it to the front if it is already open.</summary>
        private void OnOpenMixer(object sender, RoutedEventArgs e)
        {
            if (_mixerWindow == null)
            {
                _mixerWindow = new MixerWindow(this);
                _mixerWindow.Closed += (_, _) => _mixerWindow = null;
                _mixerWindow.Loaded += (s, _) => StyleScrollBars((DependencyObject)s!);
                _mixerWindow.Show();
            }
            else
            {
                if (_mixerWindow.WindowState == WindowState.Minimized) _mixerWindow.WindowState = WindowState.Normal;
                _mixerWindow.Activate();
            }
        }

        // --- MIDI activity window ---

        private MidiMonitorWindow? _monitorWindow;

        /// <summary>Names of the open MIDI output ports, for the activity window.</summary>
        internal IReadOnlyList<string> OpenOutputNames => _outputs.OpenNames;

        /// <summary>The open MIDI input port, or null.</summary>
        internal string? InputName => _input.Name;

        /// <summary>Open the MIDI activity window, or bring it to the front if it is already open.</summary>
        private void OnOpenMonitor(object sender, RoutedEventArgs e)
        {
            if (_monitorWindow == null)
            {
                var window = new MidiMonitorWindow(this);
                window.Closed += (_, _) => _monitorWindow = null;
                window.Loaded += (s, _) => StyleScrollBars((DependencyObject)s!);
                _monitorWindow = window;
                window.Show();
            }
            else
            {
                if (_monitorWindow.WindowState == WindowState.Minimized) _monitorWindow.WindowState = WindowState.Normal;
                _monitorWindow.Activate();
            }
        }

        /// <summary>Make a track the edited (and recorded) track, from another window.</summary>
        internal void SelectTrack(TrackViewModel vm) => SetActiveTrack(vm);

        /// <summary>Send a track's mixer settings and channels to the player (after loading or reordering tracks).</summary>
        private void PushMix(TrackViewModel vm)
        {
            vm.Mix.ControlChanged -= OnMixChanged;
            vm.Mix.ControlChanged += OnMixChanged;
            _player.SetTrackChannels(vm.Index, vm.Track.Channels);
            foreach (int cc in MixerChannel.Controllers)
                _player.SetTrackControl(vm.Index, cc, vm.Mix.IsOverridden(cc) ? vm.Mix.Get(cc) : -1);
        }

        private void OnMixChanged(MixerChannel mix, int cc, int value)
        {
            var vm = Tracks.FirstOrDefault(t => t.Mix == mix);
            if (vm == null) return;
            _player.SetTrackChannels(vm.Index, vm.Track.Channels);
            _player.SetTrackControl(vm.Index, cc, value);
        }

        // --- Pausing the note view ---

        /// <summary>
        /// Stop drawing the piano roll, its playhead and the keyboard while the Lanes window is open and
        /// this window is not the one in use, to save CPU.
        /// </summary>
        internal bool PauseNoteViewWithLanes
        {
            get => _settings.PauseNoteViewWithLanes;
            set
            {
                _settings.PauseNoteViewWithLanes = value;
                _settings.Save();
            }
        }

        /// <summary>Work out once per frame whether the note view is paused; on resume, bring it up to date.</summary>
        private void UpdateNoteViewPause()
        {
            bool paused = _settings.PauseNoteViewWithLanes && !IsActive
                          && _tracksWindow is { IsVisible: true } lanes && lanes.WindowState != WindowState.Minimized;
            if (paused == _noteViewPaused) return;
            _noteViewPaused = paused;
            pnlPaused.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
            if (paused) return;

            if (_elementsDirty)
            {
                _elementsDirty = false;
                BuildElements();
            }
            _keyboardSignature = 0;
            ShowPosition(force: true);
        }
    }
}
