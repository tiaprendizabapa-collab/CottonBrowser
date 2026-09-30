package com.cottonbrowser.mobile;

import android.content.Context;
import android.net.Uri;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;

import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.Collections;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicLong;

/**
 * Local domain blocker for Android WebView subresources. No network lookup or JavaScript bridge is
 * needed: the bundled rules are parsed once and each request checks at most its hostname labels.
 */
public final class AdBlocker {
    private static final byte[] EMPTY_BODY = new byte[0];
    private static final Map<String, String> BLOCKED_HEADERS;

    static {
        Map<String, String> headers = new HashMap<>();
        headers.put("Access-Control-Allow-Origin", "*");
        headers.put("Cache-Control", "no-store");
        BLOCKED_HEADERS = Collections.unmodifiableMap(headers);
    }

    private final DomainBlocklist domains;
    private final AtomicBoolean enabled = new AtomicBoolean(true);
    private final AtomicLong blockedCount = new AtomicLong();

    private AdBlocker(DomainBlocklist domains) {
        this.domains = domains;
    }

    /** An unreadable APK asset must not prevent the browser from opening. */
    public static AdBlocker load(Context context) {
        try (InputStreamReader reader = new InputStreamReader(
                context.getApplicationContext().getAssets().open("blocklist.txt"),
                StandardCharsets.UTF_8)) {
            return new AdBlocker(DomainBlocklist.parse(reader));
        } catch (IOException ignored) {
            return new AdBlocker(DomainBlocklist.empty());
        }
    }

    /** Return null to let WebView load the resource, or an empty 403 response to block it. */
    public WebResourceResponse intercept(WebResourceRequest request) {
        // Never replace the top-level document with a blank response, even if its host is listed.
        if (!enabled.get() || request == null || request.isForMainFrame()
                || !matches(request.getUrl())) return null;

        blockedCount.incrementAndGet();
        return new WebResourceResponse("text/plain", "UTF-8", 403,
                "Blocked by CottonBrowser", BLOCKED_HEADERS, new ByteArrayInputStream(EMPTY_BODY));
    }

    public boolean isEnabled() { return enabled.get(); }

    public void setEnabled(boolean value) { enabled.set(value); }

    public long getBlockedCount() { return blockedCount.get(); }

    public int getDomainCount() { return domains.size(); }

    public boolean matchesHost(String host) { return domains.matchesHost(host); }

    /** Exact-host or dot-boundary subdomain match; never a substring match. */
    public boolean matches(Uri uri) {
        if (uri == null) return false;
        String scheme = uri.getScheme();
        if (!"http".equalsIgnoreCase(scheme) && !"https".equalsIgnoreCase(scheme)) return false;

        return domains.matchesHost(uri.getHost());
    }
}
