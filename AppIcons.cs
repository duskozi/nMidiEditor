using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace nMIDI
{
    /// <summary>The window icons in Assets/, loaded once.</summary>
    internal static class AppIcons
    {
        private static readonly Dictionary<string, ImageSource> Cache = new();

        public static ImageSource App => Get("nMIDIEditor");
        public static ImageSource TrackLanes => Get("track-lanes");
        public static ImageSource EventList => Get("event-list");
        public static ImageSource FingeringReport => Get("fingering-report");
        public static ImageSource Dialog => Get("dialog");

        private static ImageSource Get(string name)
        {
            if (!Cache.TryGetValue(name, out var icon))
            {
                // A BitmapFrame keeps the .ico decoder, so the title bar and the taskbar each pick their best size.
                icon = BitmapFrame.Create(new Uri($"pack://application:,,,/Assets/{name}.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Cache[name] = icon;
            }
            return icon;
        }
    }
}
