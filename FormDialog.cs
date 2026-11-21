using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace nMIDI
{
    /// <summary>
    /// A small dark dialog built in code: a title, an optional line of text, labelled fields
    /// (numbers, choices, check boxes, text) and OK / Cancel. Each Add method returns a function
    /// that reads the field after <see cref="Ask"/> returned true.
    /// </summary>
    public sealed class FormDialog : Window
    {
        private Grid _fields = NewFieldGrid();
        private readonly StackPanel _columns = new() { Orientation = Orientation.Horizontal };
        private readonly List<Func<bool>> _validators = new();
        private Control? _first;

        public FormDialog(Window owner, string title, string? intro = null)
        {
            Owner = owner;
            Title = title;
            Icon = AppIcons.Dialog;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowInTaskbar = false;
            Background = (Brush)Application.Current.FindResource("Bg1");
            Foreground = (Brush)Application.Current.FindResource("Text");
            FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
            FontSize = 12;
            UseLayoutRounding = true;


            var root = new StackPanel { Margin = new Thickness(18, 16, 18, 14), MinWidth = 340 };
            if (intro != null)
                root.Children.Add(new TextBlock
                {
                    Text = intro,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 420,
                    Foreground = (Brush)Application.Current.FindResource("TextDim"),
                    Margin = new Thickness(0, 0, 0, 12),
                });
            _columns.Children.Add(_fields);
            root.Children.Add(_columns);

            var buttonStyle = (Style)Application.Current.FindResource("ToolButton");
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0),
            };
            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Style = buttonStyle };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0), Style = buttonStyle };
            ok.Click += (_, _) =>
            {
                if (_validators.All(v => v())) DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;

            SourceInitialized += (_, _) => DarkTitleBar(this);
            Loaded += (_, _) =>
            {
                _first?.Focus();
                if (_first is TextBox box) box.SelectAll();
            };
        }

        private static Grid NewFieldGrid()
        {
            var grid = new Grid { VerticalAlignment = VerticalAlignment.Top };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 });
            return grid;
        }

        /// <summary>Start a new column of fields to the right (for long forms such as the settings).</summary>
        public void NextColumn()
        {
            _fields = NewFieldGrid();
            _fields.Margin = new Thickness(32, 0, 0, 0);
            _columns.Children.Add(_fields);
        }

        /// <summary>Show the dialog. True when the user pressed OK and every field was valid.</summary>
        public bool Ask() => ShowDialog() == true;

        private int AddRow(string label, UIElement field, string? hint)
        {
            int row = _fields.RowDefinitions.Count;
            _fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 14, 4) };
            Grid.SetRow(text, row);
            _fields.Children.Add(text);

            UIElement content = field;
            if (hint != null)
            {
                var panel = new DockPanel();
                var hintText = new TextBlock
                {
                    Text = hint,
                    Foreground = (Brush)Application.Current.FindResource("TextFaint"),
                    FontSize = 11,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                DockPanel.SetDock(hintText, Dock.Right);
                panel.Children.Add(hintText);
                panel.Children.Add(field);
                content = panel;
            }
            Grid.SetRow(content, row);
            Grid.SetColumn(content, 1);
            if (content is FrameworkElement fe) fe.Margin = new Thickness(0, 4, 0, 4);
            _fields.Children.Add(content);
            return row;
        }

        public Func<int> AddNumber(string label, int value, int min, int max, string? hint = null)
        {
            var box = new TextBox
            {
                Text = value.ToString(CultureInfo.InvariantCulture),
                Height = 26,
                Width = 90,
                HorizontalAlignment = HorizontalAlignment.Left,
                Style = (Style)Application.Current.FindResource("DarkTextBox"),
            };
            _first ??= box;
            AddRow(label, box, hint ?? $"{min} to {max}");
            int result = value;
            _validators.Add(() =>
            {
                string text = box.Text.Trim().Replace('−', '-');
                if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v) && v >= min && v <= max)
                {
                    result = v;
                    box.ClearValue(BorderBrushProperty);
                    return true;
                }
                box.BorderBrush = Brushes.IndianRed;
                box.Focus();
                box.SelectAll();
                return false;
            });
            return () => result;
        }

        public Func<string> AddText(string label, string value, string? hint = null)
        {
            var box = new TextBox { Text = value, Height = 26, MinWidth = 200, Style = (Style)Application.Current.FindResource("DarkTextBox") };
            _first ??= box;
            AddRow(label, box, hint);
            return () => box.Text;
        }

        /// <summary>A file path with a Browse button. With <paramref name="required"/>, OK needs an existing file.</summary>
        public Func<string> AddFile(string label, string value, string filter, bool required, string? hint = null)
        {
            var box = new TextBox { Text = value, Height = 26, Width = 300, Style = (Style)Application.Current.FindResource("DarkTextBox") };
            var browse = new Button
            {
                Content = "Browse…",
                Height = 26,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(6, 0, 0, 0),
                Style = (Style)Application.Current.FindResource("ToolButton"),
            };
            browse.Click += (_, _) =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter, Title = label };
                try
                {
                    if (System.IO.File.Exists(box.Text)) dialog.InitialDirectory = System.IO.Path.GetDirectoryName(box.Text);
                }
                catch (ArgumentException) { }
                if (dialog.ShowDialog(this) == true) box.Text = dialog.FileName;
            };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(box);
            panel.Children.Add(browse);
            _first ??= box;
            AddRow(label, panel, hint);
            if (required)
                _validators.Add(() =>
                {
                    if (System.IO.File.Exists(box.Text.Trim())) { box.ClearValue(BorderBrushProperty); return true; }
                    box.BorderBrush = Brushes.IndianRed;
                    box.Focus();
                    return false;
                });
            return () => box.Text.Trim();
        }

        public Func<T> AddChoice<T>(string label, IEnumerable<T> items, T selected, string? hint = null)
        {
            var combo = new ComboBox { Height = 26, MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left, MaxDropDownHeight = 360 };
            foreach (var item in items) combo.Items.Add(item!);
            combo.SelectedItem = selected;
            if (combo.SelectedIndex < 0 && combo.Items.Count > 0) combo.SelectedIndex = 0;
            _first ??= combo;
            AddRow(label, combo, hint);
            return () => (T)combo.SelectedItem!;
        }

        /// <summary>A button that shows a value and opens another dialog to change it; <paramref name="pick"/> returns the new text, or null.</summary>
        public void AddPicker(string label, string text, Func<string?> pick, string? hint = null)
        {
            var button = new Button
            {
                Content = text,
                Height = 26,
                MinWidth = 220,
                HorizontalAlignment = HorizontalAlignment.Left,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 0, 10, 0),
                Style = (Style)Application.Current.FindResource("ToolButton"),
            };
            button.Click += (_, _) =>
            {
                if (pick() is string chosen) button.Content = chosen;
            };
            _first ??= button;
            AddRow(label, button, hint);
        }

        public Func<bool> AddCheck(string label, bool value, string? hint = null)
        {
            var check = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center, Content = hint ?? "" };
            AddRow(label, check, null);
            return () => check.IsChecked == true;
        }

        /// <summary>A line of text across the dialog, to separate groups of fields.</summary>
        public void AddHeading(string text)
        {
            int row = _fields.RowDefinitions.Count;
            _fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new TextBlock
            {
                Text = text.ToUpperInvariant(),
                Style = (Style)Application.Current.FindResource("CapsLabel"),
                Margin = new Thickness(0, row == 0 ? 0 : 10, 0, 2),
            };
            Grid.SetRow(heading, row);
            Grid.SetColumnSpan(heading, 2);
            _fields.Children.Add(heading);
        }


        internal static void DarkTitleBar(Window window) => ThemeManager.TitleBar(window);
    }
}
