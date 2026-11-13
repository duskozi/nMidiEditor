using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using nMIDI.Model;
using nMIDI.Piano;
using System;
using System.Collections.Generic;
using System.Linq;

namespace nMIDI
{
    /// <summary>The pitch bend and modulation wheels next to the keyboard.</summary>
    public partial class MainWindow
    {
        private DateTime _wheelInputUntil;
        private (IReadOnlyList<SongCtrl>? Controls, int Channel, long[] BendTicks, int[] Bends, long[] ModTicks, int[] Mods) _wheelCache;

        private void InitWheels()
        {
            wheels.BendChanged += value => SendToEditedTrack(channel => new PitchBendEvent((ushort)value) { Channel = channel });
            wheels.ModChanged += value => SendToEditedTrack(channel => new ControlChangeEvent((SevenBitNumber)1, (SevenBitNumber)value) { Channel = channel });
        }

        private void SendToEditedTrack(Func<FourBitNumber, MidiEvent> make)
        {
            var vm = _activeVm ?? Tracks.FirstOrDefault();
            var device = _outputs.Get(vm?.OutputDevice ?? NoSongPort);
            if (device == null) return;
            try { device.SendEvent(make((FourBitNumber)(vm?.Track.Channel ?? 0))); }
            catch (MidiDeviceException) { }
        }

        /// <summary>Wheel moves from the MIDI input show on the wheels for a moment.</summary>
        private void ShowInputOnWheels(ChannelEvent e)
        {
            switch (e)
            {
                case PitchBendEvent pb: wheels.Bend = pb.PitchValue; break;
                case ControlChangeEvent cc when cc.ControlNumber == 1: wheels.Mod = cc.ControlValue; break;
                default: return;
            }
            _wheelInputUntil = DateTime.UtcNow.AddSeconds(1.5);
        }

        /// <summary>Every frame: the edited track's pitch bend and modulation at the playhead.</summary>
        private void UpdateWheels()
        {
            if (wheels.IsDragging || DateTime.UtcNow < _wheelInputUntil || wheels.Visibility != System.Windows.Visibility.Visible) return;
            var track = (_activeVm ?? Tracks.FirstOrDefault())?.Track;
            if (track == null) { wheels.Bend = WheelPanel.BendCentre; wheels.Mod = 0; return; }
            if (!ReferenceEquals(_wheelCache.Controls, track.Controls) || _wheelCache.Channel != track.Channel)
            {
                var bends = track.Controls.Where(c => c.Kind == CtrlKind.PitchBend && c.Channel == track.Channel).ToList();
                var mods = track.Controls.Where(c => c.Kind == CtrlKind.Controller && c.Number == 1 && c.Channel == track.Channel).ToList();
                _wheelCache = (track.Controls, track.Channel, bends.Select(c => c.Tick).ToArray(), bends.Select(c => c.Value).ToArray(),
                               mods.Select(c => c.Tick).ToArray(), mods.Select(c => c.Value).ToArray());
            }
            long tick = (long)_player.PositionTicks;
            wheels.Bend = ValueAt(_wheelCache.BendTicks, _wheelCache.Bends, tick, WheelPanel.BendCentre);
            wheels.Mod = ValueAt(_wheelCache.ModTicks, _wheelCache.Mods, tick, 0);
        }

        /// <summary>The last value at or before <paramref name="tick"/> (ticks are sorted).</summary>
        private static int ValueAt(long[] ticks, int[] values, long tick, int none)
        {
            int i = Array.BinarySearch(ticks, tick);
            if (i < 0) i = ~i - 1;
            else while (i + 1 < ticks.Length && ticks[i + 1] == tick) i++;
            return i >= 0 ? values[i] : none;
        }
    }
}
