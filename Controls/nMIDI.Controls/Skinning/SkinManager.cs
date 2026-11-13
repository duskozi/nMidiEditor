using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace nMIDI.Controls.Skinning;

/// <summary>Built-in skins shipped with the library.</summary>
public enum DawSkin
{
    /// <summary>Dark graphite with an orange accent; the only built-in skin of nMIDI.Controls.</summary>
    Graphite,
}

/// <summary>
/// Applies skins at runtime. Skins are plain <see cref="ResourceDictionary"/>s that define every
/// <see cref="DawSkinKeys"/> key; all control templates use DynamicResource, so switching is live.
/// <para>App-wide: <c>SkinManager.Apply(DawSkin.Graphite)</c>.</para>
/// <para>Scoped (one panel only): <c>&lt;Border nc:SkinManager.Skin="Graphite"&gt;</c>.</para>
/// <para>Custom: <c>SkinManager.Apply(new Uri("pack://application:,,,/MyApp;component/MySkin.xaml"))</c>.</para>
/// If no skin is applied the theme fallback (Graphite) is used automatically.
/// </summary>
public static class SkinManager
{
    // Tracks which merged dictionary in a target is the skin we inserted, so we replace instead of stacking.
    private static readonly ConditionalWeakTable<ResourceDictionary, ResourceDictionary> s_current = new();

    /// <summary>Raised after an app-wide skin change.</summary>
    public static event EventHandler? SkinChanged;

    /// <summary>The last built-in skin applied app-wide (null if a custom dictionary was applied).</summary>
    public static DawSkin? CurrentSkin { get; private set; } = DawSkin.Graphite;

    /// <summary>Pack URI of a skin's resource dictionary.</summary>
    public static Uri GetSkinUri(DawSkin skin)
        => new($"pack://application:,,,/nMIDI.Controls;component/Skins/{skin}.xaml", UriKind.Absolute);

    /// <summary>A new resource dictionary with the skin's resources.</summary>
    public static ResourceDictionary Load(DawSkin skin) => new() { Source = GetSkinUri(skin) };

    /// <summary>Applies a built-in skin to <paramref name="target"/> (defaults to Application resources).</summary>
    public static void Apply(DawSkin skin, ResourceDictionary? target = null)
    {
        ApplyCore(Load(skin), target);
        if (target is null)
        {
            CurrentSkin = skin;
            SkinChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Applies a skin dictionary from a URI.</summary>
    public static void Apply(Uri skinSource, ResourceDictionary? target = null)
        => Apply(new ResourceDictionary { Source = skinSource }, target);

    /// <summary>Applies a custom skin dictionary.</summary>
    public static void Apply(ResourceDictionary skin, ResourceDictionary? target = null)
    {
        ArgumentNullException.ThrowIfNull(skin);
        ApplyCore(skin, target);
        if (target is null)
        {
            CurrentSkin = null;
            SkinChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    private static void ApplyCore(ResourceDictionary skin, ResourceDictionary? target)
    {
        target ??= Application.Current?.Resources
            ?? throw new InvalidOperationException("No Application is running; pass an explicit target dictionary.");

        if (s_current.TryGetValue(target, out ResourceDictionary? previous))
        {
            int index = target.MergedDictionaries.IndexOf(previous);
            if (index >= 0)
            {
                // Replace in place to keep merge order (and therefore precedence) stable.
                target.MergedDictionaries[index] = skin;
            }
            else
            {
                target.MergedDictionaries.Add(skin);
            }
            s_current.Remove(target);
        }
        else
        {
            target.MergedDictionaries.Add(skin);
        }
        s_current.Add(target, skin);
    }

    /// <summary>Returns the keys a dictionary is missing compared to the full key set (useful in tests for custom skins).</summary>
    public static ComponentResourceKey[] FindMissingKeys(ResourceDictionary skin)
        => DawSkinKeys.All.Where(k => !ContainsDeep(skin, k)).ToArray();

    private static bool ContainsDeep(ResourceDictionary dict, object key)
        => dict.Contains(key) || dict.MergedDictionaries.Any(m => ContainsDeep(m, key));

    #region Scoped skin attached property

    /// <summary>Identifies the Skin attached property (see <see cref="GetSkin"/>).</summary>
    public static readonly DependencyProperty SkinProperty = DependencyProperty.RegisterAttached(
        "Skin", typeof(DawSkin?), typeof(SkinManager), new PropertyMetadata(null, OnSkinPropertyChanged));

    /// <summary>The skin set on an element with the attached Skin property.</summary>
    public static DawSkin? GetSkin(FrameworkElement element) => (DawSkin?)element.GetValue(SkinProperty);

    /// <summary>Skins one element subtree independently of the application skin.</summary>
    public static void SetSkin(FrameworkElement element, DawSkin? value) => element.SetValue(SkinProperty, value);

    private static void OnSkinPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        ResourceDictionary resources = fe.Resources;
        if (e.NewValue is DawSkin skin)
        {
            ApplyCore(Load(skin), resources);
        }
        else if (s_current.TryGetValue(resources, out ResourceDictionary? previous))
        {
            resources.MergedDictionaries.Remove(previous);
            s_current.Remove(resources);
        }
    }

    #endregion
}
