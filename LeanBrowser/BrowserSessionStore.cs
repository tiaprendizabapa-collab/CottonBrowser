using System.Text.Json;

namespace LeanBrowser;

public sealed record SessionTabState(string Url, string Title, bool IsPinned = false,
    string? GroupName = null, int GroupColorArgb = 0, bool IsMuted = false)
{
    public static bool IsRestorableUrl(string? url) => BookmarkStore.IsWebUrl(url)
        && (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, TrustedBrowserBridge.HostName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(url, TrustedBrowserBridge.NewTabUrl, StringComparison.OrdinalIgnoreCase));

    public static SessionTabState? Capture(BrowserTab tab)
    {
        if (tab.IsPrivate || tab.IsDisposed) return null;
        var url = tab.LastKnownUrl;
        var muted = false;
        try
        {
            var core = tab.Web.CoreWebView2;
            if (core is not null && IsRestorableUrl(core.Source)) url = core.Source;
            muted = core?.IsMuted == true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        return IsRestorableUrl(url)
            ? new(url, tab.Text, tab.IsPinned, tab.GroupName, tab.GroupColorArgb, muted)
            : null;
    }
}

public sealed record BrowserWindowSession(SessionTabState[] Tabs, int SelectedIndex);
public sealed record BrowserSessionDocument(BrowserWindowSession[] Windows, bool CleanShutdown);

/// <summary>Only normal tabs are captured. Atomic snapshots survive an interrupted write.</summary>
public sealed class BrowserSessionStore(string path)
{
    public string? Error { get; private set; }
    private bool _readFailed;

    public BrowserSessionDocument Load()
    {
        try
        {
            if (!File.Exists(path)) return new([], true);
            var document = JsonSerializer.Deserialize<BrowserSessionDocument>(File.ReadAllText(path))
                ?? throw new JsonException("Sessão vazia.");
            if (document.Windows is null || document.Windows.Any(window => window is null || window.Tabs is null))
                throw new JsonException("Sessão inválida.");
            var windows = document.Windows.Take(20).Select(window =>
            {
                var tabs = window.Tabs.Where(tab => tab is not null && SessionTabState.IsRestorableUrl(tab.Url))
                    .Take(200).Select(tab => tab with
                    {
                        Title = (tab.Title ?? "Nova aba")[..Math.Min(tab.Title?.Length ?? 8, 512)],
                        GroupName = string.IsNullOrWhiteSpace(tab.GroupName) ? null : tab.GroupName[..Math.Min(tab.GroupName.Length, 40)]
                    }).ToArray();
                return new BrowserWindowSession(tabs, Math.Clamp(window.SelectedIndex, 0, Math.Max(0, tabs.Length - 1)));
            }).Where(window => window.Tabs.Length > 0).ToArray();
            return new(windows, document.CleanShutdown);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _readFailed = true;
            Error = "Não foi possível ler a sessão anterior. O arquivo foi preservado. " + ex.Message;
            return new([], true);
        }
    }

    public bool Save(BrowserSessionDocument document)
    {
        // Never silently replace a damaged user's session.
        if (_readFailed) return false;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(document));
            File.Move(temporary, path, true);
            Error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = "Não foi possível salvar a sessão. " + ex.Message;
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
