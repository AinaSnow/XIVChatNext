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
            Localize.BindWindow(this, () => this.Title = LocalizationHelper.GetString(this.isNewServer ? "ManageServer.New" : "ManageServer.Title"));
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(400, 320));
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            var serverName = this.ServerName.Text.Trim();
            var serverHost = this.ServerHost.Text.Trim();

            if (serverName.Length == 0 || serverHost.Length == 0) {
                ValidationError.Text = SetupText.T("请输入连接名称和游戏电脑地址。", "Enter a connection name and game computer address.");
                return;
            }

            ushort port;
            if (this.ServerPort.Text.Length == 0) {
                port = 14777;
            } else {
                if (!ushort.TryParse(this.ServerPort.Text, out port) || port < 1) {
                    ValidationError.Text = SetupText.T("端口应为 1–65535 的整数。", "The port must be an integer between 1 and 65535.");
                    return;
                }
            }

            if (System.Uri.CheckHostName(serverHost) == System.UriHostNameType.Unknown) { ValidationError.Text = SetupText.T("请输入 IP 或主机名，不要包含协议或端口。", "Enter an IP address or hostname without a scheme or port."); return; }
            var previous = this.Server?.Snapshot(); var previousLast = this.App.Config.LastSuccessfulConnection;
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

            if (this.App.Config.LastSuccessfulConnection?.Id == this.Server!.Id && (previous?.Host != serverHost || previous?.Port != port)) this.App.Config.LastSuccessfulConnection = null;
            try { this.App.Config.Save(); this.Close(); }
            catch (System.Exception ex) {
                if (this.isNewServer) this.App.Config.Servers.Remove(this.Server);
                else { this.Server.Name = previous!.Name; this.Server.Host = previous.Host; this.Server.Port = previous.Port; }
                this.App.Config.LastSuccessfulConnection = previousLast; ValidationError.Text = ex.Message;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            this.Close();
        }
    }
}
