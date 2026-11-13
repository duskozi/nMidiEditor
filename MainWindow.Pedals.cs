using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using nMIDI.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace nMIDI
{
    /// <summary>
    /// Piano pedals: a strip along the bottom of the piano roll (the pedal data of the edited tracks) and
    /// the pedals next to the keyboard, which go down while the song or the MIDI input presses them.
    /// </summary>
    public partial class MainWindow
    {
        private PedalTimeline? _pedalTimeline;
        private int _pedalSignature;
        private DateTime _pedalInputUntil;
        private readonly Dictionary<int, bool> _pedalInputHeld = new();

        private void InitPedals()
        {
            pedalOverlay.Viewer = viewer;
            pedals.PedalChanged += (pedal, value) =>
                SendToEditedTrack(channel => new ControlChangeEvent((SevenBitNumber)(int)pedal, (SevenBitNumber)value) { Channel = channel });
        }

        private void OnPedalsToggled(object sender, RoutedEventArgs e)
        {
            _settings.ShowPedals = tglPedals.IsChecked == true;
            UpdatePedalPanelVisibility();
        }

        private void UpdatePedalPanelVisibility()
        {
            if (pedals == null || tglPedals == null) return;
            pedals.Visibility = tglKeyboard.IsChecked == true && tglPedals.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// The tracks whose pedals are shown: the edited tracks, or, if none of them has pedal data (pedals are
        /// often on another track of a piano piece), every visible track that has.
        /// </summary>
        private List<SongTrack> PedalTracks()
        {
            static bool HasPedals(SongTrack t) => t.Controls.Any(c => c.Kind == CtrlKind.Controller && c.Number is 64 or 66 or 67);
            var edited = EditTracks.Where(HasPedals).ToList();
            if (edited.Count > 0) return edited;
            return Tracks.Where(vm => vm.ShowNotes && HasPedals(vm.Track)).Select(vm => vm.Track).ToList();
        }

        private int PedalSignature()
        {
            var hash = new HashCode();
            hash.Add(_activeVm);
            foreach (var t in EditTracks) hash.Add(t);
            foreach (var vm in Tracks) { hash.Add(RuntimeHelpers.GetHashCode(vm.Track.Controls)); hash.Add(vm.ShowNotes); }
            return hash.ToHashCode();
        }

        /// <summary>Rebuild the pedal data if the tracks or their controllers changed, and redraw the strip.</summary>
        private void RefreshPedals(bool force = true)
        {
            if (_song == null)
            {
                _pedalTimeline = null;
                pedalOverlay.Pedals = null;
                pedalOverlay.InvalidateVisual();
                return;
            }
            int signature = PedalSignature();
            if (!force && signature == _pedalSignature && _pedalTimeline != null) return;
            _pedalSignature = signature;
            _pedalTimeline = new PedalTimeline(PedalTracks());
            pedalOverlay.Pedals = _settings.ShowPedalStrip ? _pedalTimeline : null;
            pedalOverlay.SongEnd = _song.LengthTicks;
            pedalOverlay.InvalidateVisual();
        }

        /// <summary>Pedals pressed on the MIDI input show on the pedals for a moment.</summary>
        private void ShowInputOnPedals(ChannelEvent e)
        {
            if (e is not ControlChangeEvent cc || pedals.IsPressing) return;
            int number = cc.ControlNumber;
            if (number is not (64 or 66 or 67)) return;
            pedals[(Pedal)number] = cc.ControlValue;
            _pedalInputHeld[number] = cc.ControlValue > 0;
            // Held on the input: keep showing it; once all are up, go back to the song after a moment.
            _pedalInputUntil = _pedalInputHeld.Values.Any(h => h) ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(1.5);
        }

        /// <summary>Every frame: the pedals at the playhead.</summary>
        private void UpdatePedals()
        {
            RefreshPedals(force: false);
            if (pedals.Visibility != Visibility.Visible || pedals.IsPressing || DateTime.UtcNow < _pedalInputUntil) return;
            long tick = (long)_player.PositionTicks;
            foreach (var pedal in PedalTimeline.All)
                pedals[pedal] = _pedalTimeline?.ValueAt(pedal, tick) ?? 0;
        }
    }
}
