using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace XIVChat_Desktop.Controls;

public sealed class GameSymbolPicker : Button {
    internal static readonly FontFamily GameFont = new("ms-appx:///Resources/fonts/ffxiv.ttf#XIV AXIS Std ATK");
    // Assigned glyphs in the bundled font, within the Lodestone E000–E11F list.
    // https://jp.finalfantasyxiv.com/lodestone/character/52670623/blog/5654118/
    internal static readonly int[] Symbols = Ranges((0xe020, 0xe02b), (0xe031, 0xe035), (0xe037, 0xe044), (0xe048, 0xe04e),
        (0xe050, 0xe08a), (0xe08f, 0xe0c6), (0xe0d0, 0xe0db)).Concat(new[] { 0x300a, 0x300b }).ToArray();
    private static IEnumerable<int> Ranges(params (int First, int Last)[] ranges) => ranges.SelectMany(r => Enumerable.Range(r.First, r.Last - r.First + 1));
    private static readonly int[] Common = { 0xe031, 0xe032, 0xe033, 0xe034, 0xe035, 0xe037, 0xe038, 0xe03c, 0xe048, 0xe049, 0xe050, 0xe05b, 0xe05c, 0xe05d, 0xe06e, 0xe06f, 0x300a, 0x300b };
    private readonly Flyout picker = new() { Placement = FlyoutPlacementMode.Top };
    private readonly TextBlock title = new() { FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock hint = new() { FontSize = 12, Opacity = .65, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox category = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Grid grid = new() { RowSpacing = 4, ColumnSpacing = 4 };
    private TextBox? editor;
    private int selectionStart, selectionLength;
    private bool inserted;
    public GameSymbolPicker() {
        Name = "GameSymbolPicker";
        Content = new FontIcon { Glyph = "\ue76e", FontSize = 18 };
        Padding = new Thickness(8); MinWidth = 34;
        var panel = new StackPanel { Width = 300, Spacing = 10 };
        panel.Children.Add(title); panel.Children.Add(category);
        panel.Children.Add(new ScrollViewer { Content = grid, MaxHeight = 230, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(hint);
        for (int i = 0; i < 8; i++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        picker.Content = panel; Flyout = picker;
        category.SelectionChanged += (_, _) => Populate();
        picker.Opening += (_, _) => {
            if (editor == null) return;
            selectionStart = editor.SelectionStart; selectionLength = editor.SelectionLength; inserted = false;
            Localize();
        };
        picker.Closed += (_, _) => {
            if (!inserted || editor == null) return;
            editor.Focus(FocusState.Programmatic); editor.Select(selectionStart, 0);
        };
        Localize();
    }
    public void Attach(TextBox target) { editor = target; target.FontFamily = GameFont; }
    public void Localize() {
        var label = SetupText.T("游戏符号", "Game symbols");
        ToolTipService.SetToolTip(this, label); AutomationProperties.SetName(this, label); title.Text = label;
        hint.Text = SetupText.T("点击符号，插入到光标处", "Choose a symbol to insert at the cursor");
        int selected = Math.Max(0, category.SelectedIndex);
        category.Items.Clear(); category.Items.Add(SetupText.T("常用", "Common")); category.Items.Add(SetupText.T("全部符号", "All symbols"));
        category.SelectedIndex = selected;
        AutomationProperties.SetName(category, SetupText.T("符号分类", "Symbol category"));
    }
    private void Populate() {
        grid.Children.Clear(); grid.RowDefinitions.Clear();
        var items = category.SelectedIndex == 1 ? Symbols : Common;
        for (int i = 0; i < (items.Length + 7) / 8; i++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (int i = 0; i < items.Length; i++) {
            var symbol = char.ConvertFromUtf32(items[i]);
            var button = new Button { Content = new TextBlock { Text = symbol, FontFamily = GameFont, FontSize = 21 },
                Height = 34, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, Tag = symbol };
            var code = "U+" + items[i].ToString("X4"); ToolTipService.SetToolTip(button, code); AutomationProperties.SetName(button, code);
            button.Click += (_, _) => {
                if (!Insert(symbol)) return;
                picker.Hide();
            };
            Grid.SetRow(button, i / 8); Grid.SetColumn(button, i % 8); grid.Children.Add(button);
        }
    }
    internal bool Insert(string symbol) {
        if (editor == null || !editor.IsEnabled || editor.IsReadOnly) return false;
        if (selectionStart > editor.Text.Length || selectionStart + selectionLength > editor.Text.Length) return false;
        if (editor.MaxLength > 0 && editor.Text.Length - selectionLength + symbol.Length > editor.MaxLength) return false;
        editor.Select(selectionStart, selectionLength); editor.SelectedText = symbol;
        selectionStart += symbol.Length; selectionLength = 0; inserted = true;
        editor.Select(selectionStart, 0);
        return true;
    }
}
