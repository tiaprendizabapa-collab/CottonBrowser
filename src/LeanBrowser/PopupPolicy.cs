namespace LeanBrowser;

/// <summary>Não confia em IsUserInitiated: um anúncio pode aproveitar um clique legítimo.</summary>
public sealed class PopupPolicy
{
    private string? _allowedOrigin;
    private string? _currentOrigin;
    public int BlockedCount { get; private set; }
    public string? LastBlockedUrl { get; private set; }
    public bool AllowedForCurrentOrigin => _currentOrigin is not null && _allowedOrigin == _currentOrigin;

    public void OnNavigation(string url)
    {
        var origin = GetOrigin(url);
        if (origin == _currentOrigin) return;
        _currentOrigin = origin;
        _allowedOrigin = null;
        BlockedCount = 0;
        LastBlockedUrl = null;
    }

    public void AllowCurrentOrigin(bool allow) => _allowedOrigin = allow ? _currentOrigin : null;

    public bool ShouldOpen(string url, bool isUserInitiated)
    {
        if (AllowedForCurrentOrigin && isUserInitiated && BookmarkStore.IsWebUrl(url)) return true;
        BlockedCount++;
        LastBlockedUrl = BookmarkStore.IsWebUrl(url) ? url : null;
        return false;
    }

    private static string? GetOrigin(string url) => BookmarkStore.IsWebUrl(url)
        ? new Uri(url).GetLeftPart(UriPartial.Authority) : null;
}
