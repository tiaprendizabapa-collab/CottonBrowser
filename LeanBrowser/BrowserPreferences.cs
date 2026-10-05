using System.Text.Json;

namespace LeanBrowser;

/// <summary>Preferências locais compartilhadas pelas janelas do navegador.</summary>
public sealed class BrowserPreferences
{
    private static readonly Lazy<BrowserPreferences> Shared = new(() => Load(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeanBrowser", "preferences.json")));
    private readonly string _path;
    private readonly object _saveGate = new();

    public static BrowserPreferences Current => Shared.Value;
    public static event Action? Changed;
    public bool RestoreSession { get; set; } = true;
    public bool MemorySaverEnabled { get; set; } = true;
    public int SuspendAfterMinutes { get; set; } = 10;
    public string[] MemorySaverExceptions { get; set; } = [];
    public string SearchEngine { get; set; } = "Google";

    private BrowserPreferences(string path) => _path = Path.GetFullPath(path);

    public static BrowserPreferences Load(string path)
    {
        var preferences = new BrowserPreferences(path);
        try
        {
            if (!File.Exists(preferences._path)) return preferences;
            var data = JsonSerializer.Deserialize<PreferenceData>(File.ReadAllText(preferences._path));
            if (data is null) return preferences;
            preferences.RestoreSession = data.RestoreSession;
            preferences.MemorySaverEnabled = data.MemorySaverEnabled;
            preferences.SuspendAfterMinutes = data.SuspendAfterMinutes;
            preferences.MemorySaverExceptions = data.MemorySaverExceptions ?? [];
            preferences.SearchEngine = data.SearchEngine ?? "Google";
            preferences.Normalize();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return preferences;
    }

    public bool Save()
    {
        var saved = false;
        lock (_saveGate)
        {
            Normalize();
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var data = new PreferenceData
                {
                    RestoreSession = RestoreSession, MemorySaverEnabled = MemorySaverEnabled,
                    SuspendAfterMinutes = SuspendAfterMinutes, MemorySaverExceptions = MemorySaverExceptions,
                    SearchEngine = SearchEngine
                };
                File.WriteAllText(temporary, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, _path, overwrite: true);
                saved = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        Changed?.Invoke();
        return saved;
    }

    private void Normalize()
    {
        SuspendAfterMinutes = Math.Clamp(SuspendAfterMinutes, 1, 240);
        SearchEngine = SearchEngine?.ToLowerInvariant() switch
        {
            "bing" => "Bing", "duckduckgo" => "DuckDuckGo", _ => "Google"
        };
        MemorySaverExceptions = (MemorySaverExceptions ?? [])
            .Select(value => TryNormalizeExceptionHost(value, out var host) ? host : null)
            .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(200).ToArray();
    }

    public static bool TryNormalizeExceptionHost(string? input, out string host)
    {
        host = string.Empty;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text) || text.Any(char.IsWhiteSpace)) return false;
        if (!Uri.TryCreate(text.Contains("://", StringComparison.Ordinal) ? text : "https://" + text,
                UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || uri.Host.Length == 0 || uri.UserInfo.Length != 0 || Uri.CheckHostName(uri.Host) == UriHostNameType.Unknown)
            return false;
        host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        return host.Length != 0;
    }

    public bool IsMemorySaverException(string? url)
    {
        if (!TryNormalizeExceptionHost(url, out var host)) return false;
        return MemorySaverExceptions.Any(exception => host.Equals(exception, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + exception, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class PreferenceData
    {
        public bool RestoreSession { get; set; } = true;
        public bool MemorySaverEnabled { get; set; } = true;
        public int SuspendAfterMinutes { get; set; } = 10;
        public string[]? MemorySaverExceptions { get; set; } = [];
        public string? SearchEngine { get; set; } = "Google";
    }
}
