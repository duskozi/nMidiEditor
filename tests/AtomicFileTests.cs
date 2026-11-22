using nMIDI.Model;
using System;
using System.IO;
using Xunit;

namespace nMIDI.Tests
{
    public sealed class AtomicFileTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "nMIDI-tests-" + Guid.NewGuid().ToString("N"));

        public AtomicFileTests() => Directory.CreateDirectory(_folder);

        public void Dispose()
        {
            try { Directory.Delete(_folder, recursive: true); }
            catch (IOException) { }
        }

        [Fact]
        public void WriteAllTextCreatesAndReplacesTheFile()
        {
            string path = Path.Combine(_folder, "settings.json");
            AtomicFile.WriteAllText(path, "first");
            AtomicFile.WriteAllText(path, "second");

            Assert.Equal("second", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(_folder));
        }

        [Fact]
        public void AFailedWriteKeepsTheOldFileAndLeavesNoTemporaryFile()
        {
            string path = Path.Combine(_folder, "song.mp3");
            File.WriteAllText(path, "old");

            Assert.Throws<OperationCanceledException>(() => AtomicFile.Write(path, temp =>
            {
                File.WriteAllText(temp, "half");
                throw new OperationCanceledException();
            }));

            Assert.Equal("old", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(_folder));
        }

        [Fact]
        public void TheTemporaryFileKeepsTheExtensionAndFolder()
        {
            string path = Path.Combine(_folder, "take.mp3");
            string temp = AtomicFile.TempPathFor(path);

            Assert.Equal(".mp3", Path.GetExtension(temp));
            Assert.Equal(_folder, Path.GetDirectoryName(temp));
            Assert.NotEqual(path, temp);
        }

        [Fact]
        public void SavingASongLeavesOnlyTheMidiFile()
        {
            var song = new Song(480);
            var track = new SongTrack("Piano", 0);
            track.SetNotes(new[] { new SongNote(60, 0, 0, 480, 100) });
            song.SetTracks(new[] { track });
            song.Rebuild();

            string path = Path.Combine(_folder, "song.mid");
            song.Save(path);
            song.Save(path);   // over an existing file

            Assert.Single(Directory.GetFiles(_folder));
            Assert.Equal(1, Song.Load(path).NoteCount);
        }
    }
}
