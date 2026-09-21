using System.Text.Json;

namespace LeanBrowser;

public sealed record Bookmark(string Url, string Title);

/// <summary>Use na thread da UI; relê o arquivo antes de cada alteração.</summary>
public sealed class BookmarkStore(string path)
{
    public IReadOnlyList<Bookmark> Load()
    {
        if (!File.Exists(path)) return Array.Empty<Bookmark>();
        var items = JsonSerializer.Deserialize<List<Bookmark>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Arquivo de favoritos inválido.");
        if (items.Any(b => b is null || !IsWebUrl(b.Url) || b.Title is null))
            throw new InvalidDataException("Favorito inválido no arquivo.");
        return items;
    }

    public void Add(string url, string title)
    {
        if (!IsWebUrl(url)) throw new ArgumentException("Use uma URL HTTP ou HTTPS.");
        var items = Load().Where(b => b.Url != url).ToList();
        items.Add(new Bookmark(url, string.IsNullOrWhiteSpace(title) ? url : title));
        Save(items);
    }

    public void Remove(string url) => Save(Load().Where(b => b.Url != url).ToList());

    public static bool IsWebUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == "https" || uri.Scheme == "http") && string.IsNullOrEmpty(uri.UserInfo);

    private void Save(List<Bookmark> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
