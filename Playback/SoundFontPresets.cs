using MeltySynth;
using nMIDI.Model;
using System;
using System.IO;
using System.Linq;

namespace nMIDI.Playback
{
    /// <summary>Reads the preset names of a SoundFont into the instrument catalog (once per file and change).</summary>
    public static class SoundFontPresets
    {
        private static string? _loaded;
        private static DateTime _stamp;

        /// <summary>Load the presets of the file, or clear them for null or a missing file. Returns an error, or null.</summary>
        public static string? Use(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Clear();
                return null;
            }
            var stamp = File.GetLastWriteTimeUtc(path);
            if (string.Equals(path, _loaded, StringComparison.OrdinalIgnoreCase) && stamp == _stamp) return null;
            try
            {
                var font = new SoundFont(path);
                InstrumentCatalog.SetSoundFont(Path.GetFileName(path),
                    font.Presets.Select(p => (p.BankNumber, p.PatchNumber, p.Name)));
                _loaded = path;
                _stamp = stamp;
                return null;
            }
            catch (Exception ex)
            {
                Clear();
                return ex.Message;
            }
        }

        private static void Clear()
        {
            InstrumentCatalog.SetSoundFont(null, []);
            _loaded = null;
        }
    }
}
