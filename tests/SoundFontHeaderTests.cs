using nMIDI.Playback;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace nMIDI.Tests
{
    public class SoundFontHeaderTests
    {
        private static byte[] Chunk(string id, byte[] data)
        {
            var bytes = new List<byte>(Encoding.ASCII.GetBytes(id));
            bytes.AddRange(BitConverter.GetBytes((uint)data.Length));
            bytes.AddRange(data);
            if (data.Length % 2 == 1) bytes.Add(0);
            return bytes.ToArray();
        }

        private static byte[] List(string type, params byte[][] chunks) =>
            Chunk("LIST", Encoding.ASCII.GetBytes(type).Concat(chunks.SelectMany(c => c)).ToArray());

        private static byte[] Preset(string name, int program, int bank)
        {
            var record = new byte[38];
            Encoding.ASCII.GetBytes(name).CopyTo(record, 0);
            BitConverter.GetBytes((ushort)program).CopyTo(record, 20);
            BitConverter.GetBytes((ushort)bank).CopyTo(record, 22);
            return record;
        }

        /// <summary>A minimal SoundFont: INFO, a big sample chunk (which must be skipped, not read), and the presets.</summary>
        private static MemoryStream MakeFont(int sampleBytes = 100_001)
        {
            var phdr = Preset("Grand Piano", 0, 0).Concat(Preset("Strings", 48, 1)).Concat(Preset("Standard", 0, 128))
                                                  .Concat(Preset("EOP", 0, 0)).ToArray();
            var body = Encoding.ASCII.GetBytes("sfbk")
                .Concat(List("INFO", Chunk("ifil", new byte[4])))
                .Concat(List("sdta", Chunk("smpl", new byte[sampleBytes])))
                .Concat(List("pdta", Chunk("phdr", phdr), Chunk("pbag", new byte[8])))
                .ToArray();
            return new MemoryStream(Chunk("RIFF", body));
        }

        [Fact]
        public void ReadsEveryPresetButTheTerminator()
        {
            var presets = SoundFontHeader.ReadPresets(MakeFont());

            Assert.Equal(new[] { (0, 0, "Grand Piano"), (1, 48, "Strings"), (128, 0, "Standard") }, presets);
        }

        [Fact]
        public void SkipsTheSamplesInsteadOfReadingThem()
        {
            var font = new CountingStream(MakeFont(sampleBytes: 4_000_000));
            SoundFontHeader.ReadPresets(font);
            Assert.True(font.BytesRead < 10_000, $"read {font.BytesRead} bytes");
        }

        [Fact]
        public void RejectsAFileThatIsNotASoundFont()
        {
            var wave = new MemoryStream(Chunk("RIFF", Encoding.ASCII.GetBytes("WAVE").Concat(new byte[16]).ToArray()));
            Assert.Throws<InvalidDataException>(() => SoundFontHeader.ReadPresets(wave));
            Assert.Throws<InvalidDataException>(() => SoundFontHeader.ReadPresets(new MemoryStream(new byte[3])));
        }

        private sealed class CountingStream : Stream
        {
            private readonly Stream _inner;
            public CountingStream(Stream inner) => _inner = inner;
            public long BytesRead { get; private set; }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = _inner.Read(buffer, offset, count);
                BytesRead += n;
                return n;
            }
            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
