using System.Text.Json;

namespace LeanBrowser;

/// <summary>Explicit portable allowlist: never copies passwords, cookies or arbitrary profile files.</summary>
public static class BrowserBackup
{
    private sealed record BackupDocument(int Format, DateTimeOffset CreatedAt, Dictionary<string, string> Files);
    private static Dictionary<string, string> Paths(string data, string theme) => new()
    {
        ["preferences.json"] = Path.Combine(data, "preferences.json"),
        ["bookmarks.json"] = Path.Combine(data, "bookmarks.json"),
        ["bookmarks.json.folders.json"] = Path.Combine(data, "bookmarks.json.folders.json"),
        ["productivity.json"] = Path.Combine(data, "productivity.json"),
        ["theme.txt"] = Path.Combine(theme, "theme.txt"), ["accent.txt"] = Path.Combine(theme, "accent.txt")
    };

    public static void Export(string destination, string data, string theme)
    {
        var files = Paths(data, theme).Where(p => File.Exists(p.Value)).ToDictionary(p => p.Key, p => File.ReadAllText(p.Value));
        Validate(files);
        var text = JsonSerializer.Serialize(new BackupDocument(1, DateTimeOffset.UtcNow, files), new JsonSerializerOptions { WriteIndented = true });
        if (System.Text.Encoding.UTF8.GetByteCount(text) > 12 * 1024 * 1024) throw new InvalidDataException("O backup excede o limite de 12 MB.");
        AtomicFile.Write(destination, text);
    }

    public static void Restore(string source, string data, string theme)
    {
        if (new FileInfo(source).Length > 12 * 1024 * 1024) throw new InvalidDataException("O backup deve ter até 12 MB.");
        var document = JsonSerializer.Deserialize<BackupDocument>(File.ReadAllText(source)) ?? throw new InvalidDataException("Backup inválido.");
        if (document.Format != 1 || document.Files is null) throw new InvalidDataException("Formato de backup não reconhecido.");
        Validate(document.Files); // Validate every file before changing any user data.
        var paths = Paths(data, theme);
        var previous = document.Files.Keys.ToDictionary(key => key, key => File.Exists(paths[key]) ? File.ReadAllText(paths[key]) : null);
        var changed = new List<string>();
        try
        {
            foreach (var (key, text) in document.Files) { AtomicFile.Write(paths[key], text); changed.Add(key); }
        }
        catch
        {
            foreach (var key in changed.AsEnumerable().Reverse())
            {
                if (previous[key] is { } old) AtomicFile.Write(paths[key], old);
                else File.Delete(paths[key]);
            }
            throw;
        }
    }

    private static void Validate(Dictionary<string, string> files)
    {
        var allowed = Paths(".", ".");
        if (files.Count > allowed.Count || files.Any(p => !allowed.ContainsKey(p.Key) || p.Value is null || p.Value.Length > 8 * 1024 * 1024))
            throw new InvalidDataException("O backup contém arquivos não permitidos.");
        foreach (var (key, text) in files)
        {
            switch (key)
            {
                case "preferences.json":
                    using (var json = JsonDocument.Parse(text))
                    {
                        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Preferências inválidas.");
                    }
                    // Deserialize using the same typed schema as the live preferences.
                    BrowserPreferences.ValidateJson(text);
                    break;
                case "bookmarks.json":
                    var bookmarks = JsonSerializer.Deserialize<Bookmark[]>(text) ?? throw new InvalidDataException("Favoritos inválidos.");
                    if (bookmarks.Length > 20000 || bookmarks.Any(b => b is null || !BookmarkStore.IsWebUrl(b.Url)
                        || b.Title is null || b.Title.Length > 10000)) throw new InvalidDataException("Favoritos inválidos.");
                    foreach (var bookmark in bookmarks) BookmarkStore.NormalizeFolder(bookmark.Folder);
                    break;
                case "bookmarks.json.folders.json":
                    var folders = JsonSerializer.Deserialize<string[]>(text) ?? throw new InvalidDataException("Pastas inválidas.");
                    if (folders.Length > 20000) throw new InvalidDataException("Muitas pastas no backup.");
                    foreach (var folder in folders) BookmarkStore.NormalizeFolder(folder);
                    break;
                case "productivity.json":
                    ProductivityStore.Validate(JsonSerializer.Deserialize<ProductivityDocument>(text) ?? throw new InvalidDataException("Organização inválida."));
                    break;
                case "theme.txt":
                    if (text.Trim() is not ("dark" or "light")) throw new InvalidDataException("Tema inválido.");
                    break;
                case "accent.txt":
                    if (text.Trim().Length != 7 || !text.Trim().StartsWith('#')
                        || !int.TryParse(text.Trim()[1..], System.Globalization.NumberStyles.HexNumber, null, out _))
                        throw new InvalidDataException("Cor inválida.");
                    break;
            }
        }
    }
}
