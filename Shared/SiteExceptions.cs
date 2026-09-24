using System.Net;
using System.Text.Json;

namespace CottonBrowser.Shared;

/// <summary>Exact web origin. A path such as /login authorizes the whole origin, not a URL prefix.</summary>
public static class SiteExceptionAddress
{
    public static bool TryNormalizeOrigin(string? input, out string origin)
    {
        origin = string.Empty;
        var value = input?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 ||
            value.Any(char.IsWhiteSpace) || value.Contains('\\')) return false;

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            var host = value.Split('/')[0].Split(':')[0];
            var local = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                IPAddress.TryParse(host, out var ip) && IsPrivateOrLoopbackIpv4(ip);
            value = (local ? "http://" : "https://") + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            string.IsNullOrEmpty(uri.Host) ||
            uri.Host.Equals("app.cottonbrowser.test", StringComparison.OrdinalIgnoreCase)) return false;

        origin = uri.GetLeftPart(UriPartial.Authority) + "/";
        return true;
    }

    public static bool IsCanonicalOrigin(string? input) =>
        TryNormalizeOrigin(input, out var normalized) &&
        string.Equals(input, normalized, StringComparison.OrdinalIgnoreCase);

    public static bool Matches(string origin, Uri request) =>
        request.Scheme is "http" or "https" &&
        string.Equals(origin, request.GetLeftPart(UriPartial.Authority) + "/",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsPrivateOrLoopbackIpv4(IPAddress ip)
    {
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var bytes = ip.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 127 ||
            bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
            bytes[0] == 192 && bytes[1] == 168;
    }
}

public sealed record SiteExceptionSnapshot(int Version, string[] AllowedOrigins, DateTimeOffset AppliedAt);

/// <summary>Same-Windows-user prototype transport. It is not a remote policy distribution service.</summary>
public sealed class SiteExceptionStore(string? path = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string PathOnDisk { get; } = path ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LeanBrowser", "site-exceptions.json");

    public SiteExceptionSnapshot Read()
    {
        if (!File.Exists(PathOnDisk))
            return new SiteExceptionSnapshot(1, [], DateTimeOffset.MinValue);

        var snapshot = JsonSerializer.Deserialize<SiteExceptionSnapshot>(File.ReadAllText(PathOnDisk), JsonOptions);
        if (snapshot is null || snapshot.Version != 1 || snapshot.AllowedOrigins is null ||
            snapshot.AllowedOrigins.Length > 200 ||
            snapshot.AllowedOrigins.Any(origin => !SiteExceptionAddress.IsCanonicalOrigin(origin)) ||
            snapshot.AllowedOrigins.Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.AllowedOrigins.Length)
            throw new InvalidDataException("Arquivo de exceções de sites inválido.");
        return snapshot;
    }

    public SiteExceptionSnapshot Write(IEnumerable<string> allowedOrigins)
    {
        var origins = allowedOrigins.ToArray();
        if (origins.Length > 200 || origins.Any(origin => !SiteExceptionAddress.IsCanonicalOrigin(origin)) ||
            origins.Distinct(StringComparer.OrdinalIgnoreCase).Count() != origins.Length)
            throw new ArgumentException("Origens inválidas ou duplicadas.", nameof(allowedOrigins));

        var snapshot = new SiteExceptionSnapshot(1, origins, DateTimeOffset.UtcNow);
        var directory = System.IO.Path.GetDirectoryName(PathOnDisk)
            ?? throw new InvalidOperationException("Caminho de exceções inválido.");
        Directory.CreateDirectory(directory);
        var temporary = PathOnDisk + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(temporary, PathOnDisk, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return snapshot;
    }
}

/// <summary>Refresh on top-level navigation; resource-request hot paths only read memory.</summary>
public sealed class SiteAllowlist(SiteExceptionStore store)
{
    private HashSet<string> _origins = new(StringComparer.OrdinalIgnoreCase);

    public void Refresh()
    {
        try
        {
            var next = new HashSet<string>(store.Read().AllowedOrigins, StringComparer.OrdinalIgnoreCase);
            Volatile.Write(ref _origins, next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            // Corrupt/unreadable policy fails closed rather than retaining a stale exception.
            Volatile.Write(ref _origins, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    public bool IsAllowed(Uri uri) => uri.Scheme is "http" or "https" &&
        Volatile.Read(ref _origins).Contains(uri.GetLeftPart(UriPartial.Authority) + "/");

    public bool IsAllowed(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsAllowed(uri);
}
