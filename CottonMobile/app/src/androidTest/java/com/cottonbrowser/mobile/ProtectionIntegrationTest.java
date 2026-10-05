package com.cottonbrowser.mobile;

import android.app.Instrumentation;
import android.content.Intent;
import android.net.Uri;
import android.webkit.WebResourceRequest;
import android.webkit.WebView;

import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.webkit.WebViewFeature;

import org.json.JSONObject;
import org.json.JSONTokener;
import org.junit.Test;
import org.junit.runner.RunWith;

import java.lang.reflect.Field;
import java.util.Collections;
import java.util.Map;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;

import static org.junit.Assert.*;

/** Exercises the actual Activity, WebView callbacks and document-start delivery in an emulator. */
@RunWith(AndroidJUnit4.class)
public final class ProtectionIntegrationTest {
    @Test
    public void blocksNetworkResourcesAndSanitizesMobilePlayerBeforePageScripts() throws Exception {
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        var context = instrumentation.getTargetContext();
        context.getSharedPreferences("cotton_mobile", 0).edit()
                .remove("protection_exceptions").putLong("last_update_check", System.currentTimeMillis()).commit();
        MainActivity activity = (MainActivity) instrumentation.startActivitySync(
                new Intent(context, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        try {
            Object tab = field(activity, "activeTab");
            WebView view = (WebView) field(tab, "webView");
            AtomicInteger blocked = (AtomicInteger) field(tab, "blockedCount");
            AtomicReference<Boolean> earlySupported = new AtomicReference<>();
            String html = "<html><head><script>"
                    + "window.ytInitialPlayerResponse={videoDetails:{videoId:'test'},"
                    + "streamingData:{formats:[{url:'https://rr1.googlevideo.com/videoplayback'}]},"
                    + "adPlacements:[{ad:true}]};"
                    + "window.earlyProtected=!('adPlacements' in ytInitialPlayerResponse);"
                    + "</script></head><body><ins class='adsbygoogle'>advertisement</ins>"
                    + "<div id='content'>normal content</div>"
                    + "<img src='https://ads.doubleclick.net/cotton-integration.png'></body></html>";
            instrumentation.runOnMainSync(() -> {
                earlySupported.set(WebViewFeature.isFeatureSupported(WebViewFeature.DOCUMENT_START_SCRIPT));
                view.setVisibility(android.view.View.VISIBLE);
                view.loadDataWithBaseURL("https://m.youtube.com/watch?v=test", html, "text/html", "UTF-8", null);
            });
            long deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(20);
            JSONObject state = null;
            do {
                state = evaluate(instrumentation, view, "JSON.stringify({"
                    + "ready:document.readyState,early:!!window.earlyProtected,"
                    + "ads:document.querySelector('ins')?getComputedStyle(document.querySelector('ins')).display:'missing',"
                    + "content:document.getElementById('content')?getComputedStyle(document.getElementById('content')).display:'missing',"
                    + "stream:window.ytInitialPlayerResponse?.streamingData?.formats[0]?.url})");
                if (blocked.get() > 0 && "none".equals(state.optString("ads"))) break;
                Thread.sleep(100);
            } while (System.nanoTime() < deadline);
            assertTrue("Actual WebView request was not intercepted", blocked.get() > 0);
            assertEquals("Ad slot was not hidden", "none", state.getString("ads"));
            assertNotEquals("Normal page content hidden", "none", state.getString("content"));
            assertEquals("Video URL changed", "https://rr1.googlevideo.com/videoplayback", state.getString("stream"));
            if (earlySupported.get()) assertTrue("Document-start script arrived too late", state.getBoolean("early"));

            AdBlocker engine = (AdBlocker) field(activity, "adBlocker");
            WebResourceRequest mainAd = new Request("https://popads.net/popup", true);
            assertNull("Typed main documents must remain accessible", engine.intercept(mainAd, "https://example.org", false));
            assertNotNull("Popup first document must be blocked", engine.intercept(mainAd, "https://example.org", true));
            assertNull("Login popup must remain accessible", engine.intercept(
                    new Request("https://accounts.google.com/login", true), "https://example.org", true));
            instrumentation.runOnMainSync(() -> {
                try {
                    Field exceptions = MainActivity.class.getDeclaredField("protectionExceptions");
                    exceptions.setAccessible(true);
                    exceptions.set(activity, Collections.singleton("m.youtube.com"));
                    assertNull("Disabled site still intercepted", view.getWebViewClient().shouldInterceptRequest(
                            view, new Request("https://ads.doubleclick.net/ad.js", false)));
                } catch (ReflectiveOperationException error) { throw new AssertionError(error); }
            });
        } finally {
            instrumentation.runOnMainSync(activity::finish);
        }
    }

    private static Object field(Object object, String name) throws Exception {
        Field field = object.getClass().getDeclaredField(name);
        field.setAccessible(true);
        return field.get(object);
    }

    private static JSONObject evaluate(Instrumentation instrumentation, WebView view, String script) throws Exception {
        CountDownLatch latch = new CountDownLatch(1);
        AtomicReference<String> result = new AtomicReference<>();
        instrumentation.runOnMainSync(() -> view.evaluateJavascript(script, value -> {
            result.set(value);
            latch.countDown();
        }));
        assertTrue("WebView script callback timed out", latch.await(5, TimeUnit.SECONDS));
        Object decoded = new JSONTokener(result.get()).nextValue();
        return decoded instanceof String ? new JSONObject((String) decoded) : new JSONObject();
    }

    private static final class Request implements WebResourceRequest {
        private final Uri uri;
        private final boolean main;
        Request(String url, boolean main) { uri = Uri.parse(url); this.main = main; }
        public Uri getUrl() { return uri; }
        public boolean isForMainFrame() { return main; }
        public boolean isRedirect() { return false; }
        public boolean hasGesture() { return true; }
        public String getMethod() { return "GET"; }
        public Map<String, String> getRequestHeaders() { return Collections.emptyMap(); }
    }
}
