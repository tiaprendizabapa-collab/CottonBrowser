using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeanBrowser;

/// <summary>
/// UI-only capability level. This is deliberately not an authorization role
/// and must never be used as a security boundary.
/// </summary>
public enum BrowserUiMode
{
    Standard,
    Advanced
}

/// <summary>
/// Resolves a local, UI-only mode for the current Windows user. The registry is
/// DPAPI-protected to detect casual tampering and to avoid exposing profile data
/// at rest. A local user can still control a process that runs as that user, so
/// sensitive authorization must be enforced by a server or privileged service.
/// </summary>
public sealed class AccessControl
{
    private const string Purpose = "CottonBrowser.AccessControl.v2";

    private sealed class ProfileRegistry
    {
        public List<ProfileEntry> Profiles { get; set; } = new();
    }

    private sealed class ProfileEntry
    {
        public string User { get; set; } = "";
        public BrowserUiMode Mode { get; set; }
    }

    // These types exist only to migrate the previous profiles.json schema.
    private sealed class LegacyProfileRegistry
    {
        public List<LegacyProfileEntry> Profiles { get; set; } = new();
    }

    private sealed class LegacyProfileEntry
    {
        public string User { get; set; } = "";
        public LegacyUiRole Role { get; set; }
    }

    private enum LegacyUiRole
    {
        User,
        Admin
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly byte[] PurposeEntropy =
        SHA256.HashData(Encoding.UTF8.GetBytes(Purpose));

    private static readonly string ProfileDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LeanBrowser");

    private static readonly string RegistryPath = Path.Combine(
        ProfileDirectory, "profiles.dpapi");

    private static readonly string LegacyRegistryPath = Path.Combine(
        ProfileDirectory, "profiles.json");

    public string UserName { get; }
    public BrowserUiMode Mode { get; }
    public bool IsAdvancedMode => Mode == BrowserUiMode.Advanced;
    public string ModeLabel => IsAdvancedMode ? "Modo avançado" : "Modo padrão";

    public AccessControl()
    {
        UserName = WindowsIdentity.GetCurrent()?.Name ?? Environment.UserName;
        Mode = ResolveUiMode(UserName);
    }

    private static BrowserUiMode ResolveUiMode(string userName)
    {
        try
        {
            var registry = LoadRegistry();
            var entry = registry.Profiles.FirstOrDefault(profile =>
                string.Equals(profile.User, userName, StringComparison.OrdinalIgnoreCase));

            if (entry is not null) return entry.Mode;

            // The first local profile keeps the previous product behavior, but
            // this only reveals UI features; it is not an elevated privilege.
            if (registry.Profiles.Count == 0)
            {
                registry.Profiles.Add(new ProfileEntry
                {
                    User = userName,
                    Mode = BrowserUiMode.Advanced
                });
                Save(registry);
                return BrowserUiMode.Advanced;
            }

            return BrowserUiMode.Standard;
        }
        catch (CryptographicException)
        {
            // A damaged or substituted protected file must fail closed.
            return BrowserUiMode.Standard;
        }
        catch (IOException)
        {
            return BrowserUiMode.Standard;
        }
        catch (UnauthorizedAccessException)
        {
            return BrowserUiMode.Standard;
        }
        catch (JsonException)
        {
            return BrowserUiMode.Standard;
        }
    }

    private static ProfileRegistry LoadRegistry()
    {
        if (File.Exists(RegistryPath)) return LoadProtectedRegistry();

        if (!File.Exists(LegacyRegistryPath)) return new ProfileRegistry();

        // One-time migration from the former plaintext file. A successful save
        // happens before the source is removed, so a write failure preserves it.
        var legacy = JsonSerializer.Deserialize<LegacyProfileRegistry>(
            File.ReadAllText(LegacyRegistryPath), JsonOptions)
            ?? throw new JsonException("Invalid legacy profile registry.");

        var registry = new ProfileRegistry
        {
            Profiles = legacy.Profiles
                .Where(profile => !string.IsNullOrWhiteSpace(profile.User))
                .Select(profile => new ProfileEntry
                {
                    User = profile.User,
                    Mode = profile.Role == LegacyUiRole.Admin
                        ? BrowserUiMode.Advanced
                        : BrowserUiMode.Standard
                })
                .ToList()
        };

        Save(registry);
        File.Delete(LegacyRegistryPath);
        return registry;
    }

    private static ProfileRegistry LoadProtectedRegistry()
    {
        byte[]? ciphertext = null;
        byte[]? plaintext = null;
        try
        {
            ciphertext = File.ReadAllBytes(RegistryPath);
            plaintext = ProtectedData.Unprotect(
                ciphertext,
                PurposeEntropy,
                DataProtectionScope.CurrentUser);

            return JsonSerializer.Deserialize<ProfileRegistry>(plaintext, JsonOptions)
                ?? throw new JsonException("Invalid protected profile registry.");
        }
        finally
        {
            if (ciphertext is not null) CryptographicOperations.ZeroMemory(ciphertext);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static void Save(ProfileRegistry registry)
    {
        byte[]? plaintext = null;
        byte[]? ciphertext = null;
        try
        {
            plaintext = JsonSerializer.SerializeToUtf8Bytes(registry, JsonOptions);
            ciphertext = ProtectedData.Protect(
                plaintext,
                PurposeEntropy,
                DataProtectionScope.CurrentUser);
            WriteCiphertextAtomically(ciphertext);
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            if (ciphertext is not null) CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    private static void WriteCiphertextAtomically(byte[] ciphertext)
    {
        Directory.CreateDirectory(ProfileDirectory);
        var temporary = Path.Combine(
            ProfileDirectory,
            ".profiles." + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(ciphertext, 0, ciphertext.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, RegistryPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
