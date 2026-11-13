using nMIDI.PianoRoll;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace nMIDI
{
    /// <summary>Switches the colour and control theme while the app runs.</summary>
    public static class ThemeManager
    {
        /// <summary>The themes, by display name; the file in Themes/ has the name without spaces.</summary>
        public static readonly string[] Names = ["Studio", "Light classic", "Retro hardware", "High contrast"];

        public static string Current { get; private set; } = "Studio";

        /// <summary>True when the current theme is dark (for the windows' title bars).</summary>
        public static bool IsDark { get; private set; } = true;

        /// <summary>Raised after the theme changed, for things drawn in code.</summary>
        public static event Action? Changed;

        /// <summary>Use a theme. Unknown names (and the old "Classic") fall back to Studio.</summary>
        public static void Apply(string? name)
        {
            name = Names.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ?? Names[0];
            var merged = Application.Current.Resources.MergedDictionaries;
            var file = name.Replace(" ", "");
            var dictionary = new ResourceDictionary { Source = new Uri($"pack://application:,,,/nMIDIEditor;component/Themes/{file}.xaml", UriKind.Absolute) };
            int index = merged.ToList().FindIndex(d => d.Source != null && d.Source.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase)
                                                        && !d.Source.OriginalString.Contains("NinePatchWpf", StringComparison.OrdinalIgnoreCase));
            if (index >= 0) merged[index] = dictionary;
            else merged.Add(dictionary);
            Current = name;
            IsDark = Application.Current.TryFindResource("IsDarkTheme") is not bool dark || dark;

            // Colours drawn in code.
            PianoRollStyle.Reload();
            LaneBrushes.Reload();
            Lanes.LaneStyle.Reload();
            foreach (Window w in Application.Current.Windows) TitleBar(w);
            Changed?.Invoke();
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>Dark or light title bar to match the theme (Windows 10 2004+ and Windows 11).</summary>
        public static void TitleBar(Window window)
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero) return;
                int on = IsDark ? 1 : 0;
                _ = DwmSetWindowAttribute(handle, 20, ref on, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }

    /// <summary>Theme colours for things drawn in code.</summary>
    public static class ThemeColors
    {
        /// <summary>The theme's brush for <paramref name="key"/>, frozen, or <paramref name="fallback"/>.</summary>
        public static Brush Brush(string key, Brush fallback)
        {
            if (Application.Current?.TryFindResource(key) is not Brush b) return fallback;
            if (b.IsFrozen) return b;
            var clone = b.Clone();
            clone.Freeze();
            return clone;
        }
    }
}
