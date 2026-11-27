using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace nMIDI.Playback
{
    /// <summary>
    /// Reads the preset list of a SoundFont (.sf2, also .sf3) without loading its samples. The preset headers sit in
    /// the small "phdr" chunk at the end of the file, so this reads a few kilobytes even from a 2 GB SoundFont, where
    /// loading the whole font (MeltySynth's <c>new SoundFont(path)</c>) would read every sample into memory.
    /// </summary>
    public static class SoundFontHeader
    {
        private const int PresetRecordSize = 38;   // char[20] name, WORD preset, WORD bank, WORD bag, DWORD ×3

        /// <summary>The presets of a SoundFont file: bank, program and name, in file order.</summary>
        /// <exception cref="InvalidDataException">The file is not a SoundFont, or its preset list is missing or damaged.</exception>
        public static IReadOnlyList<(int Bank, int Program, string Name)> ReadPresets(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096);
            return ReadPresets(stream);
        }

        public static IReadOnlyList<(int Bank, int Program, string Name)> ReadPresets(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

            if (ReadId(reader) != "RIFF") throw new InvalidDataException("Not a SoundFont file (no RIFF header).");
            long riffEnd = Math.Min(stream.Length, stream.Position + reader.ReadUInt32());
            if (ReadId(reader) != "sfbk") throw new InvalidDataException("Not a SoundFont file (not an sfbk RIFF).");

            // Top level: LIST INFO, LIST sdta (the samples: skipped), LIST pdta (the presets).
            while (stream.Position + 8 <= riffEnd)
            {
                string id = ReadId(reader);
                long size = reader.ReadUInt32();
                long next = stream.Position + size + (size & 1);
                if (id == "LIST" && size >= 4 && ReadId(reader) == "pdta")
                {
                    long listEnd = Math.Min(riffEnd, stream.Position - 4 + size);
                    while (stream.Position + 8 <= listEnd)
                    {
                        string sub = ReadId(reader);
                        long subSize = reader.ReadUInt32();
                        long subNext = stream.Position + subSize + (subSize & 1);
                        if (sub == "phdr") return ReadPresetHeaders(reader, subSize);
                        stream.Position = subNext;
                    }
                    break;
                }
                stream.Position = next;
            }
            throw new InvalidDataException("The SoundFont has no preset list (phdr).");
        }

        private static List<(int Bank, int Program, string Name)> ReadPresetHeaders(BinaryReader reader, long size)
        {
            if (size % PresetRecordSize != 0 || size < PresetRecordSize)
                throw new InvalidDataException("The SoundFont's preset list is damaged.");
            int count = (int)(size / PresetRecordSize) - 1;   // the last record is the "EOP" terminator
            var presets = new List<(int, int, string)>(count);
            for (int i = 0; i < count; i++)
            {
                var name = reader.ReadBytes(20);
                int program = reader.ReadUInt16();
                int bank = reader.ReadUInt16();
                reader.ReadBytes(PresetRecordSize - 24);   // bag index, library, genre, morphology
                presets.Add((bank, program, DecodeName(name)));
            }
            return presets;
        }

        private static string DecodeName(byte[] bytes)
        {
            int length = Array.IndexOf(bytes, (byte)0);
            if (length < 0) length = bytes.Length;
            var chars = new char[length];
            for (int i = 0; i < length; i++) chars[i] = bytes[i] is >= 32 and < 127 ? (char)bytes[i] : '?';
            return new string(chars).Trim();
        }

        private static string ReadId(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(4);
            if (bytes.Length < 4) throw new InvalidDataException("The SoundFont file is cut short.");
            return Encoding.ASCII.GetString(bytes);
        }
    }
}
