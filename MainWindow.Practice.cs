using nMIDI.Model;
using nMIDI.Piano;
using nMIDI.Playback;
using nMIDI.Theory;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace nMIDI
{
    /// <summary>
    /// Practice mode: the same window with a practice bar in place of the editing tools. You play a part (a track,
    /// one hand or both) on the MIDI keyboard or the keyboard panel while the editor plays the rest. In wait mode the
    /// song stops at each note until you have played the right keys; otherwise you play along in time and get a
    /// score. With a loop range the speed can go up a little after each time through.
    /// </summary>
    public partial class MainWindow
    {
        private bool _practiceMode;
        private PracticeScript? _practiceScript;
        private PracticeSession? _practiceRun;
        private bool _practiceWaiting;
        private double _practiceLastTick = -1;
        private TimeSpan _practiceLastFrame;
        private string? _practiceLastRun;
        private int _practiceScoreSignature;
        private bool _fillingPracticeTracks;

        // Feedback on the keyboard: the last wrong key (red) and the last hit (green), for a moment.
        private int _flashKey = -1;
        private bool _flashWrong;
        private DateTime _flashUntil;

        // The edit layout, to restore when leaving practice mode.
        private GridLength _editLaneHeight;
        private double _editLaneMinHeight;
        private bool _editStepOn;

        private static readonly Color HitColor = Color.FromRgb(0x22, 0xC5, 0x5E);
        private static readonly Color WrongColor = Color.FromRgb(0xEF, 0x44, 0x44);

        private void OnModeEdit(object sender, RoutedEventArgs e) => SetPracticeMode(false);
        private void OnModePractice(object sender, RoutedEventArgs e) => SetPracticeMode(true);

        /// <summary>Switch between editing and practising.</summary>
        private void SetPracticeMode(bool practice)
        {
            tglModeEdit.IsChecked = !practice;
            tglModePractice.IsChecked = practice;
            if (practice == _practiceMode) return;
            _practiceMode = practice;
            _editor.Enabled = !practice;

            if (practice)
            {
                _editor.CancelDrag();
                _editStepOn = tglStep.IsChecked == true;
                tglStep.IsChecked = false;
                _editLaneHeight = rowLane.Height;
                _editLaneMinHeight = rowLane.MinHeight;
                rowLane.MinHeight = 0;
                rowLane.Height = new GridLength(0);
                if (tglKeyboard.IsChecked != true) tglKeyboard.IsChecked = true;
                if (rowKeyboard.Height.Value < 200) rowKeyboard.Height = new GridLength(220);

                tglHandLeft.IsChecked = _settings.PracticeHands == (int)PracticeHands.Left;
                tglHandRight.IsChecked = _settings.PracticeHands == (int)PracticeHands.Right;
                tglHandBoth.IsChecked = _settings.PracticeHands == (int)PracticeHands.Both;
                tglWait.IsChecked = _settings.PracticeWait;
                tglRamp.IsChecked = _settings.PracticeRamp;
                txtRampFrom.Text = _settings.PracticeRampFrom.ToString(CultureInfo.InvariantCulture);
                txtRampStep.Text = _settings.PracticeRampStep.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                rowLane.MinHeight = _editLaneMinHeight;
                rowLane.Height = _editLaneHeight;
                tglStep.IsChecked = _editStepOn;
            }

            var edit = practice ? Visibility.Collapsed : Visibility.Visible;
            bdTrackList.Visibility = edit;
            bdEditBar.Visibility = edit;
            pnlLane.Visibility = edit;
            splitLane.Visibility = edit;
            bdPracticeBar.Visibility = practice ? Visibility.Visible : Visibility.Collapsed;
            runLogoEditor.Text = practice ? " Practice" : " Editor";

            if (practice) RebuildPractice();
            else EndPractice();
            FallingForMode();
            _keyboardSignature = 0;
            txtStatus.Text = practice
                ? "Practice: choose the track and hand, then press Start (or Enter). F9 goes back to editing."
                : "Editing.";
        }

        /// <summary>Work out the part to play and give the player the rest of the song.</summary>
        private void RebuildPractice()
        {
            if (!_practiceMode) return;
            FillPracticeTracks();
            if (_song == null)
            {
                _practiceScript = null;
                _practiceRun = null;
                txtPracticeScore.Text = "Open a song to practise.";
                return;
            }

            var hands = (PracticeHands)_settings.PracticeHands;
            var tracks = EditTracks.Where(t => !t.IsDrums).ToList();
            var notes = new List<PracticeNote>();
            foreach (var track in tracks)
            {
                var fingers = track.Fingering;
                for (int i = 0; i < track.Notes.Count; i++)
                {
                    var f = i < fingers.Length ? fingers[i] : default;
                    notes.Add(new PracticeNote(track.Notes[i], track.Index, f.Hand, f.Finger));
                }
            }
            _practiceScript = new PracticeScript(notes, hands, chordWindow: Math.Max(1, _song.TicksPerQuarter / 16));
            _player.UpdateSong(_practiceScript.Accompaniment(_song));
            _practiceWaiting = false;
            NewPracticeRun((long)_player.PositionTicks);

            string part = hands switch { PracticeHands.Left => "left hand", PracticeHands.Right => "right hand", _ => "both hands" };
            txtStatus.Text = _practiceScript.Notes.Count == 0
                ? "Nothing to play in this part: choose another track or hand."
                : $"Practice: {string.Join(" + ", tracks.Select(t => t.Name))}, {part} — {_practiceScript.Notes.Count:N0} notes in {_practiceScript.Steps.Count:N0} steps.";
        }

        private void EndPractice()
        {
            _practiceScript = null;
            _practiceRun = null;
            _practiceWaiting = false;
            _flashKey = -1;
            if (_song != null) _player.UpdateSong(_song);
            UpdateTransport();
        }

        private void FillPracticeTracks()
        {
            _fillingPracticeTracks = true;
            cmbPracticeTrack.Items.Clear();
            foreach (var vm in Tracks.Where(t => !t.Track.IsDrums && t.Track.Notes.Count > 0))
                cmbPracticeTrack.Items.Add(new ComboBoxItem { Content = $"{vm.Index + 1}. {vm.Track.Name}", Tag = vm });
            foreach (ComboBoxItem item in cmbPracticeTrack.Items)
                if (item.Tag == _activeVm) cmbPracticeTrack.SelectedItem = item;
            _fillingPracticeTracks = false;
        }

        private void OnPracticeTrackChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_fillingPracticeTracks || cmbPracticeTrack.SelectedItem is not ComboBoxItem { Tag: TrackViewModel vm }) return;
            SetActiveTrack(vm);
            RebuildPractice();
        }

        private void OnPracticeHands(object sender, RoutedEventArgs e)
        {
            var hands = sender == tglHandLeft ? PracticeHands.Left : sender == tglHandRight ? PracticeHands.Right : PracticeHands.Both;
            tglHandLeft.IsChecked = hands == PracticeHands.Left;
            tglHandRight.IsChecked = hands == PracticeHands.Right;
            tglHandBoth.IsChecked = hands == PracticeHands.Both;
            _settings.PracticeHands = (int)hands;
            RebuildPractice();
        }

        private void OnPracticeWait(object sender, RoutedEventArgs e)
        {
            _settings.PracticeWait = tglWait.IsChecked == true;
            if (_practiceWaiting) { _practiceWaiting = false; }
            NewPracticeRun((long)_player.PositionTicks);
        }

        private void OnPracticeRamp(object sender, RoutedEventArgs e)
        {
            _settings.PracticeRamp = tglRamp.IsChecked == true;
            if (_settings.PracticeRamp && !(HasLoop && tglLoop.IsChecked == true))
                txtStatus.Text = "Speed up each loop works with a loop range: right-click the ruler to set one, and turn Loop on.";
        }

        private void OnPracticeStart(object sender, RoutedEventArgs e) => StartPractice();

        /// <summary>Start a run from the beginning (of the loop range, when the loop is on).</summary>
        private void StartPractice()
        {
            if (_song == null || _practiceScript == null) return;
            ReadRampSettings();
            bool loop = HasLoop && tglLoop.IsChecked == true;
            if (_settings.PracticeRamp && loop) sldSpeed.Value = Math.Clamp(_settings.PracticeRampFrom, 25, 100);
            long from = loop ? _loopStart : 0;
            _player.Pause();
            SeekTo(from);
            viewer.ScrollToX(from, 0.05);
            _practiceWaiting = false;
            NewPracticeRun(from);
            Play();
        }

        private void ReadRampSettings()
        {
            if (int.TryParse(txtRampFrom.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int from))
                _settings.PracticeRampFrom = Math.Clamp(from, 25, 100);
            if (int.TryParse(txtRampStep.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int step))
                _settings.PracticeRampStep = Math.Clamp(step, 1, 50);
            txtRampFrom.Text = _settings.PracticeRampFrom.ToString(CultureInfo.InvariantCulture);
            txtRampStep.Text = _settings.PracticeRampStep.ToString(CultureInfo.InvariantCulture);
        }

        private void NewPracticeRun(long fromTick)
        {
            if (_song == null || _practiceScript == null) { _practiceRun = null; return; }
            bool loop = HasLoop && tglLoop.IsChecked == true && fromTick < _loopEnd;
            _practiceRun = new PracticeSession(_practiceScript, wait: _settings.PracticeWait,
                                               window: Math.Max(1, _song.TicksPerQuarter / 4),
                                               fromTick: fromTick, toTick: loop ? _loopEnd : long.MaxValue);
            _practiceLastTick = fromTick;
            _practiceScoreSignature = 0;
            ShowPracticeScore();
        }

        /// <summary>Ticks per millisecond at the playhead, at the current speed (for the timing in the score).</summary>
        private double TicksPerMs()
        {
            if (_song == null) return 0;
            double bpm = _song.Timeline.BpmAt(_player.PositionTicks, _song.TicksPerQuarter) * _player.Speed;
            return bpm * _song.TicksPerQuarter / 60_000.0;
        }

        private void ShowPracticeScore()
        {
            var run = _practiceRun;
            if (run == null) { txtPracticeScore.Text = _practiceLastRun ?? ""; return; }
            int sig = HashCode.Combine(run.Steps, run.CleanSteps, run.WrongKeys, run.Hits, run.Misses, _practiceLastRun);
            if (sig == _practiceScoreSignature) return;
            _practiceScoreSignature = sig;
            string now = run.Steps + run.Hits + run.Misses + run.WrongKeys == 0 ? "" : run.Summary(TicksPerMs());
            txtPracticeScore.Text = _practiceLastRun != null && now.Length == 0 ? "Last run: " + _practiceLastRun
                                  : _practiceLastRun != null ? now + "   ·   last run: " + _practiceLastRun
                                  : now;
        }

        /// <summary>The end of a run: keep its score and say how it went.</summary>
        private void FinishPracticeRun(string how)
        {
            var run = _practiceRun;
            if (run == null || run.Steps + run.Hits + run.Misses == 0) return;
            if (!run.Wait) run.Advance(long.MaxValue / 4);
            _practiceLastRun = run.Summary(TicksPerMs());
            txtStatus.Text = $"{how} {_practiceLastRun}";
            _practiceRun = null;
            _practiceScoreSignature = 0;
            ShowPracticeScore();
        }

        private void OnPracticeSongEnd()
        {
            if (!_practiceMode) return;
            FinishPracticeRun("Done!");
        }

        /// <summary>A key went down on the MIDI input or the keyboard panel.</summary>
        private void PracticePress(int key)
        {
            if (!_practiceMode || _song == null) return;
            if (_practiceRun == null || _practiceRun.IsDone && _practiceRun.Wait) NewPracticeRun((long)_player.PositionTicks);
            var run = _practiceRun;
            if (run == null) return;
            var result = run.Press(key, (long)Math.Round(_player.PositionTicks));
            if (result == PressResult.Ignored) return;
            _flashKey = key;
            _flashWrong = result == PressResult.Wrong;
            _flashUntil = DateTime.UtcNow.AddMilliseconds(_flashWrong ? 350 : 180);
            _keyboardSignature = 0;

            // Wait mode: the step is complete, go on.
            if (run.Wait && _practiceWaiting && run.CurrentStep is var step && (step == null || step.Tick > _player.PositionTicks + 1))
            {
                _practiceWaiting = false;
                Play();
            }
            ShowPracticeScore();
        }

        /// <summary>Every frame: stop at the next step (wait mode), judge misses (play-along), follow loops and jumps.</summary>
        private void PracticeFrame()
        {
            if (!_practiceMode || _song == null) return;
            var now = TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond);
            double frameMs = _practiceLastFrame == TimeSpan.Zero ? 16 : Math.Clamp((now - _practiceLastFrame).TotalMilliseconds, 1, 100);
            _practiceLastFrame = now;

            bool playing = _player.State == PlayerState.Playing;
            double tick = _player.PositionTicks;
            double last = _practiceLastTick;
            _practiceLastTick = tick;

            if (playing && last >= 0)
            {
                bool loop = HasLoop && tglLoop.IsChecked == true;
                if (loop && tick < last - 1 && last >= _loopEnd - _song.TicksPerQuarter && tick <= _loopStart + _song.TicksPerQuarter)
                {
                    // Once through the loop: keep the score, speed up, go again.
                    FinishPracticeRun("Loop done:");
                    if (_settings.PracticeRamp)
                    {
                        double speed = Math.Min(100, Math.Round(sldSpeed.Value / 5) * 5 + _settings.PracticeRampStep);
                        sldSpeed.Value = speed;
                        if (speed >= 100) txtStatus.Text += "  Full speed!";
                    }
                    NewPracticeRun((long)tick);
                }
                else if (tick < last - _song.TicksPerQuarter || tick > last + _song.TicksPerQuarter * 8)
                    NewPracticeRun((long)tick);   // moved by hand
            }
            else if (!playing && Math.Abs(tick - last) > 1 && !_practiceWaiting)
                NewPracticeRun((long)tick);       // moved while stopped

            var run = _practiceRun;
            if (run == null) { ShowPracticeScore(); return; }

            if (run.Wait)
            {
                var step = run.CurrentStep;
                if (playing && step != null)
                {
                    // Stop just before the step (a frame ahead, so its accompaniment does not sound early).
                    double ticksPerFrame = TicksPerMs() * frameMs;
                    if (tick + ticksPerFrame * 1.5 >= step.Tick)
                    {
                        _player.Pause();
                        _player.Seek(Math.Max(0, step.Tick - 1));
                        _practiceLastTick = step.Tick - 1;
                        _practiceWaiting = true;
                        UpdateTransport();
                        ShowPosition(force: true);
                    }
                }
            }
            else if (playing) run.Advance((long)tick);

            ShowPracticeScore();
        }

        /// <summary>Practice keys on the keyboard panel: the keys to play next, hits and wrong keys.</summary>
        private long AddPracticeKeys(ref List<KeyHighlight>? keys, long signature)
        {
            var run = _practiceRun;
            var list = new List<KeyHighlight>();
            double tick = _player.PositionTicks;

            if (run is { Wait: true })
            {
                // The step being waited for pulses in the track colour (green once pressed); the next one is faint.
                double pulse = _practiceWaiting ? 0.7 + 0.3 * Math.Sin(Environment.TickCount64 / 160.0) : 1.0;
                if (run.CurrentStep is { } step)
                {
                    foreach (var n in step.Notes)
                    {
                        bool pressed = run.Pressed.Contains(n.Note.Number);
                        var color = pressed ? HitColor : TrackColor(n.Track);
                        list.Add(new KeyHighlight(n.Note.Number, color, pressed ? 1.0 : pulse, (int)PracticeScript.HandOf(n), n.Finger));
                    }
                    if (run.NextStep is { } next)
                        foreach (var n in next.Notes)
                            if (Array.IndexOf(step.Keys, n.Note.Number) < 0)
                                list.Add(new KeyHighlight(n.Note.Number, TrackColor(n.Track), 0.22, (int)PracticeScript.HandOf(n), n.Finger));
                }
                signature = signature * 31 + (long)(pulse * 20) + 7_000_000 + run.Current * 977 + run.Pressed.Count;
            }
            else if (run != null)
            {
                // Play-along: the notes coming up in the next beat get brighter as they come near.
                long beat = _song?.TicksPerBeat ?? 480;
                foreach (var n in run.Upcoming((long)tick, beat))
                {
                    double distance = Math.Max(0, n.Note.Start - tick);
                    double intensity = n.Note.Start <= tick ? 1.0 : 0.25 + 0.6 * (1 - distance / beat);
                    list.Add(new KeyHighlight(n.Note.Number, TrackColor(n.Track), intensity, (int)PracticeScript.HandOf(n), n.Finger));
                    signature = signature * 31 + n.Note.Number * 7 + (long)(intensity * 10);
                }
            }

            if (_flashKey >= 0 && DateTime.UtcNow < _flashUntil)
            {
                list.Add(new KeyHighlight(_flashKey, _flashWrong ? WrongColor : HitColor, 1.0));
                signature = signature * 31 + 9_000_000 + _flashKey * 2 + (_flashWrong ? 1 : 0);
            }

            if (list.Count == 0) return signature;
            keys ??= new List<KeyHighlight>();
            var mine = list.Select(k => k.Note).ToHashSet();
            keys.RemoveAll(k => mine.Contains(k.Note));
            // The keyboard keeps the first of equally bright entries for a key: the flash goes first so it wins.
            keys.AddRange(list.OrderByDescending(k => k.Note == _flashKey && (k.Color == WrongColor || k.Color == HitColor)));
            return signature;
        }

        private Color TrackColor(int track) =>
            track < Tracks.Count ? Tracks[track].Color : ((SolidColorBrush)FindResource("Accent")).Color;

        /// <summary>Keys in practice mode: Enter starts, F9 goes back to editing. Editing keys do nothing.</summary>
        private bool HandlePracticeKey(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    StartPractice();
                    return true;
                case Key.F9:
                    SetPracticeMode(false);
                    return true;
                default:
                    return false;
            }
        }
    }
}
