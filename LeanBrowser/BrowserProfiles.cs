using System.Text.Json;
using System.Diagnostics;

namespace LeanBrowser;

public sealed record BrowserProfile(string Id, string Name);

public static class BrowserPaths
{
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeanBrowser");
    public static string ProfileId { get; private set; } = "default";
    public static string DataDirectory => ForProfile(Root, ProfileId);
    public static string ThemeDirectory => ProfileId == "default"
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LeanBrowser") : DataDirectory;
    public static string ForProfile(string root, string id)
    {
        if (id != "default" && !Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Perfil inválido.");
        return id == "default" ? Path.GetFullPath(root) : Path.Combine(Path.GetFullPath(root), "Profiles", id);
    }
    public static void Initialize(string[] args)
    {
        var index = Array.IndexOf(args, "--profile");
        if (index >= 0)
        {
            if (index + 1 >= args.Length) throw new ArgumentException("Informe o perfil.");
            var id = args[index + 1];
            _ = ForProfile(Root, id);
            if (!new BrowserProfileStore(Root).Load().Any(p => p.Id == id)) throw new ArgumentException("Perfil não encontrado.");
            ProfileId = id;
        }
    }
}

public sealed class BrowserProfileStore(string root)
{
    private string FilePath => Path.Combine(root, "browser-profiles.json");
    public IReadOnlyList<BrowserProfile> Load()
    {
        var entries = File.Exists(FilePath) ? JsonSerializer.Deserialize<List<BrowserProfile>>(File.ReadAllText(FilePath))
            ?? throw new InvalidDataException("Lista de perfis inválida.") : [];
        foreach (var profile in entries)
        {
            if (profile is null || string.IsNullOrWhiteSpace(profile.Name)) throw new InvalidDataException("Perfil inválido.");
            _ = BrowserPaths.ForProfile(root, profile.Id);
        }
        if (!entries.Any(p => p.Id == "default")) entries.Insert(0, new("default", "Pessoal"));
        return entries.DistinctBy(p => p.Id).ToArray();
    }
    public BrowserProfile Create(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 60 || name.Any(char.IsControl)) throw new ArgumentException("Use um nome de 1 a 60 caracteres.");
        return Change(() =>
        {
            var profile = new BrowserProfile(Guid.NewGuid().ToString("N"), name);
            Save(Load().Append(profile));
            return profile;
        });
    }
    public void Rename(string id, string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 60 || name.Any(char.IsControl)) throw new ArgumentException("Nome inválido.");
        Change(() => { Save(Load().Select(p => p.Id == id ? p with { Name = name } : p)); return true; });
    }
    private T Change<T>(Func<T> operation)
    {
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant())));
        using var mutex = new Mutex(false, "Local\\CottonBrowser.ProfileRegistry." + key);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Outro perfil está atualizando a lista. Tente novamente.");
            return operation();
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }
    private void Save(IEnumerable<BrowserProfile> profiles)
    {
        Directory.CreateDirectory(root);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(profiles)); File.Move(temporary, FilePath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void Open(BrowserProfile profile)
    {
        _ = BrowserPaths.ForProfile(BrowserPaths.Root, profile.Id);
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Executável não encontrado.")) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
        start.ArgumentList.Add("--profile"); start.ArgumentList.Add(profile.Id);
        Process.Start(start);
    }
}
