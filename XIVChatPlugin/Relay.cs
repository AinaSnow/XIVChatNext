using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using XIVChat.Relay.Protocol;
using XIVChat.Relay.Transport;

namespace XIVChatPlugin;

internal enum ConnectionStatus { Disconnected, Connecting, Negotiating, Connected }

internal sealed class Relay : IDisposable {
    private readonly Plugin plugin;
    private readonly CancellationTokenSource lifetime = new();
    private X509Certificate2? certificate;
    private string? credential;
    private string? server;
    private Task? running;
    private int disposed;
    internal static string? ConnectionError { get; private set; }
    internal ConnectionStatus Status { get; private set; }
    internal string Fingerprint => certificate == null ? "" : RelayTls.Fingerprint(certificate);
    internal Relay(Plugin plugin) => this.plugin = plugin;
    internal void Start() {
        if (running != null || lifetime.IsCancellationRequested) return;
        try {
            server = plugin.Config.RelayUrl;
            RelayProtocol.BaseUri(server);
            credential = WindowsSecret.Unprotect(plugin.Config.RelayCredential ?? "");
            if (plugin.Config.RelayCertificate == null) {
                certificate = RelayTls.CreateCertificate();
                plugin.Config.RelayCertificate = WindowsSecret.Protect(Convert.ToBase64String(certificate.Export(X509ContentType.Pfx)));
                plugin.Config.Save();
            } else certificate = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(WindowsSecret.Unprotect(plugin.Config.RelayCertificate)), null, RelayTls.KeyStorage);
            var host = new RelayHostEndpoint(server, credential, certificate);
            host.StateChanged += state => {
                Status = state == "connected" ? ConnectionStatus.Connected : state == "connecting" ? ConnectionStatus.Connecting : ConnectionStatus.Disconnected;
                ConnectionError = state == "disconnected" ? "Relay disconnected. Check the server address, registration credential and endpoint fingerprint; reconnecting automatically." : null;
            };
            running = Task.Run(async () => {
                try {
                    await host.RunAsync(async (stream, token) => {
                        var client = new StreamConnected(stream);
                        using var registration = token.Register(() => client.Disconnect());
                        plugin.Server.SpawnClientTask(client, true);
                        await client.Closed;
                    }, lifetime.Token);
                } catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception) { ConnectionError = "Relay connection stopped."; }
                finally { Status = ConnectionStatus.Disconnected; }
            });
        } catch (Exception ex) { Status = ConnectionStatus.Disconnected; ConnectionError = ex.Message; }
    }
    internal async Task<string> CreateInvitationAsync() {
        if (Status != ConnectionStatus.Connected || credential == null || server == null) throw new InvalidOperationException("Connect the plugin to your relay first.");
        return RelayEndpoint.EncodeInvitation(await RelayEndpoint.InviteAsync(server, credential, Fingerprint, lifetime.Token));
    }
    internal void ResendPublicKey() { /* Existing game-key trust still happens inside each end-to-end TLS stream. */ }
    public void Dispose() {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        if (running == null) { certificate?.Dispose(); lifetime.Dispose(); }
        else _ = running.ContinueWith(_ => { certificate?.Dispose(); lifetime.Dispose(); }, TaskScheduler.Default);
    }
}
