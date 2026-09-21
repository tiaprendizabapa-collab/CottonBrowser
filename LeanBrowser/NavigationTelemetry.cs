using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace LeanBrowser;

/// <summary>
/// Evento de telemetria de navegação. O endereço é enviado somente quando a
/// integração é explicitamente configurada por endpoint e token.
/// </summary>
public sealed record NavigationTelemetryEvent(
    string EventType,
    string UserId,
    string SessionId,
    string Url,
    string Domain,
    string? SearchTerm,
    string? Title,
    bool IsActive,
    DateTimeOffset OccurredAt);

/// <summary>
/// Captura eventos em uma fila limitada e os transmite em lotes fora da UI.
/// Falhas de rede descartam o lote atual para que a navegação continue fluida.
/// </summary>
public sealed class NavigationTelemetry : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Channel<NavigationTelemetryEvent> _queue = Channel.CreateBounded<NavigationTelemetryEvent>(
        new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly CancellationTokenSource _stop = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly Uri? _endpoint;
    private readonly string? _token;
    private readonly string _userId;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly Task _worker;

    public bool IsConfigured => _endpoint is not null && !string.IsNullOrWhiteSpace(_token);
    public int PendingCount => _queue.Reader.Count;

    public NavigationTelemetry(string userId)
    {
        _userId = string.IsNullOrWhiteSpace(userId) ? Environment.UserName : userId;
        _endpoint = ParseEndpoint(Environment.GetEnvironmentVariable("COTTON_MONITORING_ENDPOINT"));
        _token = Environment.GetEnvironmentVariable("COTTON_MONITORING_TOKEN");
        _worker = Task.Run(ConsumeAsync);
    }

    public void RecordNavigation(string? url, string? title, bool isActive)
    {
        Record("navigation", url, title, isActive);
    }

    public void RecordActiveTab(string? url, string? title)
    {
        Record("active_tab", url, title, true);
    }

    private void Record(string eventType, string? url, string? title, bool isActive)
    {
        if (!IsConfigured || !BookmarkStore.IsWebUrl(url)) return;

        try
        {
            var uri = new Uri(url!);
            var safeUrl = uri.GetLeftPart(UriPartial.Path) + uri.Query;
            var search = SearchTermParser.TryExtract(uri);
            _queue.Writer.TryWrite(new NavigationTelemetryEvent(
                eventType,
                _userId,
                _sessionId,
                safeUrl,
                uri.Host,
                search,
                string.IsNullOrWhiteSpace(title) ? null : title[..Math.Min(300, title.Length)],
                isActive,
                DateTimeOffset.UtcNow));
        }
        catch (UriFormatException) { }
    }

    private async Task ConsumeAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
            {
                var batch = new List<NavigationTelemetryEvent>(32);
                while (batch.Count < 32 && _queue.Reader.TryRead(out var item)) batch.Add(item);
                if (batch.Count > 0) await SendBatchAsync(batch, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (ChannelClosedException) { }
    }

    private async Task SendBatchAsync(IReadOnlyList<NavigationTelemetryEvent> batch, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return;

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Content = new StringContent(JsonSerializer.Serialize(batch, JsonOptions), Encoding.UTF8, "application/json");

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            // 5xx costuma ser transitório; uma única tentativa evita acumular
            // histórico local ou gastar recursos durante uma indisponibilidade.
            if ((int)response.StatusCode >= 500)
                await Task.Delay(350, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
    }

    private static Uri? ParseEndpoint(string? raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https")) return null;
        return uri;
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _stop.Cancel();
        try { _worker.Wait(TimeSpan.FromMilliseconds(250)); } catch { }
        _http.Dispose();
        _stop.Dispose();
    }
}

internal static class SearchTermParser
{
    private static readonly HashSet<string> SearchHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "google.com", "www.google.com", "google.com.br", "www.google.com.br",
        "bing.com", "www.bing.com",
        "duckduckgo.com", "www.duckduckgo.com", "search.yahoo.com",
        "youtube.com", "www.youtube.com"
    };

    public static string? TryExtract(Uri uri)
    {
        if (!SearchHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))) return null;

        var keys = uri.Host.Contains("youtube", StringComparison.OrdinalIgnoreCase)
            ? new[] { "search_query", "q" } : new[] { "q", "query", "p" };
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = pair.Split('=', 2);
            var key = WebUtility.UrlDecode(pieces[0]);
            if (!keys.Contains(key, StringComparer.OrdinalIgnoreCase) || pieces.Length != 2) continue;
            var value = WebUtility.UrlDecode(pieces[1]).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value[..Math.Min(500, value.Length)];
        }
        return null;
    }
}
