using System.Net;

namespace LeanBrowser;

/// <summary>
/// Produces a telemetry-safe representation of a URL. Fragments are never
/// included, since OAuth implicit-flow tokens may be placed after '#'.
/// </summary>
internal static class TelemetryUrlSanitizer
{
    internal static readonly IReadOnlySet<string> DefaultSensitiveKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "token",
            "access_token",
            "refresh_token",
            "id_token",
            "code",
            "client_secret",
            "assertion",
            "saml",
            "password",
            "secret"
        };

    /// <summary>
    /// Redacts the values of matching query-string keys while retaining useful
    /// non-sensitive navigation data such as the host and page path.
    /// </summary>
    internal static string Sanitize(Uri uri, IEnumerable<string> sensitiveKeys)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(sensitiveKeys);

        var keys = sensitiveKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => WebUtility.UrlDecode(key.Trim()))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // GetLeftPart(UriPartial.Path) intentionally excludes query and fragment.
        var path = uri.GetLeftPart(UriPartial.Path);
        var query = uri.Query.TrimStart('?');
        if (query.Length == 0) return path;

        var safePairs = new List<string>();
        foreach (var pair in query.Split('&', StringSplitOptions.None))
        {
            var separator = pair.IndexOf('=');
            var rawKey = separator >= 0 ? pair[..separator] : pair;
            var decodedKey = WebUtility.UrlDecode(rawKey.Replace('+', ' '));

            // Preserve the original spelling of the key, but never its value.
            safePairs.Add(keys.Contains(decodedKey) ? $"{rawKey}=***" : pair);
        }

        return safePairs.Count == 0 ? path : $"{path}?{string.Join("&", safePairs)}";
    }
}
