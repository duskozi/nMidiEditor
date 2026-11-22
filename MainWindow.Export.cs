using NAudio.MediaFoundation;
using NAudio.Wave;
using Microsoft.Win32;
using nMIDI.Playback;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace nMIDI
{
    /// <summary>
    /// Export the song as audio: it is played through a SoundFont (.sf2) synthesizer and saved as WAV or MP3.
    /// What sounds is what you hear: mute, solo and the mixer settings apply.
    /// </summary>
    public partial class MainWindow
    {
        private static readonly string[] AudioFormats = { "MP3, 192 kbit/s", "MP3, 320 kbit/s", "MP3, 128 kbit/s", "WAV, 16-bit" };

        private bool _exportingAudio;
        private CancellationTokenSource? _exportCancel;

        private async void ExportAudio()
        {
            if (_song == null || _exportingAudio) return;

            var d = new FormDialog(this, "Export audio",
                "The song is played through a SoundFont and saved as an audio file. Mute, solo and the mixer settings apply. " +
                "A free General MIDI SoundFont such as GeneralUser GS works well.");
            var soundFont = d.AddFile("SoundFont", _settings.SoundFontPath ?? "", "SoundFont (*.sf2)|*.sf2", required: true);
            var format = d.AddChoice("Format", AudioFormats, AudioFormats.Contains(_settings.AudioFormat) ? _settings.AudioFormat : AudioFormats[0]);
            string[] ranges = HasLoop ? new[] { "Whole song", "Loop range" } : new[] { "Whole song" };
            var range = d.AddChoice("Range", ranges, HasLoop && tglLoop.IsChecked == true ? ranges[1] : ranges[0]);
            if (!d.Ask()) return;

            _settings.SoundFontPath = soundFont();
            _settings.AudioFormat = format();
            bool mp3 = _settings.AudioFormat.StartsWith("MP3", StringComparison.Ordinal);
            int bitrate = mp3 ? int.Parse(_settings.AudioFormat.Split(' ')[1], CultureInfo.InvariantCulture) * 1000 : 0;
            bool loopOnly = range() == "Loop range";

            var save = new SaveFileDialog
            {
                Title = "Export audio",
                Filter = mp3 ? "MP3 audio (*.mp3)|*.mp3" : "WAV audio (*.wav)|*.wav",
                FileName = Path.GetFileNameWithoutExtension(_song.Name) + (loopOnly ? " (loop)" : "") + (mp3 ? ".mp3" : ".wav"),
                DefaultExt = mp3 ? ".mp3" : ".wav",
                AddExtension = true,
            };
            if (_song.Path != null) save.InitialDirectory = Path.GetDirectoryName(_song.Path);
            if (save.ShowDialog(this) != true) return;
            string path = save.FileName;

            // Everything the renderer needs, taken now: the song can be edited while it works.
            var playback = _song.Playback;
            var channels = _song.Tracks.Select(t => t.Channels).ToList();
            var overrides = new Dictionary<int, IReadOnlyDictionary<int, int>>();
            foreach (var vm in Tracks)
            {
                var set = Model.MixerChannel.Controllers.Where(vm.Mix.IsOverridden).ToDictionary(cc => cc, vm.Mix.Get);
                if (set.Count > 0) overrides[vm.Index] = set;
            }
            var options = new RenderOptions
            {
                SoundFontPath = _settings.SoundFontPath,
                AudibleTracks = Tracks.Where(vm => vm.IsAudible).Select(vm => vm.Index).ToHashSet(),
                MixerOverrides = overrides,
                StartTick = loopOnly ? _loopStart : 0,
                EndTick = loopOnly ? _loopEnd : -1,
            };

            _exportingAudio = true;
            _exportCancel = new CancellationTokenSource();
            var cancel = _exportCancel.Token;
            string name = Path.GetFileName(path);
            var progress = new Progress<double>(p => txtStatus.Text = $"Exporting {name}… {p * 100:0} %   (Esc cancels)");
            txtStatus.Text = $"Exporting {name}…";
            try
            {
                await Task.Run(() =>
                {
                    var audio = AudioRenderer.Render(playback, channels, options, progress, cancel);
                    cancel.ThrowIfCancellationRequested();
                    var pcm = audio.ToPcm16();
                    var wave = new WaveFormat(audio.SampleRate, 16, 2);
                    // Written to a temporary file first: a failed or cancelled export never destroys a file it would replace.
                    AtomicFile.Write(path, temp =>
                    {
                        if (mp3)
                        {
                            MediaFoundationApi.Startup();
                            using var source = new RawSourceWaveStream(new MemoryStream(pcm), wave);
                            MediaFoundationEncoder.EncodeToMp3(source, temp, bitrate);
                        }
                        else
                        {
                            using var writer = new WaveFileWriter(temp, wave);
                            writer.Write(pcm, 0, pcm.Length);
                        }
                        cancel.ThrowIfCancellationRequested();
                    });
                }, cancel);
                txtStatus.Text = $"Exported {name}.";
            }
            catch (OperationCanceledException)
            {
                txtStatus.Text = "Audio export cancelled.";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                AppLog.Error("Audio export", ex);
                txtStatus.Text = "Audio export failed.";
                string hint = mp3 ? "\n\nIf the MP3 encoder is missing (Windows N editions), install the Media Feature Pack or export as WAV." : "";
                MessageBox.Show(this, $"Could not export the audio:\n{ex.Message}{hint}", "nMIDI Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _exportingAudio = false;
                _exportCancel?.Dispose();
                _exportCancel = null;
            }
        }

        /// <summary>Esc while exporting: stop the export.</summary>
        private bool CancelAudioExport()
        {
            if (_exportCancel == null) return false;
            _exportCancel.Cancel();
            return true;
        }
    }
}
