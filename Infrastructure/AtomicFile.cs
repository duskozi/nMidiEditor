using System;
using System.IO;
using System.Text;

namespace nMIDI
{
    /// <summary>
    /// Writes files so that a crash or a full disk in the middle of a write never leaves a half-written file:
    /// the content goes to a temporary file next to the target, which then replaces the target in one step.
    /// </summary>
    public static class AtomicFile
    {
        /// <summary>The temporary file used while writing <paramref name="path"/> (same folder, same extension).</summary>
        public static string TempPathFor(string path)
        {
            string full = Path.GetFullPath(path);
            string folder = Path.GetDirectoryName(full) ?? ".";
            // Keep the extension: some writers (the Media Foundation MP3 encoder) pick the container from it.
            return Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(full)}.{Guid.NewGuid():N}.tmp{Path.GetExtension(full)}");
        }

        /// <summary>Write text (UTF-8, no BOM) to a file atomically.</summary>
        public static void WriteAllText(string path, string contents) =>
            Write(path, temp => File.WriteAllText(temp, contents, new UTF8Encoding(false)));

        /// <summary>
        /// Let <paramref name="write"/> create the temporary file it is given, then move it over <paramref name="path"/>.
        /// If <paramref name="write"/> throws (or is cancelled), the temporary file is deleted and the target is left as it was.
        /// </summary>
        public static void Write(string path, Action<string> write)
        {
            ArgumentNullException.ThrowIfNull(write);
            string full = Path.GetFullPath(path);
            string temp = TempPathFor(full);
            try
            {
                write(temp);
                File.Move(temp, full, overwrite: true);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }

        /// <summary>Delete a file, ignoring a missing file and IO or permission errors.</summary>
        public static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
