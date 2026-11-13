using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using nMIDI.Controls.Primitives;

namespace nMIDI.Controls;

/// <summary>
/// Compact numeric field used for tempo, transpose, quantize amounts, etc.
/// Drag up/down to change (Shift = fine), wheel, arrows; double-click to type a value
/// (Enter commits, Esc cancels); Ctrl+click resets.
/// The template may contain a <see cref="TextBox"/> named <c>PART_Editor</c>.
/// </summary>
[TemplatePart(Name = PartEditor, Type = typeof(TextBox))]
public class ValueBox : DawRangeControl
{
    /// <summary>Template part name of the text box used for typing a value.</summary>
    public const string PartEditor = "PART_Editor";

    private TextBox? _editor;

    static ValueBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ValueBox), new FrameworkPropertyMetadata(typeof(ValueBox)));
    }

    private static readonly DependencyPropertyKey IsEditingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsEditing), typeof(bool), typeof(ValueBox), new FrameworkPropertyMetadata(false));

    /// <summary>Identifies the <see cref="IsEditing"/> dependency property.</summary>
    public static readonly DependencyProperty IsEditingProperty = IsEditingPropertyKey.DependencyProperty;

    /// <summary>True while the text editor is shown.</summary>
    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        private set => SetValue(IsEditingPropertyKey, value);
    }

    /// <summary>Identifies the <see cref="Unit"/> dependency property.</summary>
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(ValueBox), new FrameworkPropertyMetadata(null));

    /// <summary>Optional unit caption drawn after the value (e.g. "BPM").</summary>
    public string? Unit
    {
        get => (string?)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    /// <inheritdoc/>
    public override void OnApplyTemplate()
    {
        if (_editor is not null)
        {
            _editor.KeyDown -= OnEditorKeyDown;
            _editor.LostKeyboardFocus -= OnEditorLostFocus;
        }
        base.OnApplyTemplate();
        _editor = GetTemplateChild(PartEditor) as TextBox;
        if (_editor is not null)
        {
            _editor.KeyDown += OnEditorKeyDown;
            _editor.LostKeyboardFocus += OnEditorLostFocus;
        }
        IsEditing = false;
    }

    /// <summary>Double-click opens the editor; Ctrl+click still resets.</summary>
    protected override bool OnResetGesture()
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 && _editor is not null)
        {
            BeginEdit();
            return true;
        }
        return base.OnResetGesture();
    }

    /// <summary>Opens the text editor (also on double-click or Enter).</summary>
    public void BeginEdit()
    {
        if (_editor is null || IsEditing || !IsEnabled) return;
        IsEditing = true;
        _editor.Text = Value.ToString("0.###", CultureInfo.CurrentCulture);
        _editStartText = _editor.Text;
        // Editor visibility is driven by a template trigger on IsEditing; focus after layout.
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsEditing || _editor is null) return;
            _editor.Focus();
            _editor.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Closes the text editor and applies the typed value, if it changed and parses.</summary>
    public void CommitEdit()
    {
        if (!IsEditing || _editor is null) return;
        // Text left as shown: keep the exact value (the display is rounded to three decimals).
        if (_editor.Text != _editStartText && TryParse(_editor.Text, out double v))
            SetCurrentValue(ValueProperty, Quantize(v));
        EndEdit();
    }

    private string? _editStartText;

    /// <summary>Closes the text editor without changing the value.</summary>
    public void CancelEdit()
    {
        if (IsEditing) EndEdit();
    }

    private void EndEdit()
    {
        IsEditing = false;
        if (IsKeyboardFocusWithin) Focus();
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitEdit();
                e.Handled = true;
                break;
            case Key.Escape:
                CancelEdit();
                e.Handled = true;
                break;
        }
    }

    private void OnEditorLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitEdit();

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (IsEditing) return; // arrows/Delete belong to the TextBox while editing
        if (e.Key is Key.Enter or Key.F2)
        {
            BeginEdit();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (IsEditing) return;
        base.OnMouseLeftButtonDown(e);
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (IsEditing) return;
        base.OnMouseWheel(e);
    }

    /// <summary>
    /// Parses user text, tolerating units ("120 bpm") and either decimal separator: "1.5", "1,5", ".5", "-,5".
    /// When both separators appear ("1,234.5" or "1.234,5") the last one is the decimal point and the other
    /// groups thousands. Text with the same separator twice ("1.2.3") is rejected.
    /// </summary>
    public static bool TryParse(string? text, out double value) => NumberText.TryParse(text, out value);
}
