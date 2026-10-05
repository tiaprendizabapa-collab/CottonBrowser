using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;
using System.Text;

namespace LeanBrowser;

public sealed record Bookmark(string Url, string Title, string Folder = "");

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
        return items.Select(b => b with { Folder = NormalizeFolder(b.Folder) }).ToArray();
    }

    public void Add(string url, string title)
    {
        if (!IsWebUrl(url)) throw new ArgumentException("Use uma URL HTTP ou HTTPS.");
        var current = Load();
        var existing = current.FirstOrDefault(b => b.Url == url);
        var items = current.Where(b => b.Url != url).ToList();
        items.Add(new Bookmark(url, string.IsNullOrWhiteSpace(title) ? url : title, existing?.Folder ?? ""));
        Save(items);
    }

    public void Remove(string url) => Save(Load().Where(b => b.Url != url).ToList());

    public IReadOnlyList<string> Folders() => LoadFolders().Concat(Load().Select(b => b.Folder)).SelectMany(f =>
        { var parts = f.Split('/'); return Enumerable.Range(1, parts.Length).Select(i => string.Join('/', parts.Take(i))); }).Where(f => f.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    private string FolderFile => path + ".folders.json";
    private IEnumerable<string> LoadFolders() => File.Exists(FolderFile)
        ? (JsonSerializer.Deserialize<string[]>(File.ReadAllText(FolderFile)) ?? []).Select(NormalizeFolder) : [];
    public static string NormalizeFolder(string? name)
    {
        var parts = (name ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 8 || parts.Any(p => p.Length > 80 || p.Any(char.IsControl))) throw new ArgumentException("Use até 8 níveis de pastas, com nomes de até 80 caracteres.");
        return string.Join('/', parts);
    }
    public void CreateFolder(string name)
    {
        name = NormalizeFolder(name);
        if (name.Length == 0) throw new ArgumentException("Informe o nome da pasta.");
        SaveFolders(Folders().Append(name));
    }
    private void SaveFolders(IEnumerable<string> folders)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = FolderFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(folders.Distinct(StringComparer.OrdinalIgnoreCase))); File.Move(temporary, FolderFile, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void RenameFolder(string oldName, string newName)
    {
        oldName = NormalizeFolder(oldName); newName = NormalizeFolder(newName);
        if (oldName.Length == 0 || newName.Length == 0) throw new ArgumentException("Nome de pasta inválido.");
        string Rename(string value) => value.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? newName
            : value.StartsWith(oldName + "/", StringComparison.OrdinalIgnoreCase) ? newName + value[oldName.Length..] : value;
        var folders = Folders().Select(f => NormalizeFolder(Rename(f))).ToArray();
        var bookmarks = Load().Select(b => b with { Folder = NormalizeFolder(Rename(b.Folder)) }).ToList();
        Save(bookmarks); SaveFolders(folders);
    }
    public void Update(string originalUrl, Bookmark bookmark)
    {
        if (!IsWebUrl(bookmark.Url) || string.IsNullOrWhiteSpace(bookmark.Title)) throw new ArgumentException("Informe um título e um endereço HTTP ou HTTPS.");
        bookmark = bookmark with { Folder = NormalizeFolder(bookmark.Folder), Title = bookmark.Title.Trim() };
        var items = Load().ToList();
        var index = items.FindIndex(b => b.Url == originalUrl);
        if (items.Where((_, i) => i != index).Any(b => b.Url == bookmark.Url))
            throw new ArgumentException("Este endereço já está nos favoritos. Edite o favorito existente.");
        if (index < 0) items.Add(bookmark); else items[index] = bookmark;
        Save(items);
    }
    public void Move(string url, int direction)
    {
        var items = Load().ToList(); var index = items.FindIndex(b => b.Url == url);
        if (index < 0) return;
        var indices = items.Select((b, i) => (b, i)).Where(x => x.b.Folder == items[index].Folder).Select(x => x.i).ToList();
        var destination = indices.IndexOf(index) + Math.Sign(direction);
        if (destination < 0 || destination >= indices.Count) return;
        var other = indices[destination]; (items[index], items[other]) = (items[other], items[index]); Save(items);
    }
    public void Sort() => Save(Load().OrderBy(b => b.Folder, StringComparer.OrdinalIgnoreCase).ThenBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase).ToList());
    public int ImportHtml(string html)
    {
        if (html.Length > 10 * 1024 * 1024) throw new ArgumentException("O arquivo deve ter até 10 MB.");
        var imported = new List<Bookmark>(); var folders = new List<string>(); var stack = new Stack<string>(); string? pending = null;
        foreach (Match token in Regex.Matches(html, @"<H3\b[^>]*>(?<folder>.*?)</H3\s*>|<A\b(?<attributes>[^>]*)>(?<title>.*?)</A\s*>|(?<open><DL\b[^>]*>)|(?<close></DL\s*>)", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(3)))
        {
            if (token.Groups["folder"].Success) pending = WebUtility.HtmlDecode(Regex.Replace(token.Groups["folder"].Value, "<[^>]*>", "")).Replace('/', '∕').Trim();
            else if (token.Groups["open"].Success)
            {
                var parent = stack.TryPeek(out var value) ? value : "";
                var folder = pending is null ? parent : NormalizeFolder(parent.Length == 0 ? pending : parent + "/" + pending);
                stack.Push(folder); if (folder.Length > 0) folders.Add(folder); pending = null;
            }
            else if (token.Groups["close"].Success) { if (stack.Count > 0) stack.Pop(); }
            else
            {
                var match = Regex.Match(token.Groups["attributes"].Value, "\\bhref\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                var url = WebUtility.HtmlDecode(match.Groups.Cast<Group>().Skip(1).FirstOrDefault(g => g.Success)?.Value ?? "");
                if (!IsWebUrl(url)) continue;
                var title = WebUtility.HtmlDecode(Regex.Replace(token.Groups["title"].Value, "<[^>]*>", "")).Trim();
                imported.Add(new(url, title.Length == 0 ? url : title, stack.TryPeek(out var folder) ? folder : ""));
                if (imported.Count > 20000) throw new ArgumentException("Importe até 20.000 favoritos por arquivo.");
            }
        }
        if (imported.Count == 0 && folders.Count == 0) throw new InvalidDataException("Nenhum favorito ou pasta encontrado. Exporte os favoritos do outro navegador no formato HTML.");
        var existing = Load().ToList(); var urls = existing.Select(b => b.Url).ToHashSet(StringComparer.Ordinal); var added = imported.Where(b => urls.Add(b.Url)).ToArray();
        SaveFolders(Folders().Concat(folders)); Save(existing.Concat(added).ToList()); return added.Length;
    }
    public string ExportHtml()
    {
        var items = Load(); var folders = Folders();
        var output = new StringBuilder("<!DOCTYPE NETSCAPE-Bookmark-file-1>\n<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">\n<TITLE>Favoritos CottonBrowser</TITLE>\n<H1>Favoritos</H1>\n<DL><p>\n");
        var all = folders.Concat(items.Select(b => b.Folder)).SelectMany(f => { var parts = f.Split('/'); return Enumerable.Range(1, parts.Length).Select(i => string.Join('/', parts.Take(i))); }).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        void Write(string parent)
        {
            foreach (var b in items.Where(b => b.Folder.Equals(parent, StringComparison.OrdinalIgnoreCase)))
                output.AppendLine($"<DT><A HREF=\"{WebUtility.HtmlEncode(b.Url)}\">{WebUtility.HtmlEncode(b.Title)}</A>");
            foreach (var folder in all.Where(f => (f.Contains('/') ? f[..f.LastIndexOf('/')] : "").Equals(parent, StringComparison.OrdinalIgnoreCase)))
            { output.AppendLine($"<DT><H3>{WebUtility.HtmlEncode(folder.Split('/').Last())}</H3>\n<DL><p>"); Write(folder); output.AppendLine("</DL><p>"); }
        }
        Write(""); output.AppendLine("</DL><p>"); return output.ToString();
    }

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
