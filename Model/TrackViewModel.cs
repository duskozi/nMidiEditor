using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace nMIDI.Model
{
    /// <summary>One row of the track list.</summary>
    public sealed class TrackViewModel : INotifyPropertyChanged
    {
        private bool _isMuted;
        private bool _isSolo;
        private bool _showNotes = true;
        private bool _isAudible = true;
        private string? _outputDevice;
        private double _level;
        private double _laneHeight = DefaultLaneHeight;
        private bool _isActive;

        public const double DefaultLaneHeight = 64;
        public const double MinLaneHeight = 26;
        public const double MaxLaneHeight = 600;

        public TrackViewModel(SongTrack track, Color color)
        {
            Track = track;
            Color = color;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Brush = brush;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public SongTrack Track { get; }

        /// <summary>Volume, pan and effect sends of the track in the mixer window.</summary>
        public MixerChannel Mix { get; } = new();

        public int Index => Track.Index;
        public int Number => Track.Index + 1;
        public string Name => Track.Name;

        /// <summary>Instrument, shown next to the name when it differs from it.</summary>
        public string Instrument => Track.Instrument == Track.Name ? "" : Track.Instrument;

        public string ChannelText => Track.Channels.Count switch
        {
            0 => "",
            1 => $"Ch {Track.Channels[0] + 1}",
            _ => "Ch " + string.Join(",", Track.Channels.Take(3).Select(c => c + 1)) + (Track.Channels.Count > 3 ? "…" : ""),
        };

        public string NotesText => Track.Notes.Count == 1 ? "1 note" : $"{Track.Notes.Count:N0} notes";

        public Color Color { get; private set; }
        public Brush Brush { get; private set; }

        public void SetColor(Color color)
        {
            Color = color;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Brush = brush;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Brush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAudible)));   // lanes redraw
        }

        public bool IsMuted { get => _isMuted; set => Set(ref _isMuted, value); }
        public bool IsSolo { get => _isSolo; set => Set(ref _isSolo, value); }
        public bool ShowNotes { get => _showNotes; set => Set(ref _showNotes, value); }

        /// <summary>Heard: not muted, and soloed if any track is soloed. Set by the window.</summary>
        public bool IsAudible { get => _isAudible; set => Set(ref _isAudible, value); }

        public string? OutputDevice { get => _outputDevice; set => Set(ref _outputDevice, value); }

        /// <summary>Height of this track's lane in the track lanes window.</summary>
        public double LaneHeight
        {
            get => _laneHeight;
            set => Set(ref _laneHeight, Math.Clamp(value, MinLaneHeight, MaxLaneHeight));
        }

        /// <summary>The track being edited in the piano roll.</summary>
        public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

        private bool _isCoEdited;
        /// <summary>Edited together with the active track (Ctrl+click in the track list).</summary>
        public bool IsCoEdited { get => _isCoEdited; set => Set(ref _isCoEdited, value); }

        /// <summary>Tell the views that the track's name, notes or channels changed.</summary>
        public void Refresh()
        {
            foreach (var name in new[] { nameof(Name), nameof(Instrument), nameof(ChannelText), nameof(NotesText) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        /// <summary>Activity meter, 0–1.</summary>
        public double Level { get => _level; set => Set(ref _level, value); }

        /// <summary>Update <see cref="IsAudible"/> of every track from the mute and solo buttons.</summary>
        public static void UpdateAudible(IReadOnlyCollection<TrackViewModel> tracks)
        {
            bool anySolo = tracks.Any(t => t.IsSolo);
            foreach (var t in tracks)
                t.IsAudible = !t.IsMuted && (!anySolo || t.IsSolo);
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
