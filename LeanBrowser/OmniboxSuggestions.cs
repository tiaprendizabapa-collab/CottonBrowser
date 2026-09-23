using System.Drawing.Drawing2D;
using System.Text.Json;

namespace LeanBrowser;

internal sealed record SuggestionItem(string Text, string? Url);

/// <summary>Lista flutuante arredondada, controlada pelo teclado da omnibox.</summary>
internal sealed class SuggestionPanel : Control
{
    private const int RowHeight = 44;
    private readonly Font _titleFont = new(Theme.UiFont, 9.5f, FontStyle.Bold);
    private readonly Font _detailFont = new(Theme.UiFont, 9f);
    private readonly Dictionary<string, Image> _favicons = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<SuggestionItem> _items = Array.Empty<SuggestionItem>();
    private int _selected = -1;

    public event Action<SuggestionItem>? Chosen;
    public event Action<SuggestionItem>? Dismissed;
    public int SelectedIndex => _selected;
    public SuggestionItem? SelectedItem => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    public SuggestionPanel()
    {
        Visible = false;
        TabStop = false;
        Font = new Font(Theme.UiFont, 10f);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _detailFont.Dispose();
            foreach (var icon in _favicons.Values) icon.Dispose();
        }
        base.Dispose(disposing);
    }

    public void SetItems(IReadOnlyList<SuggestionItem> items)
    {
        _items = items;
        _selected = items.Count > 0 ? 0 : -1;
        Height = items.Count * RowHeight + 12;
        Visible = items.Count > 0;
        Invalidate();
    }

    public void CacheFavicon(string url, Image source)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host.Length == 0) return;
        var icon = new Bitmap(20, 20);
        using (var graphics = Graphics.FromImage(icon))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, 0, 0, 20, 20);
        }
        if (_favicons.Remove(uri.Host, out var previous)) previous.Dispose();
        if (_favicons.Count >= 32)
        {
            var oldest = _favicons.First();
            _favicons.Remove(oldest.Key);
            oldest.Value.Dispose();
        }
        _favicons[uri.Host] = icon;
        if (Visible) Invalidate();
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
        if (e.Y < 6 || row < 0 || row >= _items.Count) return;
        var item = _items[row];
        if (item.Url is not null && DismissBounds(row).Contains(e.Location)) Dismissed?.Invoke(item);
        else Chosen?.Invoke(item);
    }

    private Rectangle DismissBounds(int row) => new(Width - 40, 6 + row * RowHeight + 10, 24, 24);

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
                using var accent = new SolidBrush(Theme.Accent);
                g.FillRectangle(accent, 0, row.Y + 5, 3, row.Height - 10);
            }
            var item = _items[i];
            if (item.Url is null)
            {
                using var searchPen = new Pen(Theme.InkMuted, 1.6f);
                g.DrawEllipse(searchPen, 20, row.Y + 14, 11, 11);
                g.DrawLine(searchPen, 30, row.Y + 24, 35, row.Y + 29);
                TextRenderer.DrawText(g, item.Text, Font,
                    new Rectangle(49, row.Y, row.Width - 65, row.Height), Theme.Ink,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                continue;
            }

            var host = Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) ? uri.Host : item.Url;
            if (_favicons.TryGetValue(host, out var favicon))
                g.DrawImage(favicon, 19, row.Y + 12, 20, 20);
            else
            {
                var markHost = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
                var mark = markHost.Length > 0 ? markHost[0] : '•';
                using (var markBrush = new SolidBrush(Theme.SurfaceHot))
                    g.FillEllipse(markBrush, 15, row.Y + 8, 29, 29);
                TextRenderer.DrawText(g, char.ToUpperInvariant(mark).ToString(), _titleFont,
                    new Rectangle(15, row.Y + 8, 29, 29), Theme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            var maxTitleWidth = Math.Max(40, (row.Width - 135) / 2);
            var title = item.Text;
            var titleWidth = Math.Min(maxTitleWidth,
                TextRenderer.MeasureText(g, title, _titleFont, Size.Empty, TextFormatFlags.NoPadding).Width + 3);
            TextRenderer.DrawText(g, title, _titleFont,
                new Rectangle(54, row.Y, titleWidth, row.Height), Theme.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            var address = "  –  " + UrlHelper.ForDisplay(item.Url);
            var addressX = 54 + titleWidth;
            TextRenderer.DrawText(g, address, _detailFont,
                new Rectangle(addressX, row.Y, Math.Max(0, Width - addressX - 48), row.Height), Theme.Accent,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            var dismiss = DismissBounds(i);
            using var dismissPen = new Pen(Theme.InkMuted, 1.4f);
            g.DrawLine(dismissPen, dismiss.X + 7, dismiss.Y + 7, dismiss.Right - 7, dismiss.Bottom - 7);
            g.DrawLine(dismissPen, dismiss.Right - 7, dismiss.Y + 7, dismiss.X + 7, dismiss.Bottom - 7);
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

    public SuggestionItem? FindAddressCompletion(string query)
    {
        if (query.Length < 2 || query.Contains(' ')) return null;
        lock (_gate)
        {
            var match = _entries.FirstOrDefault(entry =>
            {
                var display = UrlHelper.ForDisplay(entry.Url);
                return display.Length > query.Length
                    && display.StartsWith(query, StringComparison.OrdinalIgnoreCase);
            });
            return match is null ? null : new SuggestionItem(
                string.IsNullOrWhiteSpace(match.Title) ? UrlHelper.ForDisplay(match.Url) : match.Title,
                match.Url);
        }
    }

    public void Remove(string url)
    {
        lock (_gate)
            _entries.RemoveAll(entry => string.Equals(entry.Url, url, StringComparison.OrdinalIgnoreCase));
        _ = SaveAsync();
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
