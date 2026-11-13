using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace nMIDI
{
    /// <summary>Settings kept between sessions, in %AppData%\nMIDIEditor\settings.json.</summary>
    public sealed class AppSettings
    {
        public List<string> RecentFiles { get; set; } = new();
        public string? InputDevice { get; set; }
        public bool Thru { get; set; } = true;
        public bool CountIn { get; set; } = true;
        public bool ReplaceWhenRecording { get; set; }
        public bool QuantizeRecording { get; set; }

        // Appearance and MIDI
        public string Theme { get; set; } = "Studio";
        public string? OutputDevice { get; set; }

        // Layout
        public double KeyboardHeight { get; set; } = 160;
        public bool ShowKeyboard { get; set; } = true;
        public bool ShowPedals { get; set; } = true;
        public bool ShowPedalStrip { get; set; } = true;
        public int KeyLabels { get; set; }
        public int KeyRange { get; set; }
        public double LaneHeight { get; set; } = 120;
        public bool ShowLane { get; set; } = true;
        public bool ShowTracks { get; set; } = true;

        // Editor
        public string Grid { get; set; } = "1/16";
        public string NoteLength { get; set; } = "Last";
        public int Velocity { get; set; } = 100;
        public bool DimOthers { get; set; } = true;
        public bool Follow { get; set; } = true;
        public int ColorMode { get; set; }

        // Fingering
        public bool FingerCharts { get; set; } = true;
        public bool FingerRepeats { get; set; } = true;
        /// <summary>0 = from the tracks when the file has them, 1 = from the tracks always, 2 = algorithm.</summary>
        public int FingerHands { get; set; }
        /// <summary>0 = off, 1 = edited track, 2 = all fingered tracks.</summary>
        public int FingersOnNotes { get; set; } = 1;
        public bool SaveFingering { get; set; } = true;

        // Files
        public int AutosaveMinutes { get; set; } = 1;
        public bool ReopenLast { get; set; }
        public int NewSongBpm { get; set; } = 120;

        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nMIDIEditor");
        private static string FilePath => Path.Combine(Folder, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
            return new AppSettings();
        }

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        /// <summary>Put a file at the top of the recent list (at most 12).</summary>
        public void AddRecent(string path)
        {
            RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            RecentFiles.Insert(0, path);
            if (RecentFiles.Count > 12) RecentFiles.RemoveRange(12, RecentFiles.Count - 12);
            Save();
        }
    }
}
