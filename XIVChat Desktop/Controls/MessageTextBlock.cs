using System;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using XIVChatCommon.Message.Server;

namespace XIVChat_Desktop.Controls {
    public class MessageTextBlock : SelectableTextBlock {
        public MessageTextBlock() {
            this.SetBinding(FontSizeProperty, new Binding {
                Path = new PropertyPath("Config.FontSize"),
                Source = (App)Application.Current,
            });
            this.FontFamily = new FontFamily("ms-appx:///Resources/fonts/ffxiv.ttf#XIV AXIS Std ATK");
            this.TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap;
            this.ActualThemeChanged += (_, _) => Render();
            this.Loaded += (_, _) => { ((App)Application.Current).Presentation.Changed += Render; ((App)Application.Current).Ai.TranslationChanged += TranslationChanged; Render(); };
            this.Unloaded += (_, _) => { ((App)Application.Current).Presentation.Changed -= Render; ((App)Application.Current).Ai.TranslationChanged -= TranslationChanged; };
            // Keep the standard copy commands and add translation when AI features are on.
            var flyout = new TextCommandBarFlyout();
            flyout.Opening += (_, _) => AddAiCommands(flyout);
            this.Inner.ContextFlyout = flyout;
        }

        public static readonly DependencyProperty FontSizeOverrideProperty = DependencyProperty.Register(
            nameof(FontSizeOverride), typeof(double), typeof(MessageTextBlock), new PropertyMetadata(0d, (d, e) => {
                var block = (MessageTextBlock)d;
                if ((double)e.NewValue is >= 8 and <= 48) block.FontSize = (double)e.NewValue;
                else block.SetBinding(FontSizeProperty, new Binding { Path = new PropertyPath("Config.FontSize"), Source = (App)Application.Current });
            }));
        public double FontSizeOverride { get => (double)GetValue(FontSizeOverrideProperty); set => SetValue(FontSizeOverrideProperty, value); }

        public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
            "Message",
            typeof(ServerMessage),
            typeof(MessageTextBlock),
            new PropertyMetadata(null, PropertyChanged)
        );

        public ServerMessage? Message {
            get => (ServerMessage)this.GetValue(MessageProperty);
            set => this.SetValue(MessageProperty, value);
        }

        public static readonly DependencyProperty ProcessMarkdownProperty = DependencyProperty.Register(
            "ProcessMarkdown",
            typeof(bool),
            typeof(MessageTextBlock),
            new PropertyMetadata(false, PropertyChanged)
        );

        public bool ProcessMarkdown {
            get => (bool)this.GetValue(ProcessMarkdownProperty);
            set => this.SetValue(ProcessMarkdownProperty, value);
        }

        public static readonly DependencyProperty ShowTimestampsProperty = DependencyProperty.Register(
            "ShowTimestamps",
            typeof(bool),
            typeof(MessageTextBlock),
            new PropertyMetadata(true, PropertyChanged)
        );

        public bool ShowTimestamps {
            get => (bool)this.GetValue(ShowTimestampsProperty);
            set => this.SetValue(ShowTimestampsProperty, value);
        }

        private void TranslationChanged(ServerMessage message) {
            var ai = ((App)Application.Current).Ai;
            if (this.Message is { } shown && (ReferenceEquals(message, shown) || ai.KeyOf(message) == ai.KeyOf(shown))) Render();
        }

        private void AddAiCommands(TextCommandBarFlyout flyout) {
            foreach (var old in flyout.SecondaryCommands.OfType<AppBarButton>().Where(b => b.Tag as string == "ai").ToArray()) flyout.SecondaryCommands.Remove(old);
            var app = (App)Application.Current;
            if (!app.Ai.Enabled || this.Message is not { } message || string.IsNullOrWhiteSpace(message.ContentText)) return;
            var translated = app.Ai.Translation(message) is { Status: not TranslationStatus.Pending };
            var translate = new AppBarButton { Tag = "ai", Label = LocalizationHelper.GetString(translated ? "Ai.TranslateAgain" : "Ai.Translate"), Icon = new SymbolIcon(Symbol.Globe) };
            translate.Click += (_, _) => _ = app.Ai.TranslateAsync(message, message.LocalSource ?? app.Session.Source);
            flyout.SecondaryCommands.Add(translate);
            if (translated) {
                var hide = new AppBarButton { Tag = "ai", Label = LocalizationHelper.GetString("Ai.HideTranslation"), Icon = new SymbolIcon(Symbol.Clear) };
                hide.Click += (_, _) => app.Ai.ClearTranslation(message);
                flyout.SecondaryCommands.Add(hide);
            }
        }

        private static void PropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (!(d is MessageTextBlock textBlock)) {
                return;
            }
            textBlock.Render();
        }

        private void Render() {
            var textBlock = this;

            textBlock.Blocks.Clear();
            textBlock.Background = null;
            var message = textBlock.Message;
            if (message == null) {
                return;
            }

            var config = ((App)Application.Current).Config;
            if (config.Notifications.Any(notif => notif.Matches(message))) {
                textBlock.Background = new SolidColorBrush(Color.FromArgb(128, 200, 100, 100));
            }

            // Create new formatted text
            var inlines = MessageFormatter.ChunksToTextBlock(
                message,
                textBlock.FontSize,
                textBlock.ProcessMarkdown,
                textBlock.ShowTimestamps,
                textBlock.ActualTheme == ElementTheme.Light
            );
            
            var paragraph = new Microsoft.UI.Xaml.Documents.Paragraph();
            foreach (var inline in inlines) {
                inline.FontFamily = textBlock.FontFamily;
                paragraph.Inlines.Add(inline);
            }
            textBlock.Blocks.Add(paragraph);

            if (((App)Application.Current).Ai.Translation(message) is { } translation) {
                var text = translation.Status switch {
                    TranslationStatus.Pending => LocalizationHelper.GetString("Ai.Translating"),
                    TranslationStatus.Failed => LocalizationHelper.GetString("Ai.TranslationFailed") + " " + translation.Text,
                    _ => translation.Text,
                };
                var note = new Microsoft.UI.Xaml.Documents.Paragraph { Margin = new Thickness(16, 2, 0, 4) };
                note.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run {
                    Text = "↳ " + text, FontStyle = Windows.UI.Text.FontStyle.Italic, FontSize = Math.Max(10, textBlock.FontSize - 1),
                    Foreground = new SolidColorBrush(translation.Status == TranslationStatus.Failed ? Color.FromArgb(255, 200, 80, 80)
                        : textBlock.ActualTheme == ElementTheme.Light ? Color.FromArgb(255, 90, 90, 110) : Color.FromArgb(255, 170, 170, 190)),
                });
                textBlock.Blocks.Add(note);
            }
        }
    }
}
