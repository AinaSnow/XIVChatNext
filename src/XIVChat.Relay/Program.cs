using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using XIVChat.Relay;
using XIVChat.Relay.Protocol;

var dataPath = Environment.GetEnvironmentVariable("XIVCHAT_RELAY_DATA") ?? Path.Combine(AppContext.BaseDirectory, "data");
if (args is ["health"]) {
    try { using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) }; using var response = await client.GetAsync("http://127.0.0.1:8080/healthz"); Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1; }
    catch { Environment.ExitCode = 1; }
    return;
}
if (args.Length > 0 && args[0] is "add-device" or "list-devices" or "list-clients" or "revoke-device" or "revoke-client") {
    using var store = new RelayStore(Path.Combine(dataPath, "relay.sqlite3"));
    if (args[0] == "add-device" && args.Length == 2) {
        var created = store.AddDevice(args[1]);
        Console.WriteLine($"Device: {created.Id}\nRegistration credential (shown once): {created.Credential}");
    } else if (args[0] is "list-devices" or "list-clients") foreach (var line in store.List(args[0][5..])) Console.WriteLine(line);
    else if (args[0].StartsWith("revoke-") && args.Length == 2) { store.Revoke(args[0][7..], args[1]); Console.WriteLine("Revoked. Active connections close within two seconds."); }
    else { Console.Error.WriteLine("Usage: add-device NAME | list-devices | list-clients | revoke-device ID | revoke-client ID"); Environment.ExitCode = 2; }
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => { options.Limits.MaxRequestBodySize = RelayProtocol.MaxControlBytes; options.Limits.MaxRequestHeadersTotalSize = 16 * 1024; });
builder.Services.AddSingleton(new RelayStore(Path.Combine(dataPath, "relay.sqlite3")));
builder.Services.AddSingleton<RelayHub>();
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetConcurrencyLimiter("global", _ => new ConcurrencyLimiterOptions { PermitLimit = 1024, QueueLimit = 0 })),
        PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
});
var app = builder.Build();
app.UseRateLimiter();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20), KeepAliveTimeout = TimeSpan.FromSeconds(20) });
app.Use(async (context, next) => { context.Response.Headers.CacheControl = "no-store"; await next(context); });
var hub = app.Services.GetRequiredService<RelayHub>(); hub.Start();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", protocol = RelayProtocol.Version }));
app.Map("/v1/host", hub.HostControl);
app.Map("/v1/client", hub.Client);
app.Map("/v1/host-data/{id}", hub.HostData);
app.MapPost("/v1/invitations", (HttpContext context, RelayStore store) => {
    var identity = store.Host(RelayHub.Bearer(context));
    if (identity == null) return Results.Unauthorized();
    if (!hub.Online(identity.Id)) return Results.Conflict();
    return Results.Json(store.Invite(identity.Id), RelayProtocol.Json);
});
app.MapPost("/v1/pair", (PairRequest request, RelayStore store) => {
    var paired = store.Pair(request);
    return paired == null ? Results.BadRequest(new { error = "Invalid, expired or consumed invitation; incompatible version; or device limit reached." }) : Results.Json(paired, RelayProtocol.Json);
});
await app.RunAsync();
