using System.Linq;
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
        }

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

        private static void PropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (!(d is MessageTextBlock textBlock)) {
                return;
            }

            var message = textBlock.Message;
            if (message == null) {
                return;
            }

            var config = ((App)Application.Current).Config;
            if (config.Notifications.Any(notif => notif.Matches(message))) {
                textBlock.Background = new SolidColorBrush(Color.FromArgb(128, 200, 100, 100));
            }

            textBlock.Blocks.Clear();

            // Create new formatted text
            var inlines = MessageFormatter.ChunksToTextBlock(
                message,
                textBlock.FontSize,
                textBlock.ProcessMarkdown,
                textBlock.ShowTimestamps
            );
            
            var paragraph = new Microsoft.UI.Xaml.Documents.Paragraph();
            foreach (var inline in inlines) {
                inline.FontFamily = textBlock.FontFamily;
                paragraph.Inlines.Add(inline);
            }
            textBlock.Blocks.Add(paragraph);
        }
    }
}
