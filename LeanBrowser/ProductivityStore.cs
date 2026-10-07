using System.Text.Json;

namespace LeanBrowser;

public sealed record WorkspaceEntry(Guid Id, string Name, SessionTabState[] Tabs, int SelectedIndex);
public sealed record ReadingEntry(Guid Id, string Url, string Title, string Notes, bool Read, DateTimeOffset SavedAt);
public sealed record ProductivityDocument(WorkspaceEntry[] Workspaces, ReadingEntry[] ReadingList);

public sealed class ProductivityStore(string path)
{
    public ProductivityDocument Load()
    {
        if (!File.Exists(path)) return new([], []);
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("Arquivo de organização muito grande.");
        var data = JsonSerializer.Deserialize<ProductivityDocument>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Arquivo de organização inválido.");
        Validate(data);
        return data;
    }

    public static void Validate(ProductivityDocument data)
    {
        if (data.Workspaces is null || data.ReadingList is null || data.Workspaces.Length > 100 || data.ReadingList.Length > 5000)
            throw new InvalidDataException("Limite de espaços ou leituras excedido.");
        if (data.Workspaces.Any(w => w is null || w.Id == Guid.Empty || string.IsNullOrWhiteSpace(w.Name) || w.Name.Length > 60
            || w.Tabs is null || w.Tabs.Length is < 1 or > 200 || w.SelectedIndex < 0 || w.SelectedIndex >= w.Tabs.Length
            || w.Tabs.Any(t => t is null || !SessionTabState.IsRestorableUrl(t.Url) || t.Title is null || t.Title.Length > 512
                || t.GroupName?.Length > 40)) || data.Workspaces.Select(w => w.Id).Distinct().Count() != data.Workspaces.Length)
            throw new InvalidDataException("Espaço de trabalho inválido.");
        if (data.ReadingList.Any(r => r is null || r.Id == Guid.Empty || !BookmarkStore.IsWebUrl(r.Url)
            || TrustedBrowserBridge.IsTrustedUiUri(r.Url) || r.Title is null || r.Title.Length > 512 || r.Notes is null || r.Notes.Length > 10000)
            || data.ReadingList.Select(r => r.Id).Distinct().Count() != data.ReadingList.Length)
            throw new InvalidDataException("Leitura inválida.");
    }

    public void SaveWorkspace(string name, SessionTabState[] tabs, int selectedIndex, Guid? id = null)
    {
        var data = Load();
        var entry = new WorkspaceEntry(id ?? Guid.NewGuid(), name.Trim(), tabs, selectedIndex);
        Save(data with { Workspaces = data.Workspaces.Where(w => w.Id != entry.Id).Append(entry).ToArray() });
    }
    public void DeleteWorkspace(Guid id) { var data = Load(); Save(data with { Workspaces = data.Workspaces.Where(w => w.Id != id).ToArray() }); }
    public void RenameWorkspace(Guid id, string name)
    {
        var data = Load(); Save(data with { Workspaces = data.Workspaces.Select(w => w.Id == id ? w with { Name = name.Trim() } : w).ToArray() });
    }
    public void SaveReading(string url, string title)
    {
        var data = Load();
        if (data.ReadingList.Any(r => r.Url == url)) return;
        Save(data with { ReadingList = data.ReadingList.Append(new ReadingEntry(Guid.NewGuid(), url,
            title[..Math.Min(title.Length, 512)], "", false, DateTimeOffset.UtcNow)).ToArray() });
    }
    public void UpdateReading(ReadingEntry entry)
    {
        var data = Load(); Save(data with { ReadingList = data.ReadingList.Select(r => r.Id == entry.Id ? entry : r).ToArray() });
    }
    public void DeleteReading(Guid id) { var data = Load(); Save(data with { ReadingList = data.ReadingList.Where(r => r.Id != id).ToArray() }); }

    private void Save(ProductivityDocument data)
    {
        Validate(data);
        var text = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        if (System.Text.Encoding.UTF8.GetByteCount(text) > 8 * 1024 * 1024)
            throw new InvalidDataException("Limite de 8 MB atingido. Remova leituras antigas ou reduza as anotações.");
        AtomicFile.Write(path, text);
    }
}

internal static class AtomicFile
{
    public static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, text); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
