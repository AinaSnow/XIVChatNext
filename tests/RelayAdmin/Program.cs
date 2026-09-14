using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using XIVChat.Relay;
using XIVChat.Relay.Protocol;
using XIVChat.Relay.Transport;

try {
    if (args is ["fixture", var path]) { await Fixture.Run(path); return; }
    await RunChecks();
} catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }

static async Task RunChecks() {
    await using var service = new TestRelay();
    using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    var token = stop.Token; var count = 0;
    void Check(bool valid, string name) { if (!valid) throw new Exception(name); Console.WriteLine($"PASS {++count}: {name}"); }
    async Task Status(string path, object? body, int expected, string name, string? origin = null, string? csrf = null, string? cookie = null) {
        using var response = await service.Send(path, body, origin, csrf, cookie);
        Check((int)response.StatusCode == expected, name + $" (HTTP {(int)response.StatusCode})");
    }
    async Task<JsonElement> Get(string path) { using var response = await service.Send(path); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>(token); }
    async Task<JsonElement> Post(string path, object body) { using var response = await service.Send(path, body); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>(token); }
    async Task<bool> Reject(Func<Task> action) { try { await action(); return false; } catch (Exception e) when (e is IOException or System.Net.WebSockets.WebSocketException or System.Security.Authentication.AuthenticationException) { return true; } }
    await service.Start();
    foreach (var path in new[] { "/admin", "/admin/", "/admin/index.html", "/admin/app.js", "/admin/app.css", "/admin/logo.png" }) {
        using var response = await service.Send(path);
        Check(response.IsSuccessStatusCode && (await response.Content.ReadAsByteArrayAsync(token)).Length > 100 && response.Headers.CacheControl?.NoStore == true && response.Headers.Contains("Content-Security-Policy"), "published admin asset loads with security headers: " + path);
    }
    Check(!(await Get("/admin/api/session")).GetProperty("authenticated").GetBoolean(), "anonymous session reveals no management state");
    await Status("/admin/api/dashboard", null, 401, "anonymous dashboard denied");
    await Status("/admin/api/devices", new { name = "unauthorized" }, 401, "anonymous device creation denied");
    await Status("/admin/api/login", new { password = service.Password }, 403, "cross-origin login denied", "https://evil.example");
    await Status("/admin/api/login", new { password = "incorrect-password-123" }, 401, "wrong password denied");
    await service.Login();
    Check(service.Cookie.Length > 64 && service.SetCookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && service.SetCookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase) && service.SetCookie.Contains("path=/admin"), "management session is HttpOnly, Strict and scoped to /admin");
    Check((await Get("/admin/api/session")).GetProperty("authenticated").GetBoolean(), "login survives a subsequent browser request");
    await Status("/admin/api/devices", new { name = "bad-csrf" }, 403, "missing/incorrect CSRF denied", csrf: "wrong");
    await Status("/admin/api/devices", new { name = "bad-origin" }, 403, "cross-origin mutation denied even with correct session", "https://evil.example");
    await Status("/admin/api/devices", new { name = "bad-cookie" }, 401, "forged session denied", cookie: "XIVChatRelayAdmin=" + RelayStore.Secret());
    using (var request = new HttpRequestMessage(HttpMethod.Post, service.Address + "/admin/api/devices") { Content = new StringContent("name=form", Encoding.UTF8, "application/x-www-form-urlencoded") }) {
        request.Headers.Add("Cookie", service.Cookie); request.Headers.Add("Origin", service.Address);
        using var response = await service.Http.SendAsync(request, token);
        Check(!response.IsSuccessStatusCode, "form-based mutation denied");
    }
    await Status("/admin/api/devices", new { name = " " }, 400, "blank device name rejected");
    await Status("/admin/api/devices", new { name = "bad\nname" }, 400, "control characters rejected");
    await Status("/admin/api/devices", new { name = new string('x', 101) }, 400, "oversized name rejected");
    var created = await Post("/admin/api/devices", new { name = "中文 🎮 <img src=x onerror=alert(1)>" });
    var id = created.GetProperty("id").GetString()!; var credential = created.GetProperty("credential").GetString()!;
    Check(credential.Length == 64 && created.GetProperty("publicUrl").GetString() == service.Address, "device registration returns a one-time credential and correct address");
    var before = (await Get("/admin/api/dashboard")).GetRawText();
    Check(!before.Contains(credential) && !before.Contains("token_hash") && !before.Contains("credential", StringComparison.OrdinalIgnoreCase), "dashboard never re-exposes credentials or hashes");
    await Status($"/admin/api/devices/{id}/invitation", new { }, 409, "offline device cannot issue a pairing invitation");
    await Status($"/admin/api/devices/{id}/rename", new { name = "Renamed 游戏电脑" }, 200, "device renamed through administration");
    Check((await Get("/admin/api/dashboard")).GetProperty("devices")[0].GetProperty("name").GetString() == "Renamed 游戏电脑", "rename persisted in dashboard");
    using var certificate = RelayTls.CreateCertificate();
    using var hostStop = CancellationTokenSource.CreateLinkedTokenSource(token);
    var host = new RelayHostEndpoint(service.Address, credential, certificate);
    var hostTask = host.RunAsync(Fixture.Echo, hostStop.Token);
    await service.Eventually(async () => (await Get("/admin/api/dashboard")).GetProperty("live").GetProperty("onlineDevices").GetArrayLength() == 1);
    var invite = RelayEndpoint.DecodeInvitation((await Post($"/admin/api/devices/{id}/invitation", new { })).GetProperty("invitation").GetString()!);
    Check(invite.Fingerprint == RelayTls.Fingerprint(certificate) && invite.Server == service.Address, "admin invitation is accepted by the unchanged desktop decoder with correct certificate pin");
    var replacement = RelayEndpoint.DecodeInvitation((await Post($"/admin/api/devices/{id}/invitation", new { })).GetProperty("invitation").GetString()!);
    Check(await Reject(() => RelayEndpoint.PairAsync(invite, "old invite", token)), "new admin invitation invalidates previous invitation");
    var paired = await RelayEndpoint.PairAsync(replacement, "Desktop 客户端", token);
    Check(await Reject(() => RelayEndpoint.PairAsync(replacement, "replay", token)), "web-generated invitation is one-time only");
    using var connection = await RelayEndpoint.ConnectAsync(service.Address, paired.Credential, paired.Fingerprint, token);
    var payload = Encoding.UTF8.GetBytes("Web administration TLS verification · 中文");
    await connection.WriteAsync(payload, token); var echoed = new byte[payload.Length]; await connection.ReadExactlyAsync(echoed, token);
    Check(payload.SequenceEqual(echoed), "web-registered device and paired client carry actual endpoint-TLS data");
    var live = await Get("/admin/api/dashboard");
    Check(live.GetProperty("clients").GetArrayLength() == 1 && live.GetProperty("live").GetProperty("connections").GetArrayLength() == 1 && !live.GetRawText().Contains("ticket", StringComparison.OrdinalIgnoreCase), "dashboard lists live connections without routing secrets");
    await Status($"/admin/api/clients/{paired.ClientId}/revoke", new { }, 200, "client access revoked through administration");
    await service.Eventually(async () => (await Get("/admin/api/dashboard")).GetProperty("live").GetProperty("connections").GetArrayLength() == 0);
    Check(await Reject(async () => { using var invalid = await RelayEndpoint.ConnectAsync(service.Address, paired.Credential, paired.Fingerprint, token); }), "revoked client disconnects and cannot reconnect");
    Check((await Get("/admin/api/dashboard")).GetProperty("live").GetProperty("onlineDevices").GetArrayLength() == 1, "revoking client leaves game device online");
    var nextInvite = RelayEndpoint.DecodeInvitation((await Post($"/admin/api/devices/{id}/invitation", new { })).GetProperty("invitation").GetString()!);
    var nextPair = await RelayEndpoint.PairAsync(nextInvite, "second client", token);
    await Status($"/admin/api/devices/{id}/revoke", new { }, 200, "game device access revoked through administration");
    await service.Eventually(async () => (await Get("/admin/api/dashboard")).GetProperty("live").GetProperty("onlineDevices").GetArrayLength() == 0);
    Check((await Get("/admin/api/dashboard")).GetProperty("clients").EnumerateArray().All(c => c.GetProperty("revoked").GetBoolean()), "revoking game device disables every associated client");
    Check(await Reject(async () => { using var invalid = await RelayEndpoint.ConnectAsync(service.Address, nextPair.Credential, nextPair.Fingerprint, token); }), "child credential fails after parent device revocation");
    hostStop.Cancel(); try { await hostTask; } catch (OperationCanceledException) { }
    var oldCookie = service.Cookie; var oldCsrf = service.Csrf;
    await Status("/admin/api/logout", new { }, 200, "logout succeeds with CSRF");
    await Status("/admin/api/dashboard", null, 401, "logged-out cookie cannot be replayed", cookie: oldCookie);
    await service.Login(); oldCookie = service.Cookie; oldCsrf = service.Csrf;
    await service.Restart();
    await Status("/admin/api/dashboard", null, 401, "server restart invalidates all management sessions", cookie: oldCookie);
    await service.Login();
    Check((await Get("/admin/api/dashboard")).GetProperty("devices")[0].GetProperty("revoked").GetBoolean(), "device, client and revocation records persist across restart");
    using (var db = new SqliteConnection("Data Source=" + Path.Combine(service.Data, "relay.sqlite3"))) {
        db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT token_hash FROM devices WHERE id=$id"; cmd.Parameters.AddWithValue("$id",id);
        Check((string)cmd.ExecuteScalar()! != credential, "database stores credential hash, not plaintext");
    }
    await service.Restart(publicUrl: "https://relay.example.test");
    await Status("/admin/api/session", null, 403, "unexpected Host header denied");
    using (var req = new HttpRequestMessage(HttpMethod.Post, service.Address + "/admin/api/login") { Content = JsonContent.Create(new { password = service.Password }) }) {
        req.Headers.Host = "relay.example.test"; req.Headers.Add("Origin", "https://relay.example.test");
        using var response = await service.Http.SendAsync(req);
        Check(response.IsSuccessStatusCode && response.Headers.GetValues("Set-Cookie").Single().Contains("secure", StringComparison.OrdinalIgnoreCase), "HTTPS reverse-proxy deployment issues Secure cookie");
    }
    await service.Restart();
    for (var i = 0; i < 5; i++) { using var response = await service.Send("/admin/api/login", new { password = "invalid-password-123" }); }
    await Status("/admin/api/login", new { password = service.Password }, 429, "sixth login attempt is rate-limited");
    await service.Restart(enabled: false);
    await Status("/admin/api/session", null, 503, "missing admin password disables administration");
    await Status("/healthz", null, 200, "existing relay stays healthy without web administration configured");
    await service.Stop();
    var invalidStart = await service.StartInvalidConfiguration();
    Check(invalidStart.ExitCode == 1 && invalidStart.Output.Contains("Relay:AdminPassword") && invalidStart.Output.Contains("1024") && !invalidStart.Output.Contains(service.Password), "invalid admin configuration exits cleanly with useful message and no password leak (exit=" + invalidStart.ExitCode + ")");
    Console.WriteLine($"Relay admin checks passed: {count}");
}

