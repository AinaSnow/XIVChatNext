using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace XIVChat_Desktop {
    public partial class ManageServer : Window {
        public App App => (App)Application.Current;
        public SavedServer? Server { get; private set; }

        private readonly bool isNewServer;

        public ManageServer(SavedServer? server) {
            this.Server = server;
            this.isNewServer = server == null;

            this.InitializeComponent();
            ThemeHelper.InitializeWindow(this);
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(400, 320));

            if (this.isNewServer) {
                this.Title = "添加服务器";
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            var serverName = this.ServerName.Text;
            var serverHost = this.ServerHost.Text;

            if (serverName.Length == 0 || serverHost.Length == 0) {
                // TODO: Show error dialog
                return;
            }

            ushort port;
            if (this.ServerPort.Text.Length == 0) {
                port = 14777;
            } else {
                if (!ushort.TryParse(this.ServerPort.Text, out port) || port < 1) {
                    // TODO: Show error dialog
                    return;
                }
            }

            if (this.isNewServer) {
                this.Server = new SavedServer(
                    serverName,
                    serverHost,
                    port
                );
                this.App.Config.Servers.Add(this.Server);
            } else {
                this.Server!.Name = serverName;
                this.Server.Host = serverHost;
                this.Server.Port = port;
            }

            this.App.Config.Save();
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            this.Close();
        }
    }
}
