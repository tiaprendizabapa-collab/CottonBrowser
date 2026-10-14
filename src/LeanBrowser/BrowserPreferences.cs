using System.Text.Json;

namespace LeanBrowser;

/// <summary>Preferências locais compartilhadas pelas janelas do navegador.</summary>
public sealed partial class BrowserPreferences
{
    private static readonly Lazy<BrowserPreferences> Shared = new(() => Load(Path.Combine(BrowserPaths.DataDirectory, "preferences.json")));
    private readonly string _path;
    private readonly object _saveGate = new();

    public static BrowserPreferences Current => Shared.Value;
    public static event Action? Changed;
    public bool RestoreSession { get; set; } = true;
    public bool MemorySaverEnabled { get; set; } = true;
    public int SuspendAfterMinutes { get; set; } = 10;
    public string[] MemorySaverExceptions { get; set; } = [];
    public string SearchEngine { get; set; } = "Google";
    public bool VerticalTabs { get; set; }
    public bool VerticalTabsCollapsed { get; set; }
    public Dictionary<string, double> SiteZoom { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string DownloadFolder { get; set; } = "";
    public bool AskDownloadLocation { get; set; }
    public Dictionary<string, int> Shortcuts { get; set; } = new();
    public string NewTabBackground { get; set; } = "Padrão";
    public bool NewTabShowClock { get; set; } = true;
    public bool NewTabShowGreeting { get; set; } = true;
    public bool NewTabShowShortcuts { get; set; } = true;
    public Bookmark[] NewTabShortcuts { get; set; } = [new("https://www.google.com/", "Google"), new("https://mail.google.com/", "Gmail"), new("https://drive.google.com/", "Drive"), new("https://github.com/", "GitHub")];

    private BrowserPreferences(string path) => _path = Path.GetFullPath(path);

    internal static void ValidateJson(string text)
    {
        var data = JsonSerializer.Deserialize<PreferenceData>(text) ?? throw new InvalidDataException("Preferências inválidas.");
        if (data.DownloadFolder?.Length > 32767 || data.SiteZoom?.Count > 2000 || data.Shortcuts?.Count > 100
            || data.NewTabShortcuts?.Any(b => b is null || !BookmarkStore.IsWebUrl(b.Url) || b.Title is null) == true)
            throw new InvalidDataException("Preferências inválidas.");
    }

    public static BrowserPreferences Load(string path)
    {
        var preferences = new BrowserPreferences(path);
        try
        {
            if (!File.Exists(preferences._path))
            {
                preferences.ImportLegacySiteZoom();
                return preferences;
            }
            var data = JsonSerializer.Deserialize<PreferenceData>(File.ReadAllText(preferences._path));
            if (data is null) return preferences;
            preferences.RestoreSession = data.RestoreSession;
            preferences.MemorySaverEnabled = data.MemorySaverEnabled;
            preferences.SuspendAfterMinutes = data.SuspendAfterMinutes;
            preferences.MemorySaverExceptions = data.MemorySaverExceptions ?? [];
            preferences.SearchEngine = data.SearchEngine ?? "Google";
            preferences.VerticalTabs = data.VerticalTabs;
            preferences.VerticalTabsCollapsed = data.VerticalTabsCollapsed;
            preferences.SiteZoom = data.SiteZoom ?? new();
            preferences.DownloadFolder = data.DownloadFolder ?? "";
            preferences.AskDownloadLocation = data.AskDownloadLocation;
            preferences.Shortcuts = data.Shortcuts ?? new();
            preferences.NewTabBackground = data.NewTabBackground ?? "Padrão";
            preferences.NewTabShowClock = data.NewTabShowClock;
            preferences.NewTabShowGreeting = data.NewTabShowGreeting;
            preferences.NewTabShowShortcuts = data.NewTabShowShortcuts;
            if (data.NewTabShortcuts is not null) preferences.NewTabShortcuts = data.NewTabShortcuts;
            preferences.Normalize();
            preferences.ImportLegacySiteZoom();
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
                    SearchEngine = SearchEngine, VerticalTabs = VerticalTabs, VerticalTabsCollapsed = VerticalTabsCollapsed,
                    NewTabBackground = NewTabBackground, NewTabShowClock = NewTabShowClock, NewTabShowGreeting = NewTabShowGreeting,
                    NewTabShowShortcuts = NewTabShowShortcuts, NewTabShortcuts = NewTabShortcuts
                    , SiteZoom = SiteZoom, DownloadFolder = DownloadFolder, AskDownloadLocation = AskDownloadLocation, Shortcuts = Shortcuts
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
        var normalizedZoom = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, factor) in SiteZoom ?? new())
            if (TryGetZoomHost("https://" + key, out var host) && IsValidZoom(factor) && normalizedZoom.Count < 2000)
                normalizedZoom[host] = factor;
        SiteZoom = normalizedZoom;
        if (DownloadFolder.Length > 0 && !Path.IsPathFullyQualified(DownloadFolder)) DownloadFolder = "";
        NormalizeShortcuts();
        if (NewTabBackground is not ("Padrão" or "Azul" or "Verde" or "Pôr do sol")) NewTabBackground = "Padrão";
        NewTabShortcuts = (NewTabShortcuts ?? []).Where(b => b is not null && BookmarkStore.IsWebUrl(b.Url) && !string.IsNullOrWhiteSpace(b.Title))
            .DistinctBy(b => b.Url).Take(12).Select(b => b with { Title = b.Title[..Math.Min(80, b.Title.Length)] }).ToArray();
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

    partial void NormalizeShortcuts();

    public double GetSiteZoom(string? url) => TryGetZoomHost(url, out var host)
        && SiteZoom.TryGetValue(host, out var zoom) ? zoom : 1;

    public double ZoomFor(string? url) => GetSiteZoom(url);

    public void RememberZoom(string? url, double zoom) => SetSiteZoom(url, zoom);

    public bool SetSiteZoom(string? url, double zoom)
    {
        if (!TryGetZoomHost(url, out var host) || !IsValidZoom(zoom)) return false;
        if (Math.Abs(zoom - 1) < .001) SiteZoom.Remove(host); else SiteZoom[host] = zoom;
        return Save();
    }

    internal static bool TryGetZoomHost(string? url, out string host)
    {
        host = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || uri.Host.Length == 0 || uri.UserInfo.Length != 0) return false;
        host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        return host.Length != 0 && host != "app.cottonbrowser.test";
    }

