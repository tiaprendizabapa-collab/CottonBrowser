using System.Text.Json;

namespace LeanBrowser;

internal sealed record UpdateCheckHistory(DateTimeOffset CheckedAt, bool Succeeded, BrowserReleaseInfo? Release);

internal sealed class UpdateCheckHistoryStore
{
    private readonly string _path;

    public UpdateCheckHistoryStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LeanBrowser", "update-check.json");

    public UpdateCheckHistory? Load()
    {
        try
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length > 256 * 1024) return null;
            var history = JsonSerializer.Deserialize<UpdateCheckHistory>(File.ReadAllText(_path));
            if (history is null || history.CheckedAt == default) return null;
            if (history.Release is { } release)
            {
                if (release.Version is null || release.Version.Build < 0 || string.IsNullOrWhiteSpace(release.Tag)) return null;
                var notes = release.Notes ?? string.Empty;
                history = history with { Release = release with { Notes = notes[..Math.Min(notes.Length, 24000)] } };
            }
            return history;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public bool Save(UpdateCheckHistory history)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(history));
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
