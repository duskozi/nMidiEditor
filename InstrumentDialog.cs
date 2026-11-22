using nMIDI.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace nMIDI
{
    /// <summary>
    /// Choose a sound by bank and name instead of a number from 1 to 128: a bank (General MIDI, drum kits, the
    /// SoundFont's banks), its groups (the 16 GM families, kits) and the sounds, with a search over every bank.
    /// The bank select and program numbers can also be typed, for a device whose banks are not listed.
    /// </summary>
    public sealed class InstrumentDialog : Window
    {
        private readonly bool _drums;
        private readonly Action<InstrumentChoice>? _try;
        private readonly Func<string?>? _chooseSoundFont;
        private readonly ComboBox _bank = new() { Height = 26, MinWidth = 260, MaxDropDownHeight = 420 };
        private readonly TextBox _find;
        private readonly ListBox _groups;
        private readonly ListBox _patches;
        private readonly CheckBox _sendBank = new() { Content = "Bank select", VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _msb, _lsb, _program;
        private readonly TextBlock _name;
        private readonly TextBlock _hint;
        private bool _filling;

        public InstrumentChoice Result { get; private set; }

        public InstrumentDialog(Window owner, string title, bool drums, InstrumentChoice? current,
                                Action<InstrumentChoice>? tryIt, Func<string?>? chooseSoundFont)
        {
            _drums = drums;
            _try = tryIt;
            _chooseSoundFont = chooseSoundFont;
            Owner = owner;
            Title = title;
            Icon = AppIcons.Dialog;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowInTaskbar = false;
            Background = Res<Brush>("Bg1");
            Foreground = Res<Brush>("Text");
            FontFamily = Res<FontFamily>("UiFont");
            FontSize = 12;
            UseLayoutRounding = true;

            var textBox = Res<Style>("DarkTextBox");
            var buttonStyle = Res<Style>("ToolButton");
            _find = new TextBox { Height = 26, Width = 200, Style = textBox, ToolTip = "Find a sound by name in every bank" };
            _msb = Number(textBox, "Bank select MSB (CC 0), 0 to 127");
            _lsb = Number(textBox, "Bank select LSB (CC 32), 0 to 127");
            _program = Number(textBox, "Program, 1 to 128");
            _groups = List(180);
            _patches = List(330);
            _name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), FontWeight = FontWeights.SemiBold };
            _hint = new TextBlock { Foreground = Res<Brush>("TextFaint"), FontSize = 11, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 530 };

            var root = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };

            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var bankLabel = new TextBlock { Text = "Bank", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            top.Children.Add(bankLabel);
            top.Children.Add(_bank);
            if (chooseSoundFont != null)
            {
                var sf = new Button { Content = "SoundFont…", Height = 26, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(6, 0, 0, 0),
                                      Style = buttonStyle, ToolTip = "List the presets of a SoundFont (.sf2)" };
                sf.Click += (_, _) => OnChooseSoundFont();
                top.Children.Add(sf);
            }
            var findPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            findPanel.Children.Add(new TextBlock { Text = "Find", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) });
            findPanel.Children.Add(_find);
            DockPanel.SetDock(findPanel, Dock.Right);
            top.Children.Insert(0, findPanel);
            root.Children.Add(top);

            var lists = new StackPanel { Orientation = Orientation.Horizontal };
            lists.Children.Add(_groups);
            _patches.Margin = new Thickness(8, 0, 0, 0);
            lists.Children.Add(_patches);
            root.Children.Add(lists);
            root.Children.Add(_hint);

            var numbers = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            numbers.Children.Add(_sendBank);
            numbers.Children.Add(Caption("MSB", 10));
            numbers.Children.Add(_msb);
            numbers.Children.Add(Caption("LSB", 10));
            numbers.Children.Add(_lsb);
            numbers.Children.Add(Caption("Program", 18));
            numbers.Children.Add(_program);
            numbers.Children.Add(_name);
            root.Children.Add(numbers);

            var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
            if (tryIt != null)
            {
                var tryButton = new Button { Content = "▶ Try", MinWidth = 80, Style = buttonStyle, ToolTip = "Play a note with this sound (Space)" };
                tryButton.Click += (_, _) => TryIt();
                buttons.Children.Add(tryButton);
            }
            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Style = buttonStyle };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0), Style = buttonStyle };
            ok.Click += (_, _) => Accept();
            DockPanel.SetDock(cancel, Dock.Right);
            DockPanel.SetDock(ok, Dock.Right);
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            root.Children.Add(buttons);
            Content = root;

            _bank.SelectionChanged += (_, _) => { if (!_filling) FillGroups(); };
            _groups.SelectionChanged += (_, _) => { if (!_filling) FillPatches(); };
            _patches.SelectionChanged += (_, _) => { if (!_filling && _patches.SelectedItem is PatchItem p) Show(p.Patch, p.Bank); };
            _patches.MouseDoubleClick += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(_patches, d) is ListBoxItem) Accept();
            };
            _find.TextChanged += (_, _) => FillPatches();
            _sendBank.Checked += (_, _) => NumbersChanged();
            _sendBank.Unchecked += (_, _) => NumbersChanged();
            foreach (var box in new[] { _msb, _lsb, _program }) box.TextChanged += (_, _) => NumbersChanged();
            PreviewKeyDown += OnKey;

            SourceInitialized += (_, _) => FormDialog.DarkTitleBar(this);
            Loaded += (_, _) => { _patches.Focus(); (_patches.ItemContainerGenerator.ContainerFromItem(_patches.SelectedItem) as ListBoxItem)?.Focus(); };

            FillBanks();
            Select(current ?? new InstrumentChoice(null, null, 0));
        }

        /// <summary>Show the dialog. True when a sound was chosen; it is in <see cref="Result"/>.</summary>
        public bool Ask() => ShowDialog() == true;

        // --- Building ---

        private static T Res<T>(string key) => (T)Application.Current.FindResource(key);

        private static TextBox Number(Style style, string tip) =>
            new() { Height = 26, Width = 52, Style = style, ToolTip = tip, HorizontalContentAlignment = HorizontalAlignment.Center };

        private TextBlock Caption(string text, double left) =>
            new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, 6, 0), Foreground = Res<Brush>("TextDim") };

        private const string ItemStyle =
            "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListBoxItem'>" +
            "<Setter Property='Foreground' Value='{DynamicResource Text}'/>" +
            "<Setter Property='Padding' Value='8,3'/>" +
            "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'>" +
            "<Border x:Name='bd' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='Transparent' Padding='{TemplateBinding Padding}'>" +
            "<ContentPresenter/></Border>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='{DynamicResource Bg2}'/></Trigger>" +
            "<Trigger Property='IsSelected' Value='True'><Setter TargetName='bd' Property='Background' Value='{DynamicResource Accent}'/>" +
            "<Setter Property='Foreground' Value='White'/></Trigger>" +
            "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>";

        private static Style? _itemStyle;

        private ListBox List(double width) => new()
        {
            Width = width,
            Height = 340,
            Background = Res<Brush>("Bg0"),
            Foreground = Res<Brush>("Text"),
            BorderBrush = Res<Brush>("Line"),
            BorderThickness = new Thickness(1),
            ItemContainerStyle = _itemStyle ??= (Style)XamlReader.Parse(ItemStyle),
        };

        private sealed record PatchItem(Patch Patch, InstrumentBank Bank, bool ShowBank)
        {
            public override string ToString() =>
                ShowBank ? $"{Patch.Program + 1,3}  {Patch.Name}   ({Bank.Name})" : $"{Patch.Program + 1,3}  {Patch.Name}";
        }

        private const string AllGroups = "All";

        private void FillBanks()
        {
            _filling = true;
            _bank.Items.Clear();
            foreach (var b in InstrumentCatalog.Banks(_drums)) _bank.Items.Add(b);
            _filling = false;
        }

        private void FillGroups(string? select = null)
        {
            _filling = true;
            _groups.Items.Clear();
            if (_bank.SelectedItem is InstrumentBank bank)
            {
                var groups = bank.Groups;
                if (groups.Count > 1) _groups.Items.Add(AllGroups);
                foreach (var g in groups) _groups.Items.Add(g);
                _hint.Text = bank.Hint;
            }
            _groups.SelectedItem = select != null && _groups.Items.Contains(select) ? select : _groups.Items.Count > 0 ? _groups.Items[0] : null;
            _filling = false;
            FillPatches();
        }

        private void FillPatches()
        {
            var keep = (_patches.SelectedItem as PatchItem)?.Patch;
            _filling = true;
            _patches.Items.Clear();
            string find = _find.Text.Trim();
            if (find.Length > 0)
            {
                // Search every bank, the ones for this track first.
                foreach (var bank in InstrumentCatalog.Banks(_drums))
                    foreach (var p in bank.Patches.Where(p => p.Name.Contains(find, StringComparison.OrdinalIgnoreCase)))
                        _patches.Items.Add(new PatchItem(p, bank, true));
            }
            else if (_bank.SelectedItem is InstrumentBank bank)
            {
                string? group = _groups.SelectedItem as string;
                foreach (var p in bank.Patches.Where(p => group == null || group == AllGroups || p.Group == group))
                    _patches.Items.Add(new PatchItem(p, bank, false));
            }
            var again = _patches.Items.OfType<PatchItem>().FirstOrDefault(i => i.Patch == keep);
            if (again != null) _patches.SelectedItem = again;
            _filling = false;
            if (_patches.SelectedItem != null) _patches.ScrollIntoView(_patches.SelectedItem);
        }

        // --- Choosing ---

        /// <summary>Open the bank and group of a choice and select its sound when it is listed.</summary>
        private void Select(InstrumentChoice c)
        {
            int msb = c.Msb ?? 0, lsb = c.Lsb ?? 0;
            var banks = _bank.Items.OfType<InstrumentBank>().ToList();
            // The bank that lists this sound, preferring one of the track's kind.
            var found = banks.SelectMany(b => b.Patches.Select(p => (Bank: b, Patch: p)))
                             .Where(x => x.Patch.Program == c.Program && x.Bank.Drums == _drums &&
                                         (x.Patch.Msb == msb && x.Patch.Lsb == lsb || !c.HasBank && !x.Bank.SendsBank))
                             .Select(x => ((InstrumentBank, Patch)?)x).FirstOrDefault();
            _filling = true;
            _bank.SelectedItem = found?.Item1 ?? banks.FirstOrDefault();
            _filling = false;
            FillGroups(found?.Item2.Group);
            if (found is { } f)
            {
                _filling = true;
                _patches.SelectedItem = _patches.Items.OfType<PatchItem>().FirstOrDefault(i => i.Patch == f.Item2);
                _filling = false;
                if (_patches.SelectedItem != null) _patches.ScrollIntoView(_patches.SelectedItem);
            }
            SetNumbers(c);
        }

        private void Show(Patch p, InstrumentBank bank)
        {
            if (_find.Text.Trim().Length > 0 && !ReferenceEquals(_bank.SelectedItem, bank))
            {
                _filling = true;
                _bank.SelectedItem = bank;
                _filling = false;
                _hint.Text = bank.Hint;
            }
            bool send = bank.SendsBank || p.Msb != 0 || p.Lsb != 0;
            SetNumbers(new InstrumentChoice(send ? p.Msb : null, send ? p.Lsb : null, p.Program));
        }

        private void SetNumbers(InstrumentChoice c)
        {
            _filling = true;
            _sendBank.IsChecked = c.HasBank;
            _msb.Text = (c.Msb ?? 0).ToString(CultureInfo.InvariantCulture);
            _lsb.Text = (c.Lsb ?? 0).ToString(CultureInfo.InvariantCulture);
            _program.Text = (c.Program + 1).ToString(CultureInfo.InvariantCulture);
            _filling = false;
            NumbersChanged();
        }

        private static int? Parse(TextBox box, int min, int max) =>
            int.TryParse(box.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int v) && v >= min && v <= max ? v : null;

        /// <summary>The choice the number fields describe, or null (and the bad field marked) when one is out of range.</summary>
        private InstrumentChoice? Current(bool mark)
        {
            bool bank = _sendBank.IsChecked == true;
            int? msb = Parse(_msb, 0, 127), lsb = Parse(_lsb, 0, 127), program = Parse(_program, 1, 128);
            if (mark)
            {
                Mark(_msb, !bank || msb != null);
                Mark(_lsb, !bank || lsb != null);
                Mark(_program, program != null);
            }
            if (program == null || bank && (msb == null || lsb == null)) return null;
            return new InstrumentChoice(bank ? msb : null, bank ? lsb : null, program.Value - 1);
        }

        private static void Mark(TextBox box, bool ok)
        {
            if (ok) box.ClearValue(BorderBrushProperty);
            else box.BorderBrush = Brushes.IndianRed;
        }

        private void NumbersChanged()
        {
            _msb.IsEnabled = _lsb.IsEnabled = _sendBank.IsChecked == true;
            if (_filling) return;
            var c = Current(mark: true);
            _name.Text = c is { } v ? InstrumentCatalog.Name(v.Msb, v.Lsb, v.Program, _drums) : "";
        }

        private void TryIt()
        {
            if (Current(mark: true) is { } c) _try?.Invoke(c);
        }

        private void Accept()
        {
            if (Current(mark: true) is not { } c) return;
            Result = c;
            DialogResult = true;
        }

        private void OnChooseSoundFont()
        {
            string? error = _chooseSoundFont?.Invoke();
            var keep = Current(mark: false);
            FillBanks();
            if (keep is { } k) Select(k);
            if (error != null) _hint.Text = "The SoundFont could not be read: " + error;
            else if (InstrumentCatalog.SoundFontName is { } name)
            {
                var first = _bank.Items.OfType<InstrumentBank>().FirstOrDefault(b => b.Name.StartsWith("SoundFont", StringComparison.Ordinal) && b.Drums == _drums);
                if (first != null) _bank.SelectedItem = first;
                _hint.Text = $"{name}: {InstrumentCatalog.SoundFontBanks.Sum(b => b.Patches.Count)} presets.";
            }
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && e.OriginalSource is not TextBox)
            {
                TryIt();
                e.Handled = true;
            }
        }
    }
}
