using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace nMIDI.Controls.Primitives;

/// <summary>
/// Base for controls drawn in <see cref="UIElement.OnRender"/> (meters, keyboard, grids).
/// Lookless in the WPF sense: every colour is a brush property, set by the default style from the
/// active skin via DynamicResource, and overridable per instance.
/// </summary>
public abstract class DawRenderControl : Control
{
    private Typeface? _typeface;

    static DawRenderControl()
    {
        // Default (not a local value) so styles can still override it.
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(DawRenderControl), new FrameworkPropertyMetadata(true));
    }

    /// <summary>Helper to register a Brush property that redraws on change.</summary>
    protected static DependencyProperty RegisterBrush(string name, System.Type owner)
        => DependencyProperty.Register(name, typeof(Brush), owner,
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <inheritdoc/>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // Control's Background/Foreground/BorderBrush/Font* don't all carry AffectsRender; we need them to.
        if (e.Property == BackgroundProperty || e.Property == ForegroundProperty || e.Property == BorderBrushProperty)
        {
            InvalidateVisual();
        }
        else if (e.Property == FontFamilyProperty || e.Property == FontSizeProperty ||
                 e.Property == FontWeightProperty || e.Property == FontStyleProperty || e.Property == FontStretchProperty)
        {
            _typeface = null;
            _texts.Clear();
            InvalidateVisual();
        }
    }

    /// <summary>Redraw at the new monitor's DPI (snapped lines, cached text and bitmaps depend on it).</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _texts.Clear();
        InvalidateVisual();
    }

    /// <summary>The control's font as a typeface.</summary>
    protected Typeface Typeface => _typeface ??= new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);

    private string? _autoToolTip;

    /// <summary>
    /// Show a tooltip the control writes itself (hovered lane, pedal state). A tooltip the app set, locally or
    /// in a style, is never replaced.
    /// </summary>
    protected void SetAutoToolTip(string? text)
    {
        var source = DependencyPropertyHelper.GetValueSource(this, ToolTipProperty).BaseValueSource;
        bool ours = source == BaseValueSource.Default || (source == BaseValueSource.Local && Equals(ReadLocalValue(ToolTipProperty), _autoToolTip));
        if (!ours) return;
        if (text is null) ClearValue(ToolTipProperty);
        else if (!Equals(ToolTip, text)) SetValue(ToolTipProperty, text);
        _autoToolTip = text;
    }

    private readonly System.Collections.Generic.Dictionary<(string, double, Brush?, double, double), FormattedText> _texts = new();

    /// <summary>
    /// Like <see cref="MakeText"/>, but cached per control, for labels redrawn every frame. With
    /// <paramref name="maxWidth"/> &gt; 0 the text is one line, trimmed with an ellipsis. Do not change the result.
    /// </summary>
    protected FormattedText CachedText(string text, double pixelsPerDip, Brush? brush = null, double? size = null, double maxWidth = 0)
    {
        brush ??= Foreground ?? Brushes.Gray;
        double em = size ?? FontSize;
        var key = (text, pixelsPerDip, brush, em, maxWidth);
        if (_texts.TryGetValue(key, out var ft)) return ft;
        ft = MakeText(text, pixelsPerDip, brush, em);
        if (maxWidth > 0)
        {
            ft.MaxTextWidth = Math.Max(1, maxWidth);
            ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;
        }
        if (_texts.Count > 512) _texts.Clear();
        _texts[key] = ft;
        return ft;
    }

    /// <summary>Text in the control's font, ready to draw.</summary>
    protected FormattedText MakeText(string text, double pixelsPerDip, Brush? brush = null, double? size = null)
        => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface,
            size ?? FontSize, brush ?? Foreground ?? Brushes.Gray, pixelsPerDip);
}
