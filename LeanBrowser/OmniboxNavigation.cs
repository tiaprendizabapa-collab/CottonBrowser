namespace LeanBrowser;

internal static class OmniboxNavigation
{
    public static string? ResolveTarget(string input, string? currentUrl, bool wasEdited,
        string? suggestionText = null, string? suggestionUrl = null, string searchEngine = "Google")
    {
        if (!string.IsNullOrWhiteSpace(suggestionUrl)) return suggestionUrl;
        var text = (suggestionText ?? input).Trim();
        if (text.Length == 0) return null;

        // O endereço exibido omite protocolo e www; Enter sem edição deve
        // preservar a URL real, inclusive HTTP e barras finais.
        if (suggestionText is null && !wasEdited && !string.IsNullOrEmpty(currentUrl)
            && string.Equals(text, UrlHelper.ForDisplay(currentUrl), StringComparison.Ordinal))
            return currentUrl;
        return UrlHelper.Normalize(text, searchEngine);
    }
}

internal sealed class TabNavigationState
{
    private string? _pendingUrl;
    public bool IsReady { get; private set; }
    public bool HasUserNavigation { get; private set; }

    public string? Request(string url)
    {
        HasUserNavigation = true;
        if (IsReady) return url;
        _pendingUrl = url;
        return null;
    }

    public string CompleteInitialization(string initialUrl)
    {
        IsReady = true;
        var target = _pendingUrl ?? initialUrl;
        _pendingUrl = null;
        return target;
    }
}
