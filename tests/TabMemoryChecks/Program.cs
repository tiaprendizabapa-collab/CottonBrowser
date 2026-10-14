using LeanBrowser;

var now = DateTimeOffset.UtcNow;
var idle = now.AddMinutes(-10);
Check(Eligible(), "A hidden page idle for the configured duration is eligible.");
Check(!Eligible(enabled: false), "Disabling memory saver keeps the page awake.");
Check(!Eligible(active: true), "The selected tab cannot be suspended.");
Check(!Eligible(visible: true), "A visible WebView cannot be suspended.");
Check(!Eligible(loading: true), "A loading document stays awake.");
Check(!Eligible(audio: true), "Audio, including muted output, stays awake.");
Check(!Eligible(downloads: true), "Downloads protect background pages.");
Check(!Eligible(capture: true), "Camera, microphone and screen capture keep the document awake.");
Check(!Eligible(suspended: true), "An already suspended page is not probed again.");
Check(!Eligible(lastActivated: now.AddMinutes(-9)), "The full inactivity interval must elapse.");
Check(!Eligible(lastActivated: now.AddMinutes(1)), "Clock adjustments must not suspend recently active pages.");
Check(!Eligible(lastActivated: now.AddSeconds(-59), minutes: 0), "An invalid duration retains a minimum idle interval.");
Check(TabMemorySafety.IsSiteException("https://example.com/path", ["example.com"]), "An exact domain exception matches.");
Check(TabMemorySafety.IsSiteException("https://meet.example.com/path", ["example.com"]), "Domain exceptions cover subdomains.");
Check(!TabMemorySafety.IsSiteException("https://notexample.com/", ["example.com"]), "Unrelated suffix domains do not match.");
Check(!TabMemorySafety.IsSiteException("https://example.com.evil.test/", ["example.com"]), "An attacker-controlled suffix does not match.");
Check(TabMemorySafety.IsSiteException("https://example.com/call", ["https://example.com/"]), "Canonical origin exceptions match page paths.");
Check(!TabMemorySafety.IsSiteException("http://example.com/", ["https://example.com/"]), "Origin exceptions keep the protocol boundary.");
Check(!TabMemorySafety.IsSiteException("https://example.com:8443/", ["https://example.com/"]), "Origin exceptions keep the port boundary.");
Check(TabMemorySafety.IsSiteException("https://EXAMPLE.com/", [" example.com "]), "Domain matching ignores case and surrounding space.");
Check(TabMemorySafety.IsSiteException("https://meet.example.com./", ["example.com"]), "Canonical DNS trailing dots preserve the site exception.");
Console.WriteLine("PASS: memory saver idle timing, selection/loading/media/download safeguards and site exception boundaries.");

bool Eligible(bool enabled = true, bool active = false, bool visible = false,
    bool loading = false, bool audio = false, bool downloads = false, bool capture = false,
    bool suspended = false, DateTimeOffset? lastActivated = null, int minutes = 10) =>
    TabMemorySafety.ShouldConsider(enabled, active, visible, loading, audio, downloads, capture,
        suspended, lastActivated ?? idle, now, minutes);

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}
