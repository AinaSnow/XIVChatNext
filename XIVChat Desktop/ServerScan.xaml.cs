using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Sodium;

namespace XIVChat_Desktop {
    public partial class ServerScan : Window {
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly Channel<byte> cancelChannel = Channel.CreateBounded<byte>(1);
        private Task? udpThread;

        public ObservableCollection<SavedServer> Servers { get; } = new ObservableCollection<SavedServer>();

        public ServerScan() {
            this.InitializeComponent();
            ThemeHelper.InitializeWindow(this);
        }

        private async Task Scanner() {
            var payload = new byte[] {
                14,
            };

            const int multicastPort = 17444;
            var udpEndpoint = new IPEndPoint(IPAddress.Any, multicastPort);
            using var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(udpEndpoint);

            var multicastAddr = IPAddress.Parse("224.0.0.147");
            udp.JoinMulticastGroup(multicastAddr);

            var timerChannel = Channel.CreateBounded<byte>(1);

            _ = Task.Run(async () => {
                while (!this.cts.IsCancellationRequested) {
                    await timerChannel.Writer.WriteAsync(1);
                    await Task.Delay(5_000, this.cts.Token);
                }
            });

            var cancelTask = this.cancelChannel.Reader.ReadAsync().AsTask();
            var timerTask = timerChannel.Reader.ReadAsync().AsTask();
            var incomingTask = udp.ReceiveAsync();

            while (!this.cts.IsCancellationRequested) {
                var result = await Task.WhenAny(cancelTask, timerTask, incomingTask);
                if (result == cancelTask) {
                    break;
                }

                if (result == timerTask) {
                    timerTask = timerChannel.Reader.ReadAsync().AsTask();
                    await udp.SendAsync(payload, payload.Length, new IPEndPoint(multicastAddr, multicastPort));
                } else if (result == incomingTask) {
                    var incoming = await incomingTask;
                    incomingTask = udp.ReceiveAsync();

                    var server = ScannedServer.Decode(incoming.Buffer, incoming.RemoteEndPoint.Address.ToString());
                    if (server == null) {
                        continue;
                    }

                    var saved = new SavedServer(server.playerName, server.address, server.port);
                    if (this.Servers.Contains(saved)) {
                        continue;
                    }

                    this.DispatcherQueue.TryEnqueue(() => {
                        this.Servers.Add(saved);
                    });
                }
            }
        }

        private async void ServerScan_OnClosed(object sender, WindowEventArgs args) {
            this.cts.Cancel();
            await this.cancelChannel.Writer.WriteAsync(1);
            var task = this.udpThread;
            if (task != null) {
                await task;
            }
        }

        private void ServerScan_OnLoaded(object sender, RoutedEventArgs e) {
            this.udpThread ??= Task.Run(this.Scanner);
        }

        private void SavedServers_OnItemDoubleClick(SavedServer server) {
            this.AddServer(server);
        }

        private void Add_Click(object sender, RoutedEventArgs e) {
            var server = this.SavedServers.SelectedServer;
            if (server == null) {
                return;
            }

            this.AddServer(server);
        }

        private void AddServer(SavedServer server) {
            var dialog = new ManageServer(server);
            dialog.Activate();
        }
    }

    public class ScannedServer {
        public readonly string playerName;
        public readonly string address;
        public readonly ushort port;
        public readonly byte[] publicKey;

        public ScannedServer(string playerName, string address, ushort port, byte[] publicKey) {
            this.playerName = playerName;
            this.address = address;
            this.port = port;
            this.publicKey = publicKey;
        }

        public static ScannedServer? Decode(byte[] payload, string host) {
            if (payload.Length < 2) {
                return null;
            }

            using var memoryStream = new MemoryStream(payload);
            using var reader = new BinaryReader(memoryStream);

            if (reader.ReadByte() != 14) {
                return null;
            }

            var strLen = reader.ReadByte();
            var strBytes = reader.ReadBytes(strLen);
            var name = Encoding.UTF8.GetString(strBytes);

            var portBytes = reader.ReadBytes(2);
            var portBytesFlipped = portBytes.Reverse().ToArray();
            var port = BitConverter.ToUInt16(portBytesFlipped, 0);

            var key = reader.ReadBytes(PublicKeyBox.PublicKeyBytes);

            return new ScannedServer(
                name,
                host,
                port,
                key
            );
        }
    }
}