sealed class TestRelay : IAsyncDisposable {
#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif
    private readonly string root;
    private Process? process;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> output = new();
    public string Address { get; }
    public string Data { get; }
    public string Password { get; } = RelayStore.Secret();
    public string Cookie { get; private set; } = "";
    public string Csrf { get; private set; } = "";
    public string SetCookie { get; private set; } = "";
    public HttpClient Http { get; } = new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
    public TestRelay() {
        root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root,"src","XIVChat.Relay","XIVChat.Relay.csproj"))) root = Directory.GetParent(root)?.FullName ?? throw new Exception("Run inside the repository.");
        Data = Path.Combine(root,"artifacts","relay-admin-tests-"+Guid.NewGuid().ToString("N"));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); Address = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    }
    private Process Spawn(string password, string? publicUrl) {
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        info.ArgumentList.Add(Path.Combine(root,"src","XIVChat.Relay","bin",BuildConfiguration,"net10.0","XIVChat.Relay.dll")); info.ArgumentList.Add("--urls"); info.ArgumentList.Add(Address);
        info.Environment["XIVCHAT_RELAY_DATA"] = Data; info.Environment["Relay__AdminPassword"] = password; info.Environment["Relay__PublicUrl"] = publicUrl ?? Address; info.Environment["Logging__LogLevel__Default"] = "Warning";
        var child = Process.Start(info)!;
        child.OutputDataReceived += (_,e) => { if(e.Data != null && output.Count < 40)output.Enqueue(e.Data); };
        child.ErrorDataReceived += (_,e) => { if(e.Data != null && output.Count < 40)output.Enqueue(e.Data); };
        child.BeginOutputReadLine(); child.BeginErrorReadLine(); return child;
    }
    public async Task Start(bool enabled = true, string? publicUrl = null) {
        output.Clear(); process = Spawn(enabled?Password:"",publicUrl);
        await Eventually(async () => { if(process.HasExited)throw new Exception("Relay failed: "+string.Join('\n',output));try { using var r = await Http.GetAsync(Address+"/healthz");return r.IsSuccessStatusCode; }catch(HttpRequestException){return false;} });
    }
    public async Task Stop() { if(process == null)return;if(!process.HasExited){process.Kill();await process.WaitForExitAsync();}process.Dispose();process = null; }
    public async Task Restart(bool enabled = true, string? publicUrl = null) { await Stop(); await Start(enabled,publicUrl); }
    public async Task<(int ExitCode,string Output)> StartInvalidConfiguration() {
        output.Clear();using var invalid = Spawn("short",null);await invalid.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));return(invalid.ExitCode,string.Join('\n',output));
    }
    public async Task Eventually(Func<Task<bool>> condition) { for(var i=0;i<40;i++){if(await condition())return;await Task.Delay(150);}throw new Exception("Timed out: "+string.Join('\n',output)); }
    public Task<HttpResponseMessage> Send(string path, object? body = null, string? origin = null, string? csrf = null, string? cookie = null) {
        var request = new HttpRequestMessage(body == null?HttpMethod.Get:HttpMethod.Post,Address+path);
        if(body != null){request.Content = JsonContent.Create(body);request.Headers.Add("Origin",origin??Address);request.Headers.Add("X-XIVChat-CSRF",csrf??Csrf);}
        if(!string.IsNullOrEmpty(cookie??Cookie))request.Headers.Add("Cookie",cookie??Cookie);
        return SendAndDispose(request);
    }
    private async Task<HttpResponseMessage> SendAndDispose(HttpRequestMessage request) { using(request)return await Http.SendAsync(request); }
    public async Task Login() {
        using var response = await Send("/admin/api/login",new{password=Password});response.EnsureSuccessStatusCode();
        SetCookie = response.Headers.GetValues("Set-Cookie").Single();Cookie = SetCookie.Split(';')[0];Csrf = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrf").GetString()!;
    }
    public async ValueTask DisposeAsync(){await Stop();Http.Dispose();}
}

