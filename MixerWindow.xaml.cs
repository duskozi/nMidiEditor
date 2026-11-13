using nMIDI.Controls.Skinning;
using nMIDI.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace nMIDI
{
    /// <summary>
    /// A mixer with one channel strip per track: record (edited track), mute, solo, pan, reverb, chorus,
    /// delay and brightness sends, a volume fader and an activity meter, plus a master fader. It works on
    /// the main window's tracks, so mute, solo and the edited track stay in sync with the other windows.
    /// <para>
    /// The controls show the song's value at the playhead until you move them; from then on the player
    /// sends the mixer value instead of the song's events for that controller.
    /// </para>
    /// </summary>
    public partial class MixerWindow : Window
    {
        private readonly MainWindow _main;
        private long _shownTick = long.MinValue;

        // The song's values of the mixer controllers per track, rebuilt when its controller list changes.
        private readonly Dictionary<SongTrack, (IReadOnlyList<SongCtrl> Source, Dictionary<int, (long[] Ticks, int[] Values)> ByCC)> _timelines = new();

        public MixerWindow(MainWindow main)
        {
            _main = main;
            InitializeComponent();
            Owner = main;
            strips.ItemsSource = main.Tracks;
            faderMaster.Value = Math.Max(-60, 40 * Math.Log10(Math.Max(1e-6, main.Player.MasterVolume)));
            ApplyTheme();
            ThemeManager.Changed += ApplyTheme;
            Closed += (_, _) => ThemeManager.Changed -= ApplyTheme;
            PreviewKeyDown += (s, e) => { if (Keyboard.FocusedElement is not System.Windows.Controls.TextBox) _main.OnPreviewKeyDown(s, e); };
            UpdateFrame();
        }

        // Skin colours that follow the app theme: panels, text, value displays and the accent. The knobs, faders,
        // buttons and meter backgrounds keep the dark Graphite look, like the controls of a hardware mixer.
        private static readonly (object SkinKey, string ThemeKey)[] ThemedSkin =
        {
            (DawSkinKeys.PanelBackground, "Bg1"),
            (DawSkinKeys.PanelRaised, "Bg2"),
            (DawSkinKeys.PanelBorder, "Line"),
            (DawSkinKeys.Text, "Text"),
            (DawSkinKeys.ScaleText, "TextDim"),
            (DawSkinKeys.ScaleTick, "Line"),
            (DawSkinKeys.KnobTrack, "Line"),
            (DawSkinKeys.Accent, "Accent"),
            (DawSkinKeys.Focus, "AccentSoft"),
            (DawSkinKeys.FaderFill, "AccentSoft"),
            (DawSkinKeys.FaderThumbLine, "Accent"),
            (DawSkinKeys.MeterSafe, "Accent"),
            (DawSkinKeys.LcdBackground, "Bg0"),
            (DawSkinKeys.LcdBorder, "Line"),
            (DawSkinKeys.LcdText, "Accent"),
        };

        /// <summary>Takes the current app theme's colours into the mixer's skin.</summary>
        private void ApplyTheme()
        {
            foreach (var (skinKey, themeKey) in ThemedSkin)
                if (Application.Current.TryFindResource(themeKey) is { } brush) Resources[skinKey] = brush;
        }

        /// <summary>Called by the main window every frame: follow the song's values at the playhead.</summary>
        public void UpdateFrame()
        {
            if (!IsVisible || WindowState == WindowState.Minimized) return;
            var song = _main.CurrentSong;
            txtSong.Text = song?.Name ?? "";
            txtTracks.Text = _main.Tracks.Count == 1 ? "1 track" : $"{_main.Tracks.Count} tracks";
            if (song == null) return;

            long tick = (long)_main.Player.PositionTicks;
            bool changed = false;
            foreach (var vm in _main.Tracks)
                changed |= EnsureTimeline(vm.Track);
            if (changed && _timelines.Count > _main.Tracks.Count * 2 + 8)
            {
                var keep = _main.Tracks.Select(t => t.Track).ToHashSet();
                foreach (var old in _timelines.Keys.Where(k => !keep.Contains(k)).ToList()) _timelines.Remove(old);
            }
            if (tick == _shownTick && !changed) return;
            _shownTick = tick;

            foreach (var vm in _main.Tracks)
            {
                var byCC = _timelines[vm.Track].ByCC;
                foreach (int cc in MixerChannel.Controllers)
                {
                    if (vm.Mix.IsOverridden(cc)) continue;
                    int value = byCC.TryGetValue(cc, out var line) ? At(line, tick, MixerChannel.Default(cc)) : MixerChannel.Default(cc);
                    vm.Mix.Follow(cc, value);
                }
            }
        }

        /// <summary>Rebuild a track's controller timelines if its controller list changed; true if it did.</summary>
        private bool EnsureTimeline(SongTrack track)
        {
            var controls = track.Controls;
            if (_timelines.TryGetValue(track, out var t) && ReferenceEquals(t.Source, controls)) return false;
            var byCC = new Dictionary<int, (long[], int[])>();
            foreach (int cc in MixerChannel.Controllers)
            {
                // The track's own channel first; other channels only when it has none of its own.
                var list = controls.Where(c => c.Kind == CtrlKind.Controller && c.Number == cc && c.Channel == track.Channel).ToList();
                if (list.Count == 0) list = controls.Where(c => c.Kind == CtrlKind.Controller && c.Number == cc).ToList();
                if (list.Count == 0) continue;
                list.Sort((a, b) => a.Tick.CompareTo(b.Tick));
                byCC[cc] = (list.Select(c => c.Tick).ToArray(), list.Select(c => c.Value).ToArray());
            }
            _timelines[track] = (controls, byCC);
            return true;
        }

        /// <summary>The value in force at <paramref name="tick"/> (the last event at or before it), or the default.</summary>
        private static int At((long[] Ticks, int[] Values) line, long tick, int fallback)
        {
            int i = Array.BinarySearch(line.Ticks, tick);
            if (i < 0) i = ~i - 1;
            else while (i + 1 < line.Ticks.Length && line.Ticks[i + 1] == tick) i++;
            return i >= 0 ? line.Values[i] : fallback;
        }

        private static TrackViewModel? TrackOf(object sender) => (sender as FrameworkElement)?.DataContext as TrackViewModel;

        // R and the name label: make this the edited (and recorded) track. The R button never toggles by itself.
        private void OnArmClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (TrackOf(sender) is { } vm) _main.SelectTrack(vm);
        }

        private void OnResetStrip(object sender, RoutedEventArgs e)
        {
            if (TrackOf(sender) is not { } vm) return;
            vm.Mix.Reset();
            _shownTick = long.MinValue;
            UpdateFrame();
        }

        private void OnResetAll(object sender, RoutedEventArgs e)
        {
            foreach (var vm in _main.Tracks) vm.Mix.Reset();
            faderMaster.Value = 0;
            tglMasterMute.IsChecked = false;
            _shownTick = long.MinValue;
            UpdateFrame();
        }

        private void OnClearMuteSolo(object sender, RoutedEventArgs e)
        {
            foreach (var vm in _main.Tracks)
            {
                vm.IsMuted = false;
                vm.IsSolo = false;
            }
        }

        private void OnMuteRightClick(object sender, MouseButtonEventArgs e) => _main.OnMuteRightClick(sender, e);
        private void OnSoloRightClick(object sender, MouseButtonEventArgs e) => _main.OnSoloRightClick(sender, e);

        private void OnMasterChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_main == null) return;   // during InitializeComponent
            if (tglMasterMute.IsChecked == true) tglMasterMute.IsChecked = false;
            _main.Player.MasterVolume = GainOf(e.NewValue);
        }

        private void OnMasterMuteClick(object sender, RoutedEventArgs e)
        {
            if (tglMasterMute.IsChecked == true)
            {
                _main.Player.MasterVolume = 0;
            }
            else
            {
                _main.Player.MasterVolume = GainOf(faderMaster.Value);
            }
        }

        /// <summary>Factor applied to CC 7 for a fader position in dB (the GM volume curve, 40·log10).</summary>
        private static double GainOf(double db) => db <= MixerChannel.MinDb + 0.01 ? 0 : Math.Pow(10, Math.Min(db, 0) / 40);
    }
}
