using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using System;
using System.Collections.Generic;

namespace nMIDI.Playback
{
    /// <summary>
    /// One MIDI input port at a time (a keyboard or controller). Channel events arrive on the
    /// device's own thread through <see cref="Received"/>; the handler must not touch the UI directly.
    /// </summary>
    public sealed class MidiInput : IDisposable
    {
        private InputDevice? _device;

        /// <summary>A channel event arrived (note, controller, pitch bend...).</summary>
        public event Action<ChannelEvent>? Received;

        /// <summary>Name of the open port, or null.</summary>
        public string? Name { get; private set; }

        /// <summary>The last error from opening a port, or null.</summary>
        public string? LastError { get; private set; }

        public static IReadOnlyList<string> GetNames()
        {
            var names = new List<string>();
            try
            {
                foreach (var device in InputDevice.GetAll())
                {
                    names.Add(device.Name);
                    device.Dispose();
                }
            }
            catch (Exception ex) when (ex is MidiDeviceException or InvalidOperationException) { }
            return names;
        }

        /// <summary>Open a port by name (null closes the current one). Returns false if it could not be opened.</summary>
        public bool Open(string? name)
        {
            Close();
            if (string.IsNullOrEmpty(name)) return true;
            try
            {
                var device = InputDevice.GetByName(name);
                device.EventReceived += OnEventReceived;
                device.StartEventsListening();
                _device = device;
                Name = name;
                LastError = null;
                return true;
            }
            catch (Exception ex) when (ex is MidiDeviceException or ArgumentException or InvalidOperationException)
            {
                LastError = $"Cannot open MIDI input '{name}': {ex.Message}";
                return false;
            }
        }

        private void OnEventReceived(object? sender, MidiEventReceivedEventArgs e)
        {
            if (e.Event is ChannelEvent channelEvent) Received?.Invoke(channelEvent);
        }

        public void Close()
        {
            var device = _device;
            _device = null;
            Name = null;
            if (device == null) return;
            device.EventReceived -= OnEventReceived;
            try
            {
                device.StopEventsListening();
                device.Dispose();
            }
            catch (MidiDeviceException) { }
        }

        public void Dispose() => Close();
    }
}
