using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LeanBrowser;

public enum UrlReputationVerdict
{
    Allowed,
    Malicious,
    Unknown
}

public interface IUrlReputationService
{
    Task<UrlReputationVerdict> CheckAsync(Uri uri, CancellationToken cancellationToken);
}

/// <summary>
/// Enterprise URL reputation provider.  Web Risk is the commercial Google
/// service intended for this scenario; the non-commercial Safe Browsing API
/// is not used by a corporate browser.
/// </summary>
public sealed class WebRiskReputationService : IUrlReputationService, IDisposable
{
    private static readonly Uri Endpoint = new("https://webrisk.googleapis.com/v1/uris:search");
    private readonly ISecretStore _secrets;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public WebRiskReputationService(ISecretStore secrets, HttpClient? httpClient = null)
    {
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    }

    public async Task<UrlReputationVerdict> CheckAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!IsExternalHttpUri(uri)) return UrlReputationVerdict.Allowed;

        var cacheKey = uri.AbsoluteUri;
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached.Verdict;

        if (!_secrets.TryOpen(SecureSecretId.WebRiskApiKey, out var apiKey) || apiKey is null)
            return UrlReputationVerdict.Unknown;

        try
        {
            using (apiKey)
            {
                var requestUri = BuildLookupUri(uri, apiKey.CopyToString());
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (!response.IsSuccessStatusCode) return UrlReputationVerdict.Unknown;

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var verdict = ParseVerdict(body, out var expiresAt);
                _cache[cacheKey] = new CacheEntry(verdict, expiresAt);
                return verdict;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return UrlReputationVerdict.Unknown;
        }
        catch (HttpRequestException)
        {
            return UrlReputationVerdict.Unknown;
        }
        catch (JsonException)
        {
            return UrlReputationVerdict.Unknown;
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private static Uri BuildLookupUri(Uri target, string apiKey)
    {
        var query = new[]
        {
            "threatTypes=MALWARE",
            "threatTypes=SOCIAL_ENGINEERING",
            "threatTypes=UNWANTED_SOFTWARE",
            "uri=" + Uri.EscapeDataString(target.AbsoluteUri),
            "key=" + Uri.EscapeDataString(apiKey)
        };
        return new Uri(Endpoint + "?" + string.Join('&', query));
    }

    private static UrlReputationVerdict ParseVerdict(string body, out DateTimeOffset expiresAt)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        expiresAt = GetExpiry(root);
        return root.TryGetProperty("threat", out var threat) && threat.ValueKind == JsonValueKind.Object
            ? UrlReputationVerdict.Malicious
            : UrlReputationVerdict.Allowed;
    }

    private static DateTimeOffset GetExpiry(JsonElement root)
    {
        var fallback = DateTimeOffset.UtcNow.AddMinutes(5);
        if (!root.TryGetProperty("expireTime", out var value) || value.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return fallback;

        var now = DateTimeOffset.UtcNow;
        return parsed > now && parsed <= now.AddHours(24) ? parsed : fallback;
    }

    private static bool IsExternalHttpUri(Uri uri) =>
        uri.IsAbsoluteUri
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.Equals(uri.Host, TrustedBrowserBridge.HostName, StringComparison.OrdinalIgnoreCase);

    private readonly record struct CacheEntry(UrlReputationVerdict Verdict, DateTimeOffset ExpiresAt);
}

/// <summary>
/// Intercepts every HTTP resource before it is sent and upgrades it to HTTPS.
/// Document requests are then held with a WebView2 deferral until the URL is
/// cleared by the reputation provider.  Unknown status blocks by default.
/// </summary>
public sealed class NetworkProtection : IDisposable
{
    private static readonly byte[] BlockedPage = """
        <!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><title>Site bloqueado</title></head>
        <body><h1>Site bloqueado</h1><p>O navegador bloqueou esta navegação por política de segurança.</p></body></html>
        """u8.ToArray();

    private static readonly byte[] VerificationUnavailablePage = """
        <!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><title>Verificação indisponível</title></head>
        <body><h1>Navegação não verificada</h1><p>A reputação desta URL não pôde ser confirmada. A política corporativa bloqueou o carregamento.</p></body></html>
        """u8.ToArray();

    private readonly CoreWebView2 _core;
    private readonly IUrlReputationService _reputation;
    private int _disposed;

    public NetworkProtection(WebView2 control, IUrlReputationService reputation)
    {
        ArgumentNullException.ThrowIfNull(control);
        _reputation = reputation ?? throw new ArgumentNullException(nameof(reputation));
        _core = control.CoreWebView2
            ?? throw new InvalidOperationException("CoreWebView2 must be initialized before attaching network protection.");

        // This catches every clear-text HTTP resource, including frames,
        // scripts, styles, images, fetch/XHR, and documents.
        _core.AddWebResourceRequestedFilter("http://*/*", CoreWebView2WebResourceContext.All);
        // Reputation is intentionally limited to documents: querying an online
        // service for every image or script would be both slow and excessive.
        _core.AddWebResourceRequestedFilter("https://*/*", CoreWebView2WebResourceContext.Document);
        _core.WebResourceRequested += OnWebResourceRequested;
    }

    private async void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        Uri target;
        try
        {
            if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var parsedTarget) || parsedTarget is null)
            {
                Block(args, BlockedPage, 400, "Invalid URL");
                return;
            }
            target = parsedTarget;

            if (target.Scheme == Uri.UriSchemeHttp)
            {
                if (!TryUpgradeToHttps(target, out target))
                {
                    Block(args, BlockedPage, 403, "HTTPS required");
                    return;
                }
                args.Request.Uri = target.AbsoluteUri;
            }

            if (args.ResourceContext != CoreWebView2WebResourceContext.Document
                || IsTrustedInternalUri(target))
                return;
        }
        catch
        {
            Block(args, VerificationUnavailablePage, 503, "Security check failed");
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var verdict = await _reputation.CheckAsync(target, timeout.Token);
            if (verdict == UrlReputationVerdict.Malicious)
                Block(args, BlockedPage, 451, "Blocked by URL reputation");
            else if (verdict != UrlReputationVerdict.Allowed)
                Block(args, VerificationUnavailablePage, 503, "URL reputation unavailable");
        }
        catch
        {
            Block(args, VerificationUnavailablePage, 503, "URL reputation unavailable");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void Block(CoreWebView2WebResourceRequestedEventArgs args, byte[] page, int statusCode, string reasonPhrase)
    {
        args.Response = _core.Environment.CreateWebResourceResponse(
            new MemoryStream(page, writable: false),
            statusCode,
            reasonPhrase,
            "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff");
    }

    private static bool TryUpgradeToHttps(Uri source, out Uri upgraded)
    {
        upgraded = source;
        if (!string.Equals(source.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(source.UserInfo))
            return false;

        var builder = new UriBuilder(source)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = source.IsDefaultPort || source.Port == 80 ? -1 : source.Port
        };
        upgraded = builder.Uri;
        return true;
    }

    private static bool IsTrustedInternalUri(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.Host, TrustedBrowserBridge.HostName, StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _core.WebResourceRequested -= OnWebResourceRequested;
    }
}
