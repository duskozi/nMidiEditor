using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace nMIDI.Model
{
    /// <summary>
    /// A track's mixer settings: volume (CC 7), pan (CC 10), reverb (CC 91), chorus (CC 93), delay (CC 94)
    /// and brightness (CC 74). Each control shows the song's value at the playhead until the user moves it;
    /// from then on the mixer value replaces the song's events for that controller, until it is reset.
    /// </summary>
    public sealed class MixerChannel : INotifyPropertyChanged
    {
        public const int VolumeCC = 7, PanCC = 10, BrightnessCC = 74, ReverbCC = 91, ChorusCC = 93, DelayCC = 94;

        /// <summary>The controllers the mixer has, in strip order.</summary>
        public static readonly int[] Controllers = { VolumeCC, PanCC, ReverbCC, ChorusCC, DelayCC, BrightnessCC };

        /// <summary>Lowest fader position in dB; below it the volume is 0.</summary>
        public const double MinDb = -60;

        private readonly int[] _value = new int[128];
        private readonly bool[] _overridden = new bool[128];

        public MixerChannel()
        {
            foreach (int cc in Controllers) _value[cc] = Default(cc);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Raised when the user sets (value 0–127) or resets (value -1) a controller.</summary>
        public event Action<MixerChannel, int, int>? ControlChanged;

        /// <summary>GM power-on value of a controller.</summary>
        public static int Default(int cc) => cc switch { VolumeCC => 100, PanCC => 64, BrightnessCC => 64, ReverbCC => 40, _ => 0 };

        public int Get(int cc) => _value[cc];
        public bool IsOverridden(int cc) => _overridden[cc];

        /// <summary>True when any control is set by the mixer rather than the song.</summary>
        public bool HasOverrides => Array.IndexOf(_overridden, true) >= 0;

        /// <summary>Volume on the fader, in dB (GM curve: 40·log10(cc/127)).</summary>
        public double VolumeDb
        {
            get => ToDb(_value[VolumeCC]);
            set => SetByUser(VolumeCC, FromDb(value));
        }

        /// <summary>Pan, -1 (left) to 1 (right).</summary>
        public double Pan
        {
            get => Math.Clamp((_value[PanCC] - 64) / 63.0, -1, 1);
            set => SetByUser(PanCC, (int)Math.Round(64 + Math.Clamp(value, -1, 1) * 63));
        }

        public double Reverb { get => _value[ReverbCC]; set => SetByUser(ReverbCC, (int)Math.Round(value)); }
        public double Chorus { get => _value[ChorusCC]; set => SetByUser(ChorusCC, (int)Math.Round(value)); }
        public double Delay { get => _value[DelayCC]; set => SetByUser(DelayCC, (int)Math.Round(value)); }
        public double Brightness { get => _value[BrightnessCC]; set => SetByUser(BrightnessCC, (int)Math.Round(value)); }

        public static double ToDb(int cc) => cc <= 0 ? MinDb : Math.Max(MinDb, 40 * Math.Log10(cc / 127.0));
        public static int FromDb(double db) => db <= MinDb + 0.01 ? 0 : (int)Math.Round(127 * Math.Pow(10, Math.Min(db, 0) / 40));

        /// <summary>Show the song's value of a controller, unless the user has set it.</summary>
        public void Follow(int cc, int value)
        {
            if (_overridden[cc] || _value[cc] == value) return;
            _value[cc] = value;
            Raise(cc);
        }

        /// <summary>Give every control back to the song.</summary>
        public void Reset()
        {
            foreach (int cc in Controllers)
            {
                if (!_overridden[cc]) continue;
                _overridden[cc] = false;
                ControlChanged?.Invoke(this, cc, -1);
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasOverrides)));
        }

        private void SetByUser(int cc, int value)
        {
            value = Math.Clamp(value, 0, 127);
            if (value == _value[cc]) return;   // a binding writing back the value it just read
            bool was = HasOverrides;
            _value[cc] = value;
            _overridden[cc] = true;
            ControlChanged?.Invoke(this, cc, value);
            if (!was) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasOverrides)));
        }

        private void Raise(int cc)
        {
            string name = cc switch
            {
                VolumeCC => nameof(VolumeDb),
                PanCC => nameof(Pan),
                ReverbCC => nameof(Reverb),
                ChorusCC => nameof(Chorus),
                DelayCC => nameof(Delay),
                _ => nameof(Brightness),
            };
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
