using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace XIVChat_Desktop.Controls {
    public class SelectableTextBlock : UserControl {
        private readonly RichTextBlock _richTextBlock;

        public SelectableTextBlock() {
            _richTextBlock = new RichTextBlock {
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
            };
            this.Content = _richTextBlock;
        }

        public BlockCollection Blocks => _richTextBlock.Blocks;
        public bool IsTextSelectionEnabled {
            get => _richTextBlock.IsTextSelectionEnabled;
            set => _richTextBlock.IsTextSelectionEnabled = value;
        }

        public TextWrapping TextWrapping {
            get => _richTextBlock.TextWrapping;
            set => _richTextBlock.TextWrapping = value;
        }
    }
}
