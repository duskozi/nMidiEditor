using Melanchall.DryWetMidi.Core;
using nMIDI.Controls.Skinning;
using System;
using System.Windows;
using System.Windows.Input;

namespace nMIDI
{
    /// <summary>
    /// MIDI activity: every channel of the MIDI output (the song, the mixer, clicked keys and the thru of the input)
    /// with a note LED, a control LED, a live note map and the last control message, and the MIDI input as a row of
    /// LEDs. The monitors take messages from any thread and draw them once per frame.
    /// </summary>
    public partial class MidiMonitorWindow : Window
    {
        private readonly MainWindow _main;

        public MidiMonitorWindow(MainWindow main)
        {
            _main = main;
            InitializeComponent();
            Owner = main;
            ApplyTheme();
            ThemeManager.Changed += ApplyTheme;
            Closed += (_, _) => ThemeManager.Changed -= ApplyTheme;
            PreviewKeyDown += (s, e) => _main.OnPreviewKeyDown(s, e);
            UpdatePorts();
        }

        private static readonly (object SkinKey, string ThemeKey)[] ThemedSkin =
        {
            (DawSkinKeys.PanelBackground, "Bg1"),
            (DawSkinKeys.PanelRaised, "Bg2"),
            (DawSkinKeys.PanelBorder, "Line"),
            (DawSkinKeys.Text, "Text"),
            (DawSkinKeys.ScaleText, "TextDim"),
            (DawSkinKeys.Accent, "Accent"),
            (DawSkinKeys.LcdBackground, "LcdBg"),
            (DawSkinKeys.LcdText, "LcdText"),
        };

        private void ApplyTheme()
        {
            foreach (var (skinKey, themeKey) in ThemedSkin)
                if (Application.Current.TryFindResource(themeKey) is { } brush) Resources[skinKey] = brush;
        }

        /// <summary>Show the port names (called when ports change).</summary>
        public void UpdatePorts()
        {
            txtOutPorts.Text = string.Join(", ", _main.OpenOutputNames);
            txtInPort.Text = _main.InputName ?? "No MIDI input – choose one in the track bar";
        }

        /// <summary>A message went out (any thread).</summary>
        public void ShowOut(ChannelEvent e) => Feed(monitorOut, e);

        /// <summary>A message came in on the MIDI input (any thread).</summary>
        public void ShowIn(ChannelEvent e) => Feed(monitorIn, e);

        private static void Feed(nMIDI.Controls.MidiActivityMonitor monitor, ChannelEvent e)
        {
            int ch = e.Channel;
            switch (e)
            {
                case NoteOnEvent on when on.Velocity > 0: monitor.NoteOn(ch, on.NoteNumber, on.Velocity); break;
                case NoteOnEvent on: monitor.NoteOff(ch, on.NoteNumber); break;
                case NoteOffEvent off: monitor.NoteOff(ch, off.NoteNumber); break;
                case ControlChangeEvent cc when (int)cc.ControlNumber is 120 or 123: monitor.AllNotesOff(ch); break;
                case ControlChangeEvent cc: monitor.ControlChange(ch, cc.ControlNumber, cc.ControlValue); break;
                case PitchBendEvent pb: monitor.PitchBend(ch, pb.PitchValue - 8192); break;
                case ProgramChangeEvent pc: monitor.ProgramChange(ch, pc.ProgramNumber); break;
                case ChannelAftertouchEvent at: monitor.ChannelPressure(ch, at.AftertouchValue); break;
                case NoteAftertouchEvent poly: monitor.PolyPressure(ch, poly.NoteNumber, poly.AftertouchValue); break;
            }
        }

        private void OnOptionChanged(object sender, RoutedEventArgs e)
        {
            monitorOut.ShowNoteMap = tglNoteMap.IsChecked == true;
            monitorOut.ShowControlText = tglControl.IsChecked == true;
        }

        private void OnClear(object sender, RoutedEventArgs e)
        {
            monitorOut.Reset();
            monitorIn.Reset();
        }
    }
}
