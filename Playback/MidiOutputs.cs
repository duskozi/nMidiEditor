using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using System;
using System.Collections.Generic;
using System.Linq;

namespace nMIDI.Playback
{
    /// <summary>
    /// The MIDI output ports of the system. Each port is opened once, on first use, and shared
    /// by every track routed to it. Use from the UI thread.
    /// </summary>
    public sealed class MidiOutputs : IDisposable
    {
        private readonly Dictionary<string, OutputDevice> _open = new(StringComparer.Ordinal);

        public MidiOutputs() => Refresh();

        /// <summary>Names of the available output ports.</summary>
        public IReadOnlyList<string> Names { get; private set; } = Array.Empty<string>();

        /// <summary>The port chosen in the settings, used when it is available.</summary>
        public string? Preferred { get; set; }

        /// <summary>The port new tracks use: the preferred port, else the Windows GS synth, else the first port.</summary>
        public string? DefaultName =>
            (Preferred != null && Names.Contains(Preferred) ? Preferred : null)
            ?? Names.FirstOrDefault(n => n.Contains("GS Wavetable", StringComparison.OrdinalIgnoreCase))
            ?? (Names.Count > 0 ? Names[0] : null);

        /// <summary>A channel event was sent to one of the open ports (raised on the sending thread).</summary>
        public event Action<ChannelEvent>? Sent;

        /// <summary>Names of the ports that are open.</summary>
        public IReadOnlyList<string> OpenNames => _open.Keys.ToList();

        /// <summary>The last error from opening a port, or null.</summary>
        public string? LastError { get; private set; }

        public void Refresh()
        {
            var names = new List<string>();
            try
            {
                foreach (var device in OutputDevice.GetAll())
                {
                    using (device) names.Add(device.Name);
                }
            }
            catch (Exception ex) when (ex is MidiDeviceException or InvalidOperationException)
            {
                LastError = ex.Message;
            }
            Names = names;
        }

        /// <summary>The open port with this name, opening it if needed; null if it cannot be opened.</summary>
        public OutputDevice? Get(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_open.TryGetValue(name, out var device)) return device;

            device = null;
            try
            {
                device = OutputDevice.GetByName(name);
                device.PrepareForEventsSending();
                device.EventSent += OnEventSent;
                _open[name] = device;
                LastError = null;
                return device;
            }
            catch (Exception ex) when (ex is MidiDeviceException or ArgumentException or InvalidOperationException)
            {
                // Found but could not be prepared (in use by another program, driver error): release it.
                if (device != null)
                {
                    device.EventSent -= OnEventSent;
                    try { device.Dispose(); }
                    catch (MidiDeviceException) { }
                }
                LastError = $"Cannot open '{name}': {ex.Message}";
                return null;
            }
        }

        private void OnEventSent(object? sender, MidiEventSentEventArgs e)
        {
            if (e.Event is ChannelEvent channelEvent) Sent?.Invoke(channelEvent);
        }

        public void Dispose()
        {
            foreach (var device in _open.Values)
            {
                try { device.Dispose(); }
                catch (MidiDeviceException) { }
            }
            _open.Clear();
        }
    }
}
