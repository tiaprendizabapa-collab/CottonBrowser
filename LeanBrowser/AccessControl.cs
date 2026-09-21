using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeanBrowser;

public enum BrowserRole
{
    User,
    Admin
}

/// <summary>
/// Resolve o perfil do usuário do Windows e aplica as permissões da interface.
/// O primeiro usuário que inicializa o navegador é registrado como Admin; outros
/// usuários do mesmo computador entram como User até serem atribuídos pelo
/// administrador no registro local de perfis.
/// </summary>
public sealed class AccessControl
{
    private sealed class ProfileRegistry
    {
        public List<ProfileEntry> Profiles { get; set; } = new();
    }

    private sealed class ProfileEntry
    {
        public string User { get; set; } = "";
        public BrowserRole Role { get; set; }
    }

    private static readonly string RegistryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LeanBrowser", "profiles.json");

    public string UserName { get; }
    public BrowserRole Role { get; }
    public bool IsAdmin => Role == BrowserRole.Admin;
    public string RoleLabel => IsAdmin ? "Admin" : "Usuário";

    public AccessControl()
    {
        UserName = WindowsIdentity.GetCurrent()?.Name ?? Environment.UserName;
        Role = ResolveRole(UserName);
    }

    private static BrowserRole ResolveRole(string userName)
    {
        try
        {
            if (!File.Exists(RegistryPath))
            {
                var firstRegistry = new ProfileRegistry();
                firstRegistry.Profiles.Add(new ProfileEntry { User = userName, Role = BrowserRole.Admin });
                Save(firstRegistry);
                return BrowserRole.Admin;
            }

            var options = new JsonSerializerOptions();
            options.Converters.Add(new JsonStringEnumConverter());
            var registry = JsonSerializer.Deserialize<ProfileRegistry>(File.ReadAllText(RegistryPath), options);
            var entry = registry?.Profiles.FirstOrDefault(profile =>
                string.Equals(profile.User, userName, StringComparison.OrdinalIgnoreCase));
            return entry?.Role ?? BrowserRole.User;
        }
        catch
        {
            // Falha de leitura deve ocultar os recursos avançados.
            return BrowserRole.User;
        }
    }

    private static void Save(ProfileRegistry registry)
    {
        var directory = Path.GetDirectoryName(RegistryPath)!;
        Directory.CreateDirectory(directory);
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(RegistryPath, JsonSerializer.Serialize(registry, options));
    }
}
