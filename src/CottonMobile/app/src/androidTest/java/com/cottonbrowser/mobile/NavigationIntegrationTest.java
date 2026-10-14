package com.cottonbrowser.mobile;

import android.app.Instrumentation;
import android.content.Intent;
import android.net.Uri;
import android.webkit.ValueCallback;
import android.webkit.WebView;

import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;

import org.junit.Test;
import org.junit.runner.RunWith;

import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;

import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public final class NavigationIntegrationTest {
    @Test
    public void resolvesHostPortsAndRejectsUnsupportedSchemes() throws Exception {
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        MainActivity activity = launch(instrumentation);
        try {
            Method resolve = MainActivity.class.getDeclaredMethod("resolveInput", String.class);
            resolve.setAccessible(true);
            assertEquals("https://example.org:8443/path?x=1",
                    resolve.invoke(activity, "example.org:8443/path?x=1"));
            assertEquals("http://localhost:8080/path", resolve.invoke(activity, "localhost:8080/path"));
            assertEquals("https://example.org/path", resolve.invoke(activity, "https://example.org/path"));
            assertNull(resolve.invoke(activity, "javascript:alert(1)"));
            assertNull(resolve.invoke(activity, "file:///sdcard/private.txt"));
            Method sameDocument = MainActivity.class.getDeclaredMethod("isSameDocumentUrl", String.class, String.class);
            sameDocument.setAccessible(true);
            assertEquals(true, sameDocument.invoke(null, "https://example.org/page", "https://example.org/page#section"));
            assertEquals(false, sameDocument.invoke(null, "https://example.org/image", "https://example.org/page#section"));
            assertEquals(false, sameDocument.invoke(null, "https://example.org/page?a=1", "https://example.org/page?a=2"));
        } finally {
            instrumentation.runOnMainSync(activity::finish);
        }
    }

    @Test
    public void updatesHistoryApiNavigationAndIgnoresOldDocumentCallbacks() throws Exception {
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        MainActivity activity = launch(instrumentation);
        try {
            Object tab = field(activity, "activeTab");
            WebView view = (WebView) field(tab, "webView");
            String current = "https://example.test/current";
            instrumentation.runOnMainSync(() -> view.loadDataWithBaseURL(current,
                    "<html><body>navigation test</body></html>", "text/html", "UTF-8", null));
            waitForUrl(instrumentation, view, tab, current);
            instrumentation.runOnMainSync(() -> {
                try {
                    setField(tab, "loading", true);
                    setField(tab, "popupPending", true);
                    view.getWebViewClient().onPageFinished(view, "https://example.test/old");
                    view.getWebViewClient().onPageCommitVisible(view, "https://example.test/old");
                    assertEquals(current, field(tab, "url"));
                    assertEquals(true, field(tab, "loading"));
                    assertEquals(true, field(tab, "popupPending"));
                    setField(tab, "loading", false);
                    setField(tab, "popupPending", false);
                } catch (ReflectiveOperationException error) { throw new AssertionError(error); }
            });
            evaluate(instrumentation, view, "history.pushState({},'', '/next#section');true");
            waitForUrl(instrumentation, view, tab, "https://example.test/next#section");
            instrumentation.runOnMainSync(() -> {
                try {
                    assertEquals("https://example.test/next#section", field(tab, "url"));
                    setField(tab, "loading", true);
                    view.getWebViewClient().onPageFinished(view, current);
                    assertEquals("A completed load must preserve a History API change",
                            "https://example.test/next#section", field(tab, "url"));
                    assertEquals(false, field(tab, "loading"));
                } catch (ReflectiveOperationException error) { throw new AssertionError(error); }
            });
        } finally {
            instrumentation.runOnMainSync(activity::finish);
        }
    }

    @Test
    public void cancelsFileSelectionWhenItsTabCloses() throws Exception {
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        MainActivity activity = launch(instrumentation);
        try {
            Object tab = field(activity, "activeTab");
            AtomicInteger completions = new AtomicInteger();
            ValueCallback<Uri[]> callback = selected -> {
                assertNull("A closed tab must not receive selected files", selected);
                completions.incrementAndGet();
            };
            instrumentation.runOnMainSync(() -> {
                try {
                    setField(activity, "fileChooserCallback", callback);
                    setField(activity, "fileChooserTab", tab);
                    setField(activity, "fileChooserAllowsMultiple", true);
                    Method close = MainActivity.class.getDeclaredMethod("closeTab", tab.getClass());
                    close.setAccessible(true);
                    close.invoke(activity, tab);
                    assertEquals(1, completions.get());
                    assertNull(field(activity, "fileChooserCallback"));
                    assertNull(field(activity, "fileChooserTab"));
                    assertEquals(false, field(activity, "fileChooserAllowsMultiple"));
                } catch (ReflectiveOperationException error) { throw new AssertionError(error); }
            });
        } finally {
            instrumentation.runOnMainSync(activity::finish);
        }
    }

    @Test
    public void oldPickerResultCannotCompleteANewRequest() throws Exception {
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        MainActivity activity = launch(instrumentation);
        try {
            Object tab = field(activity, "activeTab");
            WebView view = (WebView) field(tab, "webView");
            AtomicInteger oldCompletions = new AtomicInteger();
            AtomicInteger rejectedCompletions = new AtomicInteger();
            ValueCallback<Uri[]> old = selected -> {
                assertNull(selected);
                oldCompletions.incrementAndGet();
            };
            instrumentation.runOnMainSync(() -> {
                try {
                    setField(activity, "fileChooserCallback", old);
                    setField(activity, "fileChooserTab", tab);
                    setField(activity, "fileChooserPending", true);
                    Method cancel = MainActivity.class.getDeclaredMethod("finishFileChooser", Uri[].class);
                    cancel.setAccessible(true);
                    cancel.invoke(activity, (Object) null);
                    assertEquals(1, oldCompletions.get());
                    assertEquals(true, field(activity, "fileChooserPending"));
                    // The pending guard runs before FileChooserParams are used.
                    assertTrue(view.getWebChromeClient().onShowFileChooser(view, selected -> {
                        assertNull(selected);
                        rejectedCompletions.incrementAndGet();
                    }, null));
                    assertEquals(1, rejectedCompletions.get());
                    assertNull(field(activity, "fileChooserCallback"));
                    activity.onActivityResult(101, MainActivity.RESULT_CANCELED, null);
                    assertEquals(false, field(activity, "fileChooserPending"));
                    assertEquals(1, oldCompletions.get());
                    assertEquals(1, rejectedCompletions.get());
                } catch (ReflectiveOperationException error) { throw new AssertionError(error); }
            });
        } finally {
            instrumentation.runOnMainSync(activity::finish);
        }
    }

    private static MainActivity launch(Instrumentation instrumentation) {
        var context = instrumentation.getTargetContext();
        context.getSharedPreferences("cotton_mobile", 0).edit()
                .putLong("last_update_check", System.currentTimeMillis()).commit();
        return (MainActivity) instrumentation.startActivitySync(
                new Intent(context, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
    }

    private static void waitForUrl(Instrumentation instrumentation, WebView view, Object tab, String expected) throws Exception {
        long deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(10);
        do {
            AtomicReference<String> actual = new AtomicReference<>();
            AtomicReference<Boolean> loaded = new AtomicReference<>();
            instrumentation.runOnMainSync(() -> {
                actual.set(view.getUrl());
                try {
                    loaded.set(expected.equals(field(tab, "url")) && !((Boolean) field(tab, "loading")));
                } catch (ReflectiveOperationException error) { throw new AssertionError(error); }
            });
            if (expected.equals(actual.get()) && loaded.get() && "\"complete\"".equals(
                    evaluate(instrumentation, view, "document.readyState"))) return;
            Thread.sleep(50);
        } while (System.nanoTime() < deadline);
        fail("Document did not reach " + expected);
    }

    private static String evaluate(Instrumentation instrumentation, WebView view, String script) throws Exception {
        CountDownLatch latch = new CountDownLatch(1);
        AtomicReference<String> result = new AtomicReference<>();
        instrumentation.runOnMainSync(() -> view.evaluateJavascript(script, value -> {
            result.set(value);
            latch.countDown();
        }));
        assertTrue("WebView callback timed out", latch.await(5, TimeUnit.SECONDS));
        return result.get();
    }

    private static Object field(Object object, String name) throws ReflectiveOperationException {
        Field field = object.getClass().getDeclaredField(name);
        field.setAccessible(true);
        return field.get(object);
    }

    private static void setField(Object object, String name, Object value) throws ReflectiveOperationException {
        Field field = object.getClass().getDeclaredField(name);
        field.setAccessible(true);
        field.set(object, value);
    }
}
