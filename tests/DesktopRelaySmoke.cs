using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Sodium;
using XIVChat.Relay.Protocol;
using XIVChat.Relay.Transport;
using XIVChatCommon;
using XIVChatCommon.Message.Server;

namespace XIVChat_Desktop;

internal static class DesktopRelaySmoke {
    private sealed record Fixture(string Server, string DeviceId, string Credential);
    internal static async Task Run(App app, Action<bool, string> check, Func<object, string, object> field, Func<Button, Task> click) {
        var path = Environment.GetEnvironmentVariable("XIVCHAT_SETUP_RELAY_TEST");
        if (string.IsNullOrEmpty(path)) return;
        var fixture = System.Text.Json.JsonSerializer.Deserialize<Fixture>(File.ReadAllText(path))!;
        var certificatePath = path + ".certificate";
        using var certificate = File.Exists(certificatePath)
            ? System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(WindowsSecret.Unprotect(File.ReadAllText(certificatePath))), null, RelayTls.KeyStorage)
            : RelayTls.CreateCertificate();
        if (!File.Exists(certificatePath)) File.WriteAllText(certificatePath, WindowsSecret.Protect(Convert.ToBase64String(certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx))));
        var key = PublicKeyBox.GenerateKeyPair();
        app.Config.TrustedKeys.Add(new TrustedKey("relay fixture", key.PublicKey));
        async Task Until(Func<bool> ready) {
            for (var i = 0; i < 200; i++) { if (ready()) return; await Task.Delay(50); }
            throw new Exception("Relay desktop condition timed out.");
        }
        async Task Game(Stream stream, CancellationToken token) {
            await stream.ReadExactlyAsync(new byte[3], token);
            var handshake = await KeyExchange.ServerHandshake(key, stream, token);
            await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, token);
            await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, Pong.Instance, token);
            while (!token.IsCancellationRequested) await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, token);
        }
        var stop = new CancellationTokenSource();
        Task? running = null;
        var online = false;
        void StartHost() {
            var host = new RelayHostEndpoint(fixture.Server, fixture.Credential, certificate);
            host.StateChanged += state => online = state == "connected";
            running = host.RunAsync(Game, stop.Token);
        }
        async Task StopHost() {
            stop.Cancel();
            try { if (running != null) await running; } catch (OperationCanceledException) { }
            stop.Dispose(); stop = new CancellationTokenSource(); online = false;
        }
        try {
            StartHost(); await Until(() => online);
            var invite = await RelayEndpoint.InviteAsync(fixture.Server, fixture.Credential, RelayTls.Fingerprint(certificate), stop.Token);
            var dialog = new RelayPairDialog(); dialog.Activate(); await Task.Delay(100);
            ((TextBox)field(dialog, "name")).Text = "Remote Linux relay fixture";
            var encoded = RelayEndpoint.EncodeInvitation(invite);
            _ = RelayEndpoint.DecodeInvitation(encoded);
            ((PasswordBox)field(dialog, "invitation")).Password = encoded;
            check(((PasswordBox)field(dialog, "invitation")).Password.Length == encoded.Length, "relay invitation input retains the complete bounded invitation");
            await click((Button)field(dialog, "inspect"));
            await Until(() => field(dialog, "parsed") != null || ((TextBlock)field(dialog, "error")).Text.Length > 0);
            if (((TextBlock)field(dialog, "error")).Text is { Length: > 0 } inspectError) throw new Exception("Invitation inspection: " + inspectError);
            ((CheckBox)field(dialog, "verified")).IsChecked = true;
            check(field(dialog, "parsed") != null && ((CheckBox)field(dialog, "verified")).IsChecked == true, "pairing confirmation applies to the inspected invitation");
            await click((Button)field(dialog, "save"));
            await Until(() => app.Config.Servers.Any(s => s.Relay?.DeviceId == fixture.DeviceId) || ((TextBlock)field(dialog, "error")).Text.Length > 0);
            if (((TextBlock)field(dialog, "error")).Text is { Length: > 0 } pairError) throw new Exception("Pairing dialog: " + pairError);
            var target = app.Config.Servers.Single(s => s.Relay?.DeviceId == fixture.DeviceId);
            check(WindowsSecret.Unprotect(target.Relay!.ProtectedCredential).Length == 64, "real pairing dialog redeems remote Linux invitation and protects its saved credential");
            app.Connect(target); await Until(() => app.Connection?.SessionReady == true);
            var firstId = app.Connection!.Id;
            check(app.Config.LastSuccessfulConnection?.Relay?.DeviceId == fixture.DeviceId && !app.Connection.Available,
                "desktop authenticates through remote Linux relay and remembers a logged-out game endpoint");
            await StopHost(); await Until(() => !app.Connected); StartHost();
            await Until(() => app.Connection?.SessionReady == true && app.Connection.Id != firstId);
            check(true, "desktop automatically establishes a fresh encrypted session after relay host reconnects");
            await StopHost(); await Until(() => !app.Connected); app.Disconnect(); StartHost(); await Until(() => online);
            await Task.Delay(2500);
            check(!app.Connected, "explicit disconnect cancels a pending automatic relay retry");
        } finally { app.Disconnect(); await StopHost(); stop.Dispose(); }
    }
}
