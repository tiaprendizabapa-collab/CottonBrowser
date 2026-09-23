using System.Drawing.Drawing2D;
using System.Text.Json;

namespace LeanBrowser;

internal sealed record SuggestionItem(string Text, string? Url);

/// <summary>Lista flutuante arredondada, controlada pelo teclado da omnibox.</summary>
internal sealed class SuggestionPanel : Control
{
    private const int RowHeight = 44;
    private readonly Font _detailFont = new(Theme.UiFont, 8f);
    private IReadOnlyList<SuggestionItem> _items = Array.Empty<SuggestionItem>();
    private int _selected = -1;

    public event Action<SuggestionItem>? Chosen;
    public int SelectedIndex => _selected;
    public SuggestionItem? SelectedItem => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    public SuggestionPanel()
    {
        Visible = false;
        TabStop = false;
        Font = new Font(Theme.UiFont, 10f);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _detailFont.Dispose();
        base.Dispose(disposing);
    }

    public void SetItems(IReadOnlyList<SuggestionItem> items)
    {
        _items = items;
        _selected = -1;
        Height = items.Count * RowHeight + 12;
        Visible = items.Count > 0;
        Invalidate();
    }

    public void MoveSelection(int direction)
    {
        if (_items.Count == 0) return;
        _selected = Math.Clamp(_selected + direction, -1, _items.Count - 1);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width < 2 || Height < 2) return;
        using var path = Draw.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var row = (e.Y - 6) / RowHeight;
        if (e.Y < 6 || row >= _items.Count) row = -1;
        if (_selected == row) return;
        _selected = row;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var row = (e.Y - 6) / RowHeight;
        if (e.Y >= 6 && row >= 0 && row < _items.Count) Chosen?.Invoke(_items[row]);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Surface);
        using var border = new Pen(Theme.Divider);
        using var path = Draw.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        g.DrawPath(border, path);
        for (var i = 0; i < _items.Count; i++)
        {
            var row = new Rectangle(5, 6 + i * RowHeight, Width - 10, RowHeight);
            if (i == _selected)
            {
                using var hotPath = Draw.RoundedRect(new Rectangle(row.X, row.Y, row.Width, row.Height - 1), 8);
                using var hot = new SolidBrush(Theme.SurfaceHot);
                g.FillPath(hot, hotPath);
            }
            var item = _items[i];
            TextRenderer.DrawText(g, item.Url is null ? "⌕" : "↶", Font,
                new Rectangle(18, row.Y, 24, row.Height), Theme.InkMuted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var text = new Rectangle(48, row.Y + (item.Url is null ? 0 : 3), row.Width - 58,
                item.Url is null ? row.Height : 21);
            TextRenderer.DrawText(g, item.Text, Font, text, Theme.Ink,
                (item.Url is null ? TextFormatFlags.VerticalCenter : TextFormatFlags.Top)
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (item.Url is not null)
                TextRenderer.DrawText(g, UrlHelper.ForDisplay(item.Url), _detailFont,
                    new Rectangle(48, row.Y + 24, row.Width - 58, 17), Theme.InkMuted,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>Histórico pequeno e local; leitura e gravação fora da thread de UI.</summary>
internal sealed class NavigationHistoryStore
{
    private readonly string _path;
    private readonly Task _ready;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private List<HistoryEntry> _entries = new();

    private sealed record HistoryEntry(string Url, string Title, DateTimeOffset VisitedAt);

    public NavigationHistoryStore(string path)
    {
        _path = path;
        _ready = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var loaded = await Task.Run(() => File.Exists(_path)
                ? JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(_path))
                : null).ConfigureAwait(false);
            if (loaded is null) return;
            lock (_gate)
            {
                _entries = loaded.Concat(_entries)
                    .Where(entry => entry is not null && BookmarkStore.IsWebUrl(entry.Url))
                    .GroupBy(entry => entry.Url, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.MaxBy(entry => entry.VisitedAt)!)
                    .OrderByDescending(entry => entry.VisitedAt).Take(200).ToList();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    public IReadOnlyList<SuggestionItem> Find(string query)
    {
        lock (_gate)
            return _entries.Where(entry => entry.Url.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || entry.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                .Take(4)
                .Select(entry => new SuggestionItem(
                    string.IsNullOrWhiteSpace(entry.Title) ? UrlHelper.ForDisplay(entry.Url) : entry.Title,
                    entry.Url))
                .ToArray();
    }

    public void Record(string url, string title)
    {
        if (!BookmarkStore.IsWebUrl(url)) return;
        lock (_gate)
        {
            _entries.RemoveAll(entry => string.Equals(entry.Url, url, StringComparison.OrdinalIgnoreCase));
            _entries.Insert(0, new HistoryEntry(url, title, DateTimeOffset.UtcNow));
            if (_entries.Count > 200) _entries.RemoveRange(200, _entries.Count - 200);
        }
        _ = SaveAsync();
    }

    private async Task SaveAsync()
    {
        await _ready.ConfigureAwait(false);
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            HistoryEntry[] snapshot;
            lock (_gate) snapshot = _entries.ToArray();
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
                File.Move(temp, _path, true);
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { _writeGate.Release(); }
    }
}

/// <summary>Cliente único para sugestões; falha de rede apenas mantém o histórico.</summary>
internal sealed class SearchSuggestionClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public async Task<IReadOnlyList<SuggestionItem>> FetchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 160) return Array.Empty<SuggestionItem>();
        try
        {
            var url = "https://duckduckgo.com/ac/?q=" + Uri.EscapeDataString(query);
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (json.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<SuggestionItem>();
            return json.RootElement.EnumerateArray()
                .Where(item => item.TryGetProperty("phrase", out var phrase)
                    && phrase.ValueKind == JsonValueKind.String)
                .Select(item => new SuggestionItem(item.GetProperty("phrase").GetString()!, null))
                .Where(item => !string.IsNullOrWhiteSpace(item.Text))
                .Take(8).ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or ObjectDisposedException)
        { return Array.Empty<SuggestionItem>(); }
    }

    public void Dispose() => _http.Dispose();
}
