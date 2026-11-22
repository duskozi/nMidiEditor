using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace nMIDI
{
    /// <summary>
    /// A small error log in %AppData%\nMIDIEditor\logs\nMIDIEditor.log, for errors that would otherwise be lost
    /// (unhandled exceptions, failures on background threads). Safe to call from any thread; never throws.
    /// </summary>
    public static class AppLog
    {
        private const long MaxBytes = 1024 * 1024;   // start again (keeping one old file) after 1 MB
        private static readonly object Gate = new();

        public static string Folder => Path.Combine(AppSettings.Folder, "logs");
        public static string FilePath => Path.Combine(Folder, "nMIDIEditor.log");

        /// <summary>Log an exception with the place it happened.</summary>
        public static void Error(string where, Exception ex) => Write("ERROR", $"{where}: {ex}");

        public static void Warning(string message) => Write("WARN", message);

        private static void Write(string level, string message)
        {
            try
            {
                var line = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                    .Append(" [").Append(level).Append("] ")
                    .Append(typeof(AppLog).Assembly.GetName().Version?.ToString(3) ?? "?")
                    .Append(' ').AppendLine(message)
                    .ToString();
                lock (Gate)
                {
                    Directory.CreateDirectory(Folder);
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > MaxBytes) File.Move(FilePath, FilePath + ".old", overwrite: true);
                    File.AppendAllText(FilePath, line);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Logging must never take the app down.
            }
        }
    }
}
