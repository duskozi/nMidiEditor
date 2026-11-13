using System.Windows;
using System.Windows.Controls;

namespace nMIDI.Controls;

/// <summary>
/// A titled, skinned panel that can be hidden by the user (× in the header) or by binding <see cref="IsShown"/>.
/// When hidden the panel collapses, so every control inside reports IsVisible = false and the library's
/// controls stop processing data (activity monitors, meters, lanes). Put extra header tools in <see cref="Tools"/>.
/// </summary>
public class DawPanel : HeaderedContentControl
{
    static DawPanel()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DawPanel), new FrameworkPropertyMetadata(typeof(DawPanel)));
        FocusableProperty.OverrideMetadata(typeof(DawPanel), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Identifies the <see cref="IsShown"/> dependency property.</summary>
    public static readonly DependencyProperty IsShownProperty = DependencyProperty.Register(
        nameof(IsShown), typeof(bool), typeof(DawPanel),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>False = collapsed (no layout, no rendering, no data).</summary>
    public bool IsShown { get => (bool)GetValue(IsShownProperty); set => SetValue(IsShownProperty, value); }

    /// <summary>Identifies the <see cref="CanHide"/> dependency property.</summary>
    public static readonly DependencyProperty CanHideProperty = DependencyProperty.Register(
        nameof(CanHide), typeof(bool), typeof(DawPanel), new FrameworkPropertyMetadata(true));

    /// <summary>Show the hide (×) button in the header.</summary>
    public bool CanHide { get => (bool)GetValue(CanHideProperty); set => SetValue(CanHideProperty, value); }

    /// <summary>Identifies the <see cref="ShowHeader"/> dependency property.</summary>
    public static readonly DependencyProperty ShowHeaderProperty = DependencyProperty.Register(
        nameof(ShowHeader), typeof(bool), typeof(DawPanel), new FrameworkPropertyMetadata(true));

    /// <summary>Whether the title bar with the hide button is shown.</summary>
    public bool ShowHeader { get => (bool)GetValue(ShowHeaderProperty); set => SetValue(ShowHeaderProperty, value); }

    /// <summary>Identifies the <see cref="Tools"/> dependency property.</summary>
    public static readonly DependencyProperty ToolsProperty = DependencyProperty.Register(
        nameof(Tools), typeof(object), typeof(DawPanel), new FrameworkPropertyMetadata(null));

    /// <summary>Extra header content, right-aligned before the hide button (pickers, toggles).</summary>
    public object? Tools { get => GetValue(ToolsProperty); set => SetValue(ToolsProperty, value); }

    /// <summary>Hides the panel (used by the header button; also handy from code).</summary>
    public void Hide() => SetCurrentValue(IsShownProperty, false);

    /// <inheritdoc/>
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Hide") is Button hide)
        {
            hide.Click -= OnHideClick;
            hide.Click += OnHideClick;
        }
    }

    private void OnHideClick(object sender, RoutedEventArgs e) => Hide();
}