static class Fixture {
    public static async Task Echo(Stream stream, CancellationToken token) {
        var buffer = new byte[4096];while(true){var count=await stream.ReadAsync(buffer,token);if(count==0)return;await stream.WriteAsync(buffer.AsMemory(0,count),token);}
    }
    public static async Task Run(string path) {
        var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(path)).RootElement;
        var address = fixture.GetProperty("address").GetString()!; var credential = fixture.GetProperty("credential").GetString()!;
        using var token = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        Console.CancelKeyPress += (_,e)=>{e.Cancel=true;token.Cancel();};
        using var cert = RelayTls.CreateCertificate();
        var host = new RelayHostEndpoint(address,credential,cert);
        host.StateChanged += s=>Console.WriteLine("Fixture host: "+s);
        var hostTask = host.RunAsync(Echo,token.Token);
        var invitationFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,"ui-invitation.txt");
        Stream? stream = null;
        try {
            while(!token.IsCancellationRequested) {
                if(stream == null && File.Exists(invitationFile)) {
                    var invitation = RelayEndpoint.DecodeInvitation(await File.ReadAllTextAsync(invitationFile,token.Token));
                    var pair = await RelayEndpoint.PairAsync(invitation,"网页联调 · 桌面客户端",token.Token);
                    stream = await RelayEndpoint.ConnectAsync(address,pair.Credential,pair.Fingerprint,token.Token);
                    Console.WriteLine("Fixture desktop paired and connected with endpoint TLS.");
                }
                if(stream != null){var sample=Encoding.UTF8.GetBytes("管理后台联调");await stream.WriteAsync(sample,token.Token);var reply=new byte[sample.Length];await stream.ReadExactlyAsync(reply,token.Token);if(!reply.SequenceEqual(sample))throw new IOException("Echo mismatch.");}
                await Task.Delay(1000,token.Token);
            }
        } catch(OperationCanceledException) { } catch(IOException){Console.WriteLine("Fixture desktop disconnected.");await Task.Delay(Timeout.Infinite,token.Token).ContinueWith(_=>{});}
        finally {stream?.Dispose();token.Cancel();try{await hostTask;}catch(OperationCanceledException){}}
    }
}
