using System.Globalization;
using System.Text.Json;

namespace LeanBrowser;

internal sealed record SuggestionItem(string Text, string? Url);
internal sealed record NavigationHistoryEntry(string Url, string Title, DateTimeOffset VisitedAt);

/// <summary>Histórico de visitas, compatível com o JSON anterior; leitura e gravação fora da UI.</summary>
internal sealed class NavigationHistoryStore
{
    public const int MaximumEntries = 10000;
    private readonly string _path;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly List<Action<List<NavigationHistoryEntry>>> _pendingMutations = new();
    private List<NavigationHistoryEntry> _entries = new();
    private bool _loaded;
    private bool _mayWrite = true;
    private long _version;
    private long _savedVersion = -1;

    public event Action? Changed;
    public Task Ready { get; }

    public NavigationHistoryStore(string path)
    {
        _path = Path.GetFullPath(path);
        Ready = LoadAsync();
    }

    private async Task LoadAsync()
    {
        List<NavigationHistoryEntry>? loaded = null;
        try
        {
            loaded = await Task.Run(() => File.Exists(_path)
                ? JsonSerializer.Deserialize<List<NavigationHistoryEntry>>(File.ReadAllText(_path))
                : null).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Preserva o arquivo inválido para recuperação antes de começar um histórico novo.
            try
            {
                var backup = _path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json";
                await Task.Run(() => File.Copy(_path, backup)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _mayWrite = false; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _mayWrite = false; }
        lock (_gate)
        {
            _entries = (loaded ?? []).Where(entry => entry is not null && BookmarkStore.IsWebUrl(entry.Url))
                .Select(entry => entry with { Title = entry.Title ?? string.Empty })
                .Distinct().OrderByDescending(entry => entry.VisitedAt).Take(MaximumEntries).ToList();
            // As exclusões feitas enquanto o arquivo é lido também precisam atingir os dados carregados.
            foreach (var mutate in _pendingMutations) mutate(_entries);
            _pendingMutations.Clear();
            _loaded = true;
        }
        Changed?.Invoke();
    }

    public IReadOnlyList<NavigationHistoryEntry> Search(string query = "")
    {
        var compare = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;
        query = query.Trim();
        lock (_gate)
            return _entries.Where(entry => query.Length == 0
                || compare.IndexOf(entry.Url, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0
                || compare.IndexOf(entry.Title, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
                .ToArray();
    }

    public IReadOnlyList<SuggestionItem> Find(string query) => Search(query)
        .DistinctBy(entry => entry.Url, StringComparer.OrdinalIgnoreCase).Take(4)
        .Select(entry => new SuggestionItem(TitleFor(entry), entry.Url)).ToArray();

    public SuggestionItem? FindAddressCompletion(string query)
    {
        if (query.Length < 2 || query.Contains(' ')) return null;
        lock (_gate)
        {
            var match = _entries.FirstOrDefault(entry =>
            {
                var display = UrlHelper.ForDisplay(entry.Url);
                return display.Length > query.Length && display.StartsWith(query, StringComparison.OrdinalIgnoreCase);
            });
            return match is null ? null : new SuggestionItem(TitleFor(match), match.Url);
        }
    }

    private static string TitleFor(NavigationHistoryEntry entry) => string.IsNullOrWhiteSpace(entry.Title)
        ? UrlHelper.ForDisplay(entry.Url) : entry.Title;

    public void Record(string url, string title)
    {
        if (!BookmarkStore.IsWebUrl(url)) return;
        var entry = new NavigationHistoryEntry(url, title ?? string.Empty, DateTimeOffset.UtcNow);
        Mutate(entries =>
        {
            entries.Insert(0, entry);
            if (entries.Count > MaximumEntries) entries.RemoveRange(MaximumEntries, entries.Count - MaximumEntries);
        });
    }

    public void Remove(string url) => Mutate(entries => entries.RemoveAll(entry =>
        string.Equals(entry.Url, url, StringComparison.OrdinalIgnoreCase)));

    public void RemoveEntries(IEnumerable<NavigationHistoryEntry> selection)
    {
        var entriesToRemove = selection.ToHashSet();
        if (entriesToRemove.Count == 0) return;
        Mutate(entries => entries.RemoveAll(entriesToRemove.Contains));
    }

    /// <summary>Intervalo com início inclusivo e fim exclusivo; null não limita aquela ponta.</summary>
    public void RemoveRange(DateTimeOffset? from, DateTimeOffset? until)
    {
        if (from.HasValue && until.HasValue && from >= until) return;
        Mutate(entries => entries.RemoveAll(entry => (!from.HasValue || entry.VisitedAt >= from)
            && (!until.HasValue || entry.VisitedAt < until)));
    }

    private void Mutate(Action<List<NavigationHistoryEntry>> mutate)
    {
        lock (_gate)
        {
            mutate(_entries);
            if (!_loaded) _pendingMutations.Add(mutate);
            _version++;
        }
        Changed?.Invoke();
        _ = FlushAsync();
    }

    public async Task FlushAsync()
    {
        await Ready.ConfigureAwait(false);
        if (!_mayWrite) return;
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            NavigationHistoryEntry[] snapshot;
            long version;
            lock (_gate)
            {
                version = _version;
                if (_savedVersion == version) return;
                snapshot = _entries.ToArray();
            }
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
                    File.Move(temporary, _path, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }).ConfigureAwait(false);
            lock (_gate) _savedVersion = version;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { _writeGate.Release(); }
    }
}
