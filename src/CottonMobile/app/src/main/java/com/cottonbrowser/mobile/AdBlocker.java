package com.cottonbrowser.mobile;

import android.content.Context;
import android.net.Uri;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;

import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.SequenceInputStream;
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

    private final RequestBlocklist rules;
    private final AtomicBoolean enabled = new AtomicBoolean(true);
    private final AtomicLong blockedCount = new AtomicLong();

    private AdBlocker(RequestBlocklist rules) {
        this.rules = rules;
    }

    /** An unreadable APK asset must not prevent the browser from opening. */
    public static AdBlocker load(Context context) {
        var assets = context.getApplicationContext().getAssets();
        try (InputStreamReader reader = new InputStreamReader(new SequenceInputStream(
                assets.open("blocklist.txt"), assets.open("easylist_domains.txt")), StandardCharsets.UTF_8);
             InputStreamReader allows = new InputStreamReader(assets.open("easylist_allow.tsv"), StandardCharsets.UTF_8)) {
            return new AdBlocker(RequestBlocklist.parse(reader, allows));
        } catch (IOException | RuntimeException ignored) {
            return new AdBlocker(RequestBlocklist.empty());
        }
    }

    /** Return null to let WebView load the resource, or an empty 403 response to block it. */
    public WebResourceResponse intercept(WebResourceRequest request) {
        return intercept(request, originFromHeaders(request), false);
    }

    /** Main documents are checked only during a page-created popup's first navigation. */
    public WebResourceResponse intercept(WebResourceRequest request, String documentUrl, boolean popup) {
        if (!enabled.get() || request == null || (request.isForMainFrame() && !popup)
                || !matches(request.getUrl(), documentUrl)) return null;

        blockedCount.incrementAndGet();
        return new WebResourceResponse("text/plain", "UTF-8", 403,
                "Blocked by CottonBrowser", BLOCKED_HEADERS, new ByteArrayInputStream(EMPTY_BODY));
    }

    public boolean isEnabled() { return enabled.get(); }

    public void setEnabled(boolean value) { enabled.set(value); }

    public long getBlockedCount() { return blockedCount.get(); }

    public int getDomainCount() { return rules.size(); }

    /** Exact-host or dot-boundary subdomain match; never a substring match. */
    public boolean matches(Uri uri) {
        return matches(uri, null);
    }

    public boolean matches(Uri uri, String documentUrl) {
        if (uri == null) return false;
        String scheme = uri.getScheme();
        if (!"http".equalsIgnoreCase(scheme) && !"https".equalsIgnoreCase(scheme)) return false;

        String originHost = documentUrl == null ? null : Uri.parse(documentUrl).getHost();
        return rules.matches(uri.toString(), uri.getHost(), originHost);
    }

    private static String originFromHeaders(WebResourceRequest request) {
        if (request == null || request.getRequestHeaders() == null) return null;
        for (Map.Entry<String, String> header : request.getRequestHeaders().entrySet()) {
            if ("Referer".equalsIgnoreCase(header.getKey()) || "Origin".equalsIgnoreCase(header.getKey()))
                return header.getValue();
        }
        return null;
    }
}
