using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace XIVChat_Desktop.Controls {
    public sealed class HighlightedText : UserControl {
        private readonly TextBlock label = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, MaxLines = 8, TextTrimming = TextTrimming.CharacterEllipsis };
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(HighlightedText), new PropertyMetadata("", Changed));
        public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public static readonly DependencyProperty QueryProperty = DependencyProperty.Register(nameof(Query), typeof(string), typeof(HighlightedText), new PropertyMetadata("", Changed));
        public string Query { get => (string)GetValue(QueryProperty); set => SetValue(QueryProperty, value); }
        public HighlightedText() {
            Content = label;
        }
        private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((HighlightedText)sender).Update();
        private void Update() {
            label.Text = Text ?? ""; label.TextHighlighters.Clear();
            if (string.IsNullOrEmpty(Query) || string.IsNullOrEmpty(Text)) return;
            var highlighter = new TextHighlighter { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 240, 207, 115)), Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black) };
            for (int start = 0; start < Text.Length;) {
                var match = Text.IndexOf(Query, start, StringComparison.OrdinalIgnoreCase);
                if (match < 0) break;
                highlighter.Ranges.Add(new TextRange { StartIndex = match, Length = Query.Length }); start = match + Query.Length;
            }
            label.TextHighlighters.Add(highlighter);
        }
    }
}
