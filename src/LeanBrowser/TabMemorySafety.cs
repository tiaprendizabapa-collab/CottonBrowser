namespace LeanBrowser;

/// <summary>Conservative eligibility rules shared by the timer and its final race check.</summary>
public static class TabMemorySafety
{
    public static bool ShouldConsider(bool enabled, bool active, bool visible, bool loading,
        bool playingAudio, bool hasDownloads, bool capturedMedia, bool suspended,
        DateTimeOffset lastActivated, DateTimeOffset now, int idleMinutes) =>
        enabled && !active && !visible && !loading && !playingAudio && !hasDownloads &&
        !capturedMedia && !suspended && now - lastActivated >= TimeSpan.FromMinutes(Math.Clamp(idleMinutes, 1, 240));

    public static bool IsSiteException(string? url, IEnumerable<string>? exceptions)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target) || exceptions is null) return false;
        var targetHost = target.IdnHost.TrimEnd('.');
        foreach (var value in exceptions)
        {
            var rule = value?.Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(rule)) continue;
            if (rule.Contains("://", StringComparison.Ordinal))
            {
                if (Uri.TryCreate(rule, UriKind.Absolute, out var origin) &&
                    string.Equals(target.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(targetHost, origin.IdnHost.TrimEnd('.'), StringComparison.OrdinalIgnoreCase) &&
                    target.Port == origin.Port) return true;
            }
            else
            {
                // A domain includes its own subdomains, never a suffix such as notexample.com.
                rule = rule.Trim('.');
                if (string.Equals(targetHost, rule, StringComparison.OrdinalIgnoreCase) ||
                    targetHost.EndsWith("." + rule, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }
}
