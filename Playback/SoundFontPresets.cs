using nMIDI.Model;
using System;
using System.IO;
using System.Linq;

namespace nMIDI.Playback
{
    /// <summary>
    /// Reads the preset names of a SoundFont into the instrument catalog (once per file and change). Only the preset
    /// list is read, not the samples, so this is quick and light even for SoundFonts of several gigabytes.
    /// Safe to call from any thread.
    /// </summary>
    public static class SoundFontPresets
    {
        private static readonly object Gate = new();
        private static string? _loaded;
        private static DateTime _stamp;

        /// <summary>Load the presets of the file, or clear them for null or a missing file. Returns an error, or null.</summary>
        public static string? Use(string? path)
        {
            lock (Gate)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    Clear();
                    return null;
                }
                try
                {
                    var stamp = File.GetLastWriteTimeUtc(path);
                    if (string.Equals(path, _loaded, StringComparison.OrdinalIgnoreCase) && stamp == _stamp) return null;
                    var presets = SoundFontHeader.ReadPresets(path);
                    InstrumentCatalog.SetSoundFont(Path.GetFileName(path), presets.Select(p => (p.Bank, p.Program, p.Name)));
                    _loaded = path;
                    _stamp = stamp;
                    return null;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    Clear();
                    return ex.Message;
                }
            }
        }

        private static void Clear()
        {
            InstrumentCatalog.SetSoundFont(null, []);
            _loaded = null;
        }
    }
}