    private static bool IsValidZoom(double factor) => double.IsFinite(factor) && factor is >= .25 and <= 5;

    private void ImportLegacySiteZoom()
    {
        var legacyPath = Path.Combine(Path.GetDirectoryName(_path)!, "site-zoom.json");
        if (!File.Exists(legacyPath)) return;
        try
        {
            var oldValues = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(legacyPath));
            if (oldValues is null) return;
            foreach (var (host, factor) in oldValues)
                if (TryGetZoomHost("https://" + host, out var normalized)
                    && IsValidZoom(factor) && !SiteZoom.ContainsKey(normalized))
                    SiteZoom[normalized] = factor;
            if (Save()) File.Delete(legacyPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    public void Reload()
    {
        var loaded = Load(_path);
        foreach (var property in typeof(BrowserPreferences).GetProperties().Where(p => p.CanWrite))
            property.SetValue(this, property.GetValue(loaded));
        Changed?.Invoke();
    }

    private sealed class PreferenceData
    {
        public bool RestoreSession { get; set; } = true;
        public bool MemorySaverEnabled { get; set; } = true;
        public int SuspendAfterMinutes { get; set; } = 10;
        public string[]? MemorySaverExceptions { get; set; } = [];
        public string? SearchEngine { get; set; } = "Google";
        public bool VerticalTabs { get; set; }
        public bool VerticalTabsCollapsed { get; set; }
        public Dictionary<string, double>? SiteZoom { get; set; }
        public string? DownloadFolder { get; set; }
        public bool AskDownloadLocation { get; set; }
        public Dictionary<string, int>? Shortcuts { get; set; }
        public string? NewTabBackground { get; set; } = "Padrão";
        public bool NewTabShowClock { get; set; } = true;
        public bool NewTabShowGreeting { get; set; } = true;
        public bool NewTabShowShortcuts { get; set; } = true;
        public Bookmark[]? NewTabShortcuts { get; set; }
    }
}
