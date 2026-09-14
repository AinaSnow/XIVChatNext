using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XIVChat.Relay.Protocol;

namespace XIVChat.Relay;

/// <summary>Private administration. Opaque sessions are deliberately invalidated on restart or password change.</summary>
public sealed class RelayAdmin {
    private const string CookieName = "XIVChatRelayAdmin";
    private readonly object gate = new();
    private readonly Dictionary<string, Session> sessions = new();
    private readonly byte[]? passwordHash;
    private readonly byte[] salt = RandomNumberGenerator.GetBytes(32);
    private readonly Uri? publicUrl;
    private DateTimeOffset loginWindow;
    private int loginAttempts;
    private sealed record Session(string Csrf, DateTimeOffset Expires);
    public sealed record LoginRequest(string? Password);
    public sealed record NameRequest(string? Name);

    public RelayAdmin(IConfiguration config) {
        var password = config["Relay:AdminPassword"];
        if (string.IsNullOrEmpty(password)) return; // Existing CLI deployments remain operational, with administration disabled.
        if (password.Length is < 16 or > 1024 || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Relay:AdminPassword must contain 16–1024 characters. Set a unique, strong management password.");
        publicUrl = RelayProtocol.BaseUri(config["Relay:PublicUrl"] ?? throw new InvalidOperationException("Set Relay:PublicUrl to the externally accessible HTTPS relay address."));
        passwordHash = PasswordHash(password);
    }
    private byte[] PasswordHash(string password) => Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private Session? Authenticate(HttpContext context) {
        var token = context.Request.Cookies[CookieName];
        if (token?.Length != 64) return null;
        lock (gate) {
            var key = Hash(token);
            if (!sessions.TryGetValue(key, out var session)) return null;
            if (session.Expires > DateTimeOffset.UtcNow) return session;
            sessions.Remove(key); return null;
        }
    }
    private bool CorrectHost(HttpContext context) => publicUrl != null && string.Equals(context.Request.Host.Value, publicUrl.Authority, StringComparison.OrdinalIgnoreCase);
    private bool SameOrigin(HttpContext context) => publicUrl != null && string.Equals(context.Request.Headers.Origin.ToString(), publicUrl.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
    private CookieOptions Cookie() => new() { HttpOnly = true, Secure = publicUrl?.Scheme == "https", SameSite = SameSiteMode.Strict, Path = "/admin", IsEssential = true };
    private static IResult Error(int status, string code) => Results.Json(new { error = code }, statusCode: status);
    public void Map(WebApplication app) {
        app.Use(async (context, next) => {
            if (context.Request.Path.StartsWithSegments("/admin")) {
                context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers.XFrameOptions = "DENY";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
            }
            await next(context);
        });
        var api = app.MapGroup("/admin/api");
        api.AddEndpointFilter(async (invocation, next) => {
            var context = invocation.HttpContext;
            if (passwordHash == null) return Error(503, "admin_not_configured");
            if (!CorrectHost(context)) return Error(403, "wrong_origin");
            var path = context.Request.Path.Value!;
            if (context.Request.Method != "GET" && (!SameOrigin(context) || !context.Request.HasJsonContentType())) return Error(403, "wrong_origin");
            if (path is "/admin/api/login" or "/admin/api/session") return await next(invocation);
            var session = Authenticate(context);
            if (session == null) return Error(401, "login_required");
            if (context.Request.Method != "GET" && !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(context.Request.Headers["X-XIVChat-CSRF"].ToString()), Encoding.UTF8.GetBytes(session.Csrf))) return Error(403, "csrf_invalid");
            return await next(invocation);
        });
        api.MapGet("/session", (HttpContext context) => {
            var session = Authenticate(context);
            return Results.Ok(new { authenticated = session != null, csrf = session?.Csrf, publicUrl = publicUrl!.GetLeftPart(UriPartial.Authority) });
        });
        api.MapPost("/login", (HttpContext context, LoginRequest request) => {
            lock (gate) {
                var now = DateTimeOffset.UtcNow;
                if (now >= loginWindow.AddMinutes(1)) { loginWindow = now; loginAttempts = 0; }
                if (++loginAttempts > 5) { context.Response.Headers.RetryAfter = "60"; return Error(429, "login_rate_limited"); }
            }
            if (request.Password?.Length is not (>= 16 and <= 1024) || !CryptographicOperations.FixedTimeEquals(PasswordHash(request.Password), passwordHash!)) return Error(401, "invalid_password");
            var secret = RelayStore.Secret(); var session = new Session(RelayStore.Secret(), DateTimeOffset.UtcNow.AddHours(8));
            lock (gate) {
                foreach (var key in sessions.Where(s => s.Value.Expires <= DateTimeOffset.UtcNow).Select(s => s.Key).ToArray()) sessions.Remove(key);
                if (sessions.Count >= 64) return Error(429, "session_limit");
                var old = context.Request.Cookies[CookieName]; if (old?.Length == 64) sessions.Remove(Hash(old));
                sessions.Add(Hash(secret), session);
            }
            context.Response.Cookies.Append(CookieName, secret, Cookie());
            return Results.Ok(new { csrf = session.Csrf });
        });
        api.MapPost("/logout", (HttpContext context) => {
            lock (gate) sessions.Remove(Hash(context.Request.Cookies[CookieName]!));
            context.Response.Cookies.Delete(CookieName, Cookie()); return Results.Ok();
        });
        api.MapGet("/dashboard", (RelayStore store, RelayHub hub) => {
            var snapshot = store.ManagementSnapshot();
            return Results.Ok(new { publicUrl = publicUrl!.GetLeftPart(UriPartial.Authority), protocol = RelayProtocol.Version, snapshot.Devices, snapshot.Clients, live = hub.ManagementSnapshot(), serverTime = DateTimeOffset.UtcNow });
        });
        api.MapPost("/devices", (NameRequest request, RelayStore store) => {
            try {
                RelayStore.ValidateName(request.Name);
                var created = store.AddDevice(request.Name!.Trim());
                return Results.Ok(new { created.Id, created.Credential, publicUrl = publicUrl!.GetLeftPart(UriPartial.Authority) });
            } catch (ArgumentException) { return Error(400, "invalid_name"); }
        });
        api.MapPost("/devices/{id}/rename", (string id, NameRequest request, RelayStore store) => {
            try { return store.RenameDevice(id, request.Name!) ? Results.Ok() : Error(404, "device_unavailable"); }
            catch (ArgumentException) { return Error(400, "invalid_name"); }
        });
        api.MapPost("/devices/{id}/invitation", (string id, RelayStore store, RelayHub hub) => {
            if (!store.ActiveDevice(id) || !hub.Online(id)) return Error(409, "device_offline");
            try {
                var invite = store.Invite(id) with { Server = publicUrl!.GetLeftPart(UriPartial.Authority) };
                var encoded = "xivchat-relay:" + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(invite, RelayProtocol.Json));
                return Results.Ok(new { invitation = encoded, invite.Fingerprint, invite.ExpiresAt });
            } catch (InvalidOperationException) { return Error(409, "device_offline"); }
        });
        api.MapPost("/devices/{id}/revoke", (string id, RelayStore store) => {
            if (!store.ActiveDevice(id)) return Error(404, "device_unavailable");
            store.Revoke("device", id); return Results.Ok();
        });
        api.MapPost("/clients/{id}/revoke", (string id, RelayStore store) => {
            if (!store.ActiveClient(id)) return Error(404, "client_unavailable");
            store.Revoke("client", id); return Results.Ok();
        });
    }
}
