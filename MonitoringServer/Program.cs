using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddSignalR().AddJsonProtocol(options =>
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<NavigationLogStore>();
builder.Services.AddAuthentication("MonitoringBearer")
    .AddScheme<AuthenticationSchemeOptions, MonitoringBearerHandler>("MonitoringBearer", _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Ingest", policy => policy.RequireRole("User", "Admin"));
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "cotton-monitoring" }));
app.MapGet("/admin", () => Results.Redirect("/admin.html"));

app.MapPost("/api/telemetry/navigation", async (
    IReadOnlyList<NavigationTelemetryEvent> events,
    NavigationLogStore store,
    IHubContext<NavigationHub> hub,
    HttpContext http) =>
{
    if (events.Count == 0 || events.Count > 100) return Results.BadRequest(new { error = "O lote deve conter de 1 a 100 eventos." });
    var valid = events.Where(NavigationTelemetryEvent.IsValid).ToArray();
    if (valid.Length == 0) return Results.BadRequest(new { error = "Nenhum evento válido." });

    store.Append(valid);
    foreach (var item in valid)
        await hub.Clients.Group("admins").SendAsync("navigation", item);
    return Results.Accepted(value: new { accepted = valid.Length });
}).RequireAuthorization("Ingest");

app.MapGet("/api/admin/reports/top-sites", (NavigationLogStore store, int? limit) =>
    Results.Ok(store.TopSites(Math.Clamp(limit ?? 5, 1, 50)))).RequireAuthorization("Admin");

app.MapGet("/api/admin/reports/searches", (NavigationLogStore store, int? limit) =>
    Results.Ok(store.LatestSearches(Math.Clamp(limit ?? 50, 1, 200)))).RequireAuthorization("Admin");

app.MapHub<NavigationHub>("/hubs/navigation").RequireAuthorization("Admin");
app.Run();

public sealed record NavigationTelemetryEvent(
    string EventType,
    string UserId,
    string SessionId,
    string Url,
    string Domain,
    string? SearchTerm,
    string? Title,
    bool IsActive,
    DateTimeOffset OccurredAt)
{
    public static bool IsValid(NavigationTelemetryEvent item) =>
        item is not null && item.UserId.Length is > 0 and <= 256 &&
        item.SessionId.Length is > 0 and <= 80 && item.Url.Length is > 0 and <= 4096 &&
        Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) &&
        item.Domain.Length is > 0 and <= 255;
}

public sealed class NavigationLogStore
{
    private const int Capacity = 50_000;
    private readonly ConcurrentQueue<NavigationTelemetryEvent> _events = new();

    public void Append(IEnumerable<NavigationTelemetryEvent> items)
    {
        foreach (var item in items)
        {
            _events.Enqueue(item);
            while (_events.Count > Capacity) _events.TryDequeue(out _);
        }
    }

    public IReadOnlyList<DomainCount> TopSites(int limit) => _events
        .Where(item => item.EventType == "navigation")
        .GroupBy(item => item.Domain, StringComparer.OrdinalIgnoreCase)
        .Select(group => new DomainCount(group.Key, group.Count()))
        .OrderByDescending(item => item.Accesses)
        .Take(limit)
        .ToArray();

    public IReadOnlyList<NavigationTelemetryEvent> LatestSearches(int limit) => _events
        .Where(item => !string.IsNullOrWhiteSpace(item.SearchTerm))
        .OrderByDescending(item => item.OccurredAt)
        .Take(limit)
        .ToArray();
}

public sealed record DomainCount(string Domain, int Accesses);

public sealed class NavigationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "admins");
        await base.OnConnectedAsync();
    }
}

public sealed class MonitoringBearerHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public MonitoringBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Headers.Authorization.ToString();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token[7..].Trim();
        if (string.IsNullOrWhiteSpace(token) && Request.Path.StartsWithSegments("/hubs/navigation"))
            token = Request.Query["access_token"].ToString();
        if (string.IsNullOrWhiteSpace(token)) return Task.FromResult(AuthenticateResult.NoResult());

        var adminToken = Environment.GetEnvironmentVariable("COTTON_ADMIN_TOKEN");
        var ingestToken = Environment.GetEnvironmentVariable("COTTON_INGEST_TOKEN");
        var role = ConstantTimeEquals(token, adminToken) ? "Admin" :
            ConstantTimeEquals(token, ingestToken) ? "User" : null;
        if (role is null) return Task.FromResult(AuthenticateResult.Fail("Token inválido."));

        var identity = new ClaimsIdentity(Scheme.Name);
        identity.AddClaim(new Claim(ClaimTypes.Name, role));
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    private static bool ConstantTimeEquals(string provided, string? expected)
    {
        if (string.IsNullOrEmpty(expected)) return false;
        var left = Encoding.UTF8.GetBytes(provided);
        var right = Encoding.UTF8.GetBytes(expected);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
