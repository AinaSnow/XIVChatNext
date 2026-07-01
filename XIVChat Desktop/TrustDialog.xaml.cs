using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace XIVChat_Desktop {
    public partial class TrustDialog : Window {
        private readonly ChannelWriter<bool> trustChannel;
        private readonly byte[] remoteKey;

        private App App => (App)Application.Current;

        public TrustDialog(ChannelWriter<bool> trustChannel, byte[] remoteKey) {
            this.trustChannel = trustChannel;
            this.remoteKey = remoteKey;

            this.InitializeComponent();
            ThemeHelper.InitializeWindow(this);

            this.ClientPublicKey.Text = ToHexString(this.App.Config.KeyPair.PublicKey);
            var clientColours = BreakIntoColours(this.App.Config.KeyPair.PublicKey);
            for (var i = 0; i < this.ClientPublicKeyColours.Children.Count; i++) {
                var rect = (Rectangle)this.ClientPublicKeyColours.Children[i];
                rect.Fill = new SolidColorBrush(clientColours[i]);
            }

            var hexKey = ToHexString(remoteKey);
            this.ServerPublicKey.Text = hexKey;
            this.KeyName.Text = $"游戏端 ({hexKey.Substring(0, hexKey.Length >= 8 ? 8 : hexKey.Length)})";
            var serverColours = BreakIntoColours(remoteKey);
            for (int i = 0; i < this.ServerPublicKeyColours.Children.Count; i++) {
                var rect = (Rectangle)this.ServerPublicKeyColours.Children[i];
                rect.Fill = new SolidColorBrush(serverColours[i]);
            }
        }

        private static List<Color> BreakIntoColours(IEnumerable<byte> key) {
            var colours = new List<Color>();

            foreach (var chunk in key.ToList().Chunks(3)) {
                var r = chunk[0];
                var g = chunk.Count > 1 ? chunk[1] : (byte)0;
                var b = chunk.Count > 2 ? chunk[2] : (byte)0;

                colours.Add(Color.FromArgb(255, r, g, b));
            }

            return colours;
        }

        private static string ToHexString(IEnumerable<byte> bytes) {
            return string.Join("", bytes.Select(b => b.ToString("X2")));
        }

        private async void Yes_Click(object sender, RoutedEventArgs e) {
            var keyName = this.KeyName.Text?.Trim() ?? "";
            if (keyName.Length == 0) {
                var hex = ToHexString(this.remoteKey);
                keyName = $"游戏端 ({hex.Substring(0, hex.Length >= 8 ? 8 : hex.Length)})";
            }

            var trustedKey = new TrustedKey(keyName, this.remoteKey);
            this.App.Config.TrustedKeys.Add(trustedKey);
            this.App.Config.Save();
            await this.trustChannel.WriteAsync(true);
            this.Close();
        }

        private async void No_Click(object sender, RoutedEventArgs e) {
            await this.trustChannel.WriteAsync(false);
            this.Close();
        }
    }
}
