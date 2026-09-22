using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeanBrowser;

public enum DownloadStatus
{
    InProgress,
    Completed,
    Interrupted
}

public sealed record DownloadEntry(
    Guid Id,
    string FileName,
    string SourceUrl,
    string FilePath,
    long BytesReceived,
    long TotalBytes,
    DownloadStatus Status,
    string? Detail,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>Histórico local dos downloads, limitado para não crescer indefinidamente.</summary>
public sealed class DownloadHistoryStore
{
    private const int MaxEntries = 500;
    private readonly string _path;
    private readonly object _sync = new();
    private readonly List<DownloadEntry> _entries;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public DownloadHistoryStore(string path)
    {
        _path = path;
        _entries = LoadEntries();
    }

    public IReadOnlyList<DownloadEntry> Snapshot()
    {
        lock (_sync) return _entries.ToArray();
    }

    public void Upsert(DownloadEntry entry)
    {
        lock (_sync)
        {
            _entries.RemoveAll(item => item.Id == entry.Id);
            _entries.Insert(0, entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
            SaveEntries();
        }
    }

    private List<DownloadEntry> LoadEntries()
    {
        try
        {
            if (!File.Exists(_path)) return new List<DownloadEntry>();
            var entries = JsonSerializer.Deserialize<List<DownloadEntry>>(File.ReadAllText(_path), JsonOptions)
                ?? new List<DownloadEntry>();
            return entries
                .Where(IsValid)
                .Select(entry => entry.Status == DownloadStatus.InProgress
                    ? entry with { Status = DownloadStatus.Interrupted, Detail = "Interrompido ao fechar o navegador" }
                    : entry)
                .OrderByDescending(entry => entry.StartedAt)
                .Take(MaxEntries)
                .ToList();
        }
        catch (Exception) when (File.Exists(_path))
        {
            return new List<DownloadEntry>();
        }
    }

    private void SaveEntries()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(_entries, JsonOptions));
                File.Move(temporary, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool IsValid(DownloadEntry entry) =>
        entry.Id != Guid.Empty
        && !string.IsNullOrWhiteSpace(entry.FileName)
        && !string.IsNullOrWhiteSpace(entry.SourceUrl)
        && !string.IsNullOrWhiteSpace(entry.FilePath)
        && entry.BytesReceived >= 0
        && entry.TotalBytes >= -1;
}
