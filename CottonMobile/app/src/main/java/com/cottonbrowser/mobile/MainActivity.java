package com.cottonbrowser.mobile;

import android.app.Activity;
import android.app.AlertDialog;
import android.app.DownloadManager;
import android.content.ClipboardManager;
import android.content.ClipData;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.content.pm.ProviderInfo;
import android.graphics.Bitmap;
import android.graphics.Color;
import android.graphics.Insets;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.os.Message;
import android.os.Process;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputMethodManager;
import android.webkit.CookieManager;
import android.webkit.DownloadListener;
import android.webkit.SslErrorHandler;
import android.webkit.URLUtil;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.net.http.SslError;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.HorizontalScrollView;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.PopupMenu;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.net.URLEncoder;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/** A deliberately local-first browser. Web pages never receive a JavaScript bridge. */
public final class MainActivity extends Activity {
    private static final int BACKGROUND = 0xFF1D1D2B;
    private static final int HEADER = 0xFF202131;
    private static final int SURFACE = 0xFF303246;
    private static final int SURFACE_ACTIVE = 0xFF41445C;
    private static final int TEXT = 0xFFF5F3FF;
    private static final int MUTED = 0xFFB4B4CB;
    private static final int ACCENT = 0xFFB69BFF;
    private static final int FILE_CHOOSER_REQUEST = 101;
    private static final int MAX_SELECTED_FILES = 16;
    private static final String PREFS_NAME = "cotton_mobile";
    private static final String BOOKMARKS_KEY = "bookmarks";

    private final List<BrowserTab> tabs = new ArrayList<>();
    private final List<Bookmark> bookmarks = new ArrayList<>();
    private BrowserTab activeTab;
    private FrameLayout pageHost;
    private LinearLayout tabItems;
    private HorizontalScrollView tabScroller;
    private EditText omnibox;
    private TextView backButton;
    private TextView forwardButton;
    private TextView reloadButton;
    private TextView bookmarkButton;
    private ProgressBar progressBar;
    private ValueCallback<Uri[]> fileChooserCallback;
    private boolean fileChooserAllowsMultiple;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().setStatusBarColor(HEADER);
        getWindow().setNavigationBarColor(HEADER);
        WebView.setWebContentsDebuggingEnabled(false);
        readBookmarks();
        buildInterface();
        addTab(null, true);
    }

    private void buildInterface() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(BACKGROUND);
        setContentView(root);

        // Android 15 enforces edge-to-edge for target 35. Keep controls clear of system bars.
        if (Build.VERSION.SDK_INT >= 35) {
            root.setOnApplyWindowInsetsListener((view, windowInsets) -> {
                Insets bars = windowInsets.getInsets(WindowInsets.Type.systemBars());
                root.setPadding(bars.left, bars.top, bars.right, bars.bottom);
                return windowInsets;
            });
        }

        LinearLayout tabsRow = new LinearLayout(this);
        tabsRow.setGravity(Gravity.CENTER_VERTICAL);
        tabsRow.setBackgroundColor(HEADER);
        root.addView(tabsRow, new LinearLayout.LayoutParams(-1, dp(48)));

        ImageView brand = new ImageView(this);
        brand.setImageResource(R.drawable.cotton_icon);
        brand.setScaleType(ImageView.ScaleType.FIT_CENTER);
        brand.setContentDescription("CottonBrowser");
        LinearLayout.LayoutParams brandParams = new LinearLayout.LayoutParams(dp(36), dp(36));
        brandParams.setMargins(dp(8), 0, dp(4), 0);
        tabsRow.addView(brand, brandParams);
        brand.setOnClickListener(view -> showHome());

        tabScroller = new HorizontalScrollView(this);
        tabScroller.setHorizontalScrollBarEnabled(false);
        tabScroller.setFillViewport(false);
        tabsRow.addView(tabScroller, new LinearLayout.LayoutParams(0, -1, 1));
        tabItems = new LinearLayout(this);
        tabItems.setGravity(Gravity.CENTER_VERTICAL);
        tabScroller.addView(tabItems, new HorizontalScrollView.LayoutParams(-2, -1));

        TextView addButton = toolbarButton("+", "Nova guia");
        tabsRow.addView(addButton, new LinearLayout.LayoutParams(dp(44), dp(40)));
        addButton.setOnClickListener(view -> addTab(null, true));

        LinearLayout toolbar = new LinearLayout(this);
        toolbar.setPadding(dp(6), dp(4), dp(6), dp(6));
        toolbar.setGravity(Gravity.CENTER_VERTICAL);
        toolbar.setBackgroundColor(HEADER);
        root.addView(toolbar, new LinearLayout.LayoutParams(-1, dp(56)));

        backButton = toolbarButton("‹", "Voltar");
        forwardButton = toolbarButton("›", "Avançar");
        reloadButton = toolbarButton("⟳", "Atualizar");
        toolbar.addView(backButton, new LinearLayout.LayoutParams(dp(34), dp(42)));
        toolbar.addView(forwardButton, new LinearLayout.LayoutParams(dp(34), dp(42)));
        toolbar.addView(reloadButton, new LinearLayout.LayoutParams(dp(38), dp(42)));
        backButton.setOnClickListener(view -> {
            if (activeTab != null && activeTab.webView.canGoBack()) activeTab.webView.goBack();
        });
        forwardButton.setOnClickListener(view -> {
            if (activeTab != null && activeTab.webView.canGoForward()) activeTab.webView.goForward();
        });
        reloadButton.setOnClickListener(view -> {
            if (activeTab == null) return;
            if (activeTab.url == null) focusOmnibox();
            else if (activeTab.loading) activeTab.webView.stopLoading();
            else activeTab.webView.reload();
        });

        LinearLayout addressSurface = new LinearLayout(this);
        addressSurface.setGravity(Gravity.CENTER_VERTICAL);
        addressSurface.setBackground(rounded(SURFACE, 22));
        LinearLayout.LayoutParams addressParams = new LinearLayout.LayoutParams(0, dp(42), 1);
        addressParams.setMargins(dp(3), 0, dp(3), 0);
        toolbar.addView(addressSurface, addressParams);

        omnibox = new EditText(this);
        omnibox.setSingleLine(true);
        omnibox.setTextSize(13);
        omnibox.setTextColor(TEXT);
        omnibox.setHintTextColor(MUTED);
        omnibox.setHint("Pesquise ou digite um endereço");
        omnibox.setBackgroundColor(Color.TRANSPARENT);
        omnibox.setPadding(dp(13), 0, dp(4), 0);
        omnibox.setSelectAllOnFocus(true);
        omnibox.setImeOptions(EditorInfo.IME_ACTION_GO);
        omnibox.setInputType(android.text.InputType.TYPE_CLASS_TEXT | android.text.InputType.TYPE_TEXT_VARIATION_URI);
        addressSurface.addView(omnibox, new LinearLayout.LayoutParams(0, -1, 1));
        omnibox.setOnEditorActionListener((textView, actionId, event) -> {
            if (actionId != EditorInfo.IME_ACTION_GO && actionId != EditorInfo.IME_ACTION_SEARCH) return false;
            navigateFromOmnibox();
            return true;
        });

        bookmarkButton = toolbarButton("☆", "Adicionar aos favoritos");
        addressSurface.addView(bookmarkButton, new LinearLayout.LayoutParams(dp(38), dp(38)));
        bookmarkButton.setOnClickListener(view -> toggleBookmark());

        TextView menuButton = toolbarButton("⋮", "Mais opções");
        toolbar.addView(menuButton, new LinearLayout.LayoutParams(dp(38), dp(42)));
        menuButton.setOnClickListener(this::showMenu);

        progressBar = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        progressBar.setMax(100);
        progressBar.setProgressTintList(android.content.res.ColorStateList.valueOf(ACCENT));
        progressBar.setProgressBackgroundTintList(android.content.res.ColorStateList.valueOf(HEADER));
        progressBar.setVisibility(View.INVISIBLE);
        root.addView(progressBar, new LinearLayout.LayoutParams(-1, dp(2)));

        pageHost = new FrameLayout(this);
        pageHost.setBackgroundColor(BACKGROUND);
        root.addView(pageHost, new LinearLayout.LayoutParams(-1, 0, 1));
    }

    private TextView toolbarButton(String symbol, String description) {
        TextView button = new TextView(this);
        button.setText(symbol);
        button.setTextSize(25);
        button.setTextColor(TEXT);
        button.setGravity(Gravity.CENTER);
        button.setContentDescription(description);
        button.setBackground(rounded(HEADER, 12));
        return button;
    }

    private BrowserTab addTab(String url, boolean activate) {
        return addTab(url, activate, true);
    }

    private BrowserTab addTab(String url, boolean activate, boolean focusNewTab) {
        BrowserTab tab = new BrowserTab();
        tab.title = "Nova guia";
        tab.page = new FrameLayout(this);
        tab.page.setBackgroundColor(BACKGROUND);
        tab.webView = createWebView(tab);
        tab.page.addView(tab.webView, new FrameLayout.LayoutParams(-1, -1));
        tab.webView.setVisibility(View.GONE);
        tab.home = createHomeView();
        tab.page.addView(tab.home, new FrameLayout.LayoutParams(-1, -1));
        tab.error = createErrorView(tab);
        tab.error.setVisibility(View.GONE);
        tab.page.addView(tab.error, new FrameLayout.LayoutParams(-1, -1));
        tab.page.setVisibility(View.GONE);
        tabs.add(tab);
        pageHost.addView(tab.page, new FrameLayout.LayoutParams(-1, -1));
        if (activate) selectTab(tab);
        else renderTabs();
        if (url != null) navigate(tab, url);
        else if (activate && focusNewTab) focusOmnibox();
        return tab;
    }

    private WebView createWebView(BrowserTab tab) {
        WebView webView = new WebView(this);
        webView.setBackgroundColor(Color.WHITE);
        webView.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_YES);
        WebSettings settings = webView.getSettings();
        settings.setJavaScriptEnabled(true); // Required by modern sites; no native JavaScript bridge is exposed.
        settings.setDomStorageEnabled(true);
        settings.setSupportMultipleWindows(true);
        settings.setJavaScriptCanOpenWindowsAutomatically(false);
        settings.setAllowFileAccess(false);
        settings.setAllowContentAccess(false);
        settings.setAllowFileAccessFromFileURLs(false);
        settings.setAllowUniversalAccessFromFileURLs(false);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        settings.setSafeBrowsingEnabled(true);
        settings.setMediaPlaybackRequiresUserGesture(true);
        settings.setSupportZoom(true);
        settings.setBuiltInZoomControls(true);
        settings.setDisplayZoomControls(false);
        CookieManager.getInstance().setAcceptThirdPartyCookies(webView, false);
        webView.setWebViewClient(new BrowserClient(tab));
        webView.setWebChromeClient(new BrowserChrome(tab));
        webView.setDownloadListener(downloadListener());
        webView.setOnLongClickListener(view -> {
            WebView.HitTestResult hit = webView.getHitTestResult();
            if (hit == null || !isWebUrl(hit.getExtra())) return false;
            String link = hit.getExtra();
            new AlertDialog.Builder(this)
                    .setItems(new String[]{"Abrir em nova guia", "Copiar endereço"}, (dialog, choice) -> {
                        if (choice == 0) addTab(link, true);
                        else {
                            ClipboardManager clipboard = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
                            clipboard.setPrimaryClip(ClipData.newPlainText("Endereço", link));
                            toast("Endereço copiado");
                        }
                    }).show();
            return true;
        });
        return webView;
    }

    private View createHomeView() {
        LinearLayout home = new LinearLayout(this);
        home.setOrientation(LinearLayout.VERTICAL);
        home.setGravity(Gravity.CENTER);
        home.setPadding(dp(24), dp(24), dp(24), dp(24));
        home.setBackgroundColor(BACKGROUND);
        ImageView logo = new ImageView(this);
        logo.setImageResource(R.drawable.cotton_icon);
        logo.setScaleType(ImageView.ScaleType.FIT_CENTER);
        home.addView(logo, new LinearLayout.LayoutParams(dp(70), dp(70)));
        TextView title = text("CottonBrowser", 27, TEXT);
        title.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        LinearLayout.LayoutParams titleParams = new LinearLayout.LayoutParams(-2, -2);
        titleParams.topMargin = dp(15);
        home.addView(title, titleParams);
        TextView subtitle = text("Navegue do seu jeito", 14, MUTED);
        LinearLayout.LayoutParams subtitleParams = new LinearLayout.LayoutParams(-2, -2);
        subtitleParams.topMargin = dp(5);
        home.addView(subtitle, subtitleParams);
        TextView search = text("Pesquisar ou digitar endereço  ↗", 14, TEXT);
        search.setGravity(Gravity.CENTER_VERTICAL);
        search.setPadding(dp(18), 0, dp(18), 0);
        search.setBackground(rounded(SURFACE, 18));
        LinearLayout.LayoutParams searchParams = new LinearLayout.LayoutParams(-1, dp(52));
        searchParams.topMargin = dp(30);
        home.addView(search, searchParams);
        search.setOnClickListener(view -> focusOmnibox());
        return home;
    }

    private View createErrorView(BrowserTab tab) {
        LinearLayout error = new LinearLayout(this);
        error.setOrientation(LinearLayout.VERTICAL);
        error.setGravity(Gravity.CENTER);
        error.setPadding(dp(32), dp(24), dp(32), dp(24));
        error.setBackgroundColor(BACKGROUND);
        TextView icon = text("⚠", 38, ACCENT);
        error.addView(icon);
        TextView title = text("Não foi possível abrir esta página", 19, TEXT);
        title.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        title.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams titleParams = new LinearLayout.LayoutParams(-1, -2);
        titleParams.topMargin = dp(12);
        error.addView(title, titleParams);
        TextView details = text("Verifique sua conexão e tente novamente.", 13, MUTED);
        details.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams detailParams = new LinearLayout.LayoutParams(-1, -2);
        detailParams.topMargin = dp(8);
        error.addView(details, detailParams);
        tab.errorDetails = details;
        TextView retry = text("Tentar novamente", 14, TEXT);
        retry.setGravity(Gravity.CENTER);
        retry.setBackground(rounded(SURFACE_ACTIVE, 15));
        LinearLayout.LayoutParams retryParams = new LinearLayout.LayoutParams(dp(175), dp(46));
        retryParams.topMargin = dp(24);
        error.addView(retry, retryParams);
        retry.setOnClickListener(view -> {
            if (tab.url != null) navigate(tab, tab.url);
        });
        return error;
    }

    private void selectTab(BrowserTab tab) {
        if (activeTab == tab) return;
        if (activeTab != null) {
            activeTab.webView.onPause();
            activeTab.page.setVisibility(View.GONE);
        }
        activeTab = tab;
        tab.page.setVisibility(View.VISIBLE);
        tab.webView.onResume();
        renderTabs();
        updateControls();
    }

    private void closeTab(BrowserTab tab) {
        int index = tabs.indexOf(tab);
        if (index < 0) return;
        boolean wasActive = activeTab == tab;
        tabs.remove(index);
        if (wasActive) activeTab = null;
        pageHost.removeView(tab.page);
        tab.webView.stopLoading();
        tab.webView.setWebChromeClient(null);
        tab.webView.setWebViewClient(null);
        tab.webView.destroy();
        if (tabs.isEmpty()) addTab(null, true);
        else if (wasActive) selectTab(tabs.get(Math.min(index, tabs.size() - 1)));
        else renderTabs();
    }

    private void renderTabs() {
        tabItems.removeAllViews();
        for (BrowserTab tab : tabs) {
            LinearLayout chip = new LinearLayout(this);
            chip.setGravity(Gravity.CENTER_VERTICAL);
            chip.setBackground(rounded(tab == activeTab ? SURFACE_ACTIVE : SURFACE, 13));
            LinearLayout.LayoutParams chipParams = new LinearLayout.LayoutParams(dp(178), dp(38));
            chipParams.setMargins(dp(3), 0, dp(3), 0);
            tabItems.addView(chip, chipParams);
            if (tab.favicon != null) {
                ImageView favicon = new ImageView(this);
                favicon.setImageBitmap(tab.favicon);
                LinearLayout.LayoutParams iconParams = new LinearLayout.LayoutParams(dp(17), dp(17));
                iconParams.setMargins(dp(9), 0, dp(5), 0);
                chip.addView(favicon, iconParams);
            }
            TextView label = text(tab.title, 12, TEXT);
            label.setSingleLine(true);
            label.setEllipsize(android.text.TextUtils.TruncateAt.END);
            if (tab.favicon == null) label.setPadding(dp(11), 0, 0, 0);
            chip.addView(label, new LinearLayout.LayoutParams(0, -2, 1));
            label.setOnClickListener(view -> selectTab(tab));
            chip.setOnClickListener(view -> selectTab(tab));
            TextView close = text("×", 20, MUTED);
            close.setGravity(Gravity.CENTER);
            close.setContentDescription("Fechar " + tab.title);
            chip.addView(close, new LinearLayout.LayoutParams(dp(32), -1));
            close.setOnClickListener(view -> closeTab(tab));
        }
    }

    private void navigateFromOmnibox() {
        if (activeTab == null) return;
        String input = omnibox.getText().toString().trim();
        if (input.isEmpty()) return;
        String url = resolveInput(input);
        if (url == null) {
            toast("Apenas endereços HTTP e HTTPS podem ser abertos aqui");
            return;
        }
        navigate(activeTab, url);
        omnibox.clearFocus();
        InputMethodManager keyboard = (InputMethodManager) getSystemService(INPUT_METHOD_SERVICE);
        keyboard.hideSoftInputFromWindow(omnibox.getWindowToken(), 0);
    }

    private String resolveInput(String input) {
        String lower = input.toLowerCase(Locale.ROOT);
        if (lower.equals("localhost") || lower.startsWith("localhost:") || lower.startsWith("localhost/")
                || lower.equals("127.0.0.1") || lower.startsWith("127.0.0.1:") || lower.startsWith("127.0.0.1/")) {
            String localUrl = "http://" + input;
            return isWebUrl(localUrl) ? localUrl : null;
        }
        if (input.matches("(?i)^[a-z][a-z0-9+.-]*:.*")) {
            return isWebUrl(input) ? input : null;
        }
        boolean address = !input.contains(" ") && (input.contains(".") || input.equalsIgnoreCase("localhost") || input.startsWith("localhost:"));
        if (address) {
            String url = "https://" + input;
            return isWebUrl(url) ? url : null;
        }
        try {
            return "https://www.google.com/search?q=" + URLEncoder.encode(input, "UTF-8");
        } catch (Exception ignored) {
            return null;
        }
    }

    private void navigate(BrowserTab tab, String url) {
        if (!isWebUrl(url)) return;
        tab.url = url;
        tab.error.setVisibility(View.GONE);
        tab.home.setVisibility(View.GONE);
        tab.webView.setVisibility(View.VISIBLE);
        tab.webView.loadUrl(url);
        if (tab == activeTab) updateControls();
    }

    private void showHome() {
        addTab(null, true);
    }

    private void focusOmnibox() {
        omnibox.requestFocus();
        omnibox.selectAll();
        omnibox.post(() -> {
            InputMethodManager keyboard = (InputMethodManager) getSystemService(INPUT_METHOD_SERVICE);
            keyboard.showSoftInput(omnibox, InputMethodManager.SHOW_IMPLICIT);
        });
    }

    private void updateControls() {
        if (activeTab == null) return;
        if (!omnibox.hasFocus()) omnibox.setText(activeTab.url == null ? "" : activeTab.url);
        backButton.setTextColor(activeTab.webView.canGoBack() ? TEXT : MUTED);
        forwardButton.setTextColor(activeTab.webView.canGoForward() ? TEXT : MUTED);
        reloadButton.setText(activeTab.loading ? "×" : "⟳");
        progressBar.setVisibility(activeTab.loading ? View.VISIBLE : View.INVISIBLE);
        progressBar.setProgress(activeTab.progress);
        boolean saved = activeTab.url != null && findBookmark(activeTab.url) >= 0;
        bookmarkButton.setText(saved ? "★" : "☆");
        bookmarkButton.setTextColor(saved ? ACCENT : TEXT);
        bookmarkButton.setContentDescription(saved ? "Remover dos favoritos" : "Adicionar aos favoritos");
    }

    private void showMenu(View anchor) {
        PopupMenu menu = new PopupMenu(this, anchor);
        menu.getMenu().add(0, 1, 0, "Nova guia");
        menu.getMenu().add(0, 2, 1, "Favoritos");
        menu.getMenu().add(0, 3, 2, "Adicionar aos favoritos");
        menu.getMenu().add(0, 4, 3, "Página inicial");
        menu.setOnMenuItemClickListener(item -> {
            switch (item.getItemId()) {
                case 1: addTab(null, true); return true;
                case 2: showBookmarks(); return true;
                case 3: toggleBookmark(); return true;
                case 4: showHome(); return true;
                default: return false;
            }
        });
        menu.show();
    }

    private void toggleBookmark() {
        if (activeTab == null || !isWebUrl(activeTab.url)) {
            toast("Abra um site para adicioná-lo aos favoritos");
            return;
        }
        int index = findBookmark(activeTab.url);
        if (index >= 0) {
            bookmarks.remove(index);
            toast("Removido dos favoritos");
        } else {
            bookmarks.add(new Bookmark(activeTab.url, activeTab.title));
            toast("Adicionado aos favoritos");
        }
        writeBookmarks();
        updateControls();
    }

    private void showBookmarks() {
        if (bookmarks.isEmpty()) {
            new AlertDialog.Builder(this).setTitle("Favoritos")
                    .setMessage("Seus sites favoritos aparecerão aqui.")
                    .setPositiveButton("OK", null).show();
            return;
        }
        String[] labels = new String[bookmarks.size()];
        for (int i = 0; i < bookmarks.size(); i++) labels[i] = bookmarks.get(i).title;
        new AlertDialog.Builder(this).setTitle("Favoritos")
                .setItems(labels, (dialog, index) -> navigate(activeTab, bookmarks.get(index).url))
                .setNeutralButton("Excluir favorito", (dialog, which) -> showRemoveBookmarks())
                .setNegativeButton("Fechar", null).show();
    }

    private void showRemoveBookmarks() {
        String[] labels = new String[bookmarks.size()];
        for (int i = 0; i < bookmarks.size(); i++) labels[i] = bookmarks.get(i).title;
        new AlertDialog.Builder(this).setTitle("Excluir favorito")
                .setItems(labels, (dialog, index) -> {
                    bookmarks.remove(index);
                    writeBookmarks();
                    updateControls();
                    toast("Favorito excluído");
                }).setNegativeButton("Cancelar", null).show();
    }

    private void readBookmarks() {
        bookmarks.clear();
        SharedPreferences prefs = getSharedPreferences(PREFS_NAME, MODE_PRIVATE);
        try {
            JSONArray array = new JSONArray(prefs.getString(BOOKMARKS_KEY, "[]"));
            for (int i = 0; i < array.length(); i++) {
                JSONObject item = array.getJSONObject(i);
                String url = item.optString("url");
                if (isWebUrl(url)) bookmarks.add(new Bookmark(url, item.optString("title", url)));
            }
        } catch (JSONException ignored) {
            // Corrupted local preferences should not prevent the browser from starting.
        }
    }

    private void writeBookmarks() {
        JSONArray array = new JSONArray();
        for (Bookmark bookmark : bookmarks) {
            JSONObject item = new JSONObject();
            try {
                item.put("url", bookmark.url);
                item.put("title", bookmark.title);
                array.put(item);
            } catch (JSONException ignored) { }
        }
        getSharedPreferences(PREFS_NAME, MODE_PRIVATE).edit().putString(BOOKMARKS_KEY, array.toString()).apply();
    }

    private int findBookmark(String url) {
        for (int i = 0; i < bookmarks.size(); i++) {
            if (bookmarks.get(i).url.equals(url)) return i;
        }
        return -1;
    }

    private DownloadListener downloadListener() {
        return (url, userAgent, contentDisposition, mimeType, contentLength) -> {
            if (!isWebUrl(url)) {
                toast("Este tipo de download não é compatível");
                return;
            }
            try {
                String suggested = URLUtil.guessFileName(url, contentDisposition, mimeType);
                String safeName = new File(suggested).getName().replaceAll("[\\\\/:*?\"<>|\\p{Cntrl}]", "_");
                if (safeName.length() > 120) safeName = safeName.substring(safeName.length() - 120);
                if (safeName.isEmpty()) safeName = "download";
                // A unique destination avoids overwriting a prior download with the same name.
                int dot = safeName.lastIndexOf('.');
                String stem = dot > 0 ? safeName.substring(0, dot) : safeName;
                String extension = dot > 0 ? safeName.substring(dot) : "";
                String filename = stem + "-" + System.currentTimeMillis() + extension;
                DownloadManager.Request request = new DownloadManager.Request(Uri.parse(url));
                request.setTitle(safeName);
                request.setDescription("Baixando pelo CottonBrowser");
                request.setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED);
                if (mimeType != null && !mimeType.isEmpty()) request.setMimeType(mimeType);
                if (userAgent != null) request.addRequestHeader("User-Agent", userAgent);
                String cookies = CookieManager.getInstance().getCookie(url);
                if (cookies != null && !cookies.isEmpty()) request.addRequestHeader("Cookie", cookies);
                if (Build.VERSION.SDK_INT >= 29) {
                    request.setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, filename);
                } else {
                    // App-specific Downloads needs no broad storage permission on Android 8/9.
                    request.setDestinationInExternalFilesDir(this, Environment.DIRECTORY_DOWNLOADS, filename);
                }
                DownloadManager manager = (DownloadManager) getSystemService(DOWNLOAD_SERVICE);
                manager.enqueue(request);
                toast("Download iniciado");
            } catch (Exception error) {
                toast("Não foi possível iniciar o download");
            }
        };
    }

    private boolean openExternal(String url) {
        Uri uri = Uri.parse(url);
        String scheme = uri.getScheme();
        if (scheme == null) return false;
        switch (scheme.toLowerCase(Locale.ROOT)) {
            case "mailto":
            case "tel":
            case "sms":
            case "geo":
            case "market":
                try {
                    Intent intent = new Intent(Intent.ACTION_VIEW, uri);
                    intent.addCategory(Intent.CATEGORY_BROWSABLE);
                    startActivity(intent);
                    return true;
                } catch (Exception ignored) {
                    toast("Nenhum aplicativo pode abrir este link");
                    return false;
                }
            default:
                toast("Este tipo de link foi bloqueado");
                return false;
        }
    }

    private static boolean isWebUrl(String url) {
        if (url == null) return false;
        Uri uri = Uri.parse(url);
        String scheme = uri.getScheme();
        return ("https".equalsIgnoreCase(scheme) || "http".equalsIgnoreCase(scheme))
                && uri.getHost() != null && !uri.getHost().isEmpty();
    }

    private void showError(BrowserTab tab, String details) {
        tab.loading = false;
        tab.errorDetails.setText(details);
        tab.error.setVisibility(View.VISIBLE);
        tab.home.setVisibility(View.GONE);
        tab.webView.setVisibility(View.GONE);
        if (tab == activeTab) updateControls();
    }

    private final class BrowserClient extends WebViewClient {
        private final BrowserTab tab;

        BrowserClient(BrowserTab tab) { this.tab = tab; }

        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            if (!request.isForMainFrame()) return false;
            String url = request.getUrl().toString();
            if (isWebUrl(url) || "about:blank".equals(url)) return false;
            openExternal(url);
            return true;
        }

        @Override
        public void onPageStarted(WebView view, String url, Bitmap favicon) {
            if (!tabs.contains(tab)) return;
            tab.loading = true;
            tab.progress = 5;
            if (isWebUrl(url)) tab.url = url;
            tab.error.setVisibility(View.GONE);
            tab.home.setVisibility(View.GONE);
            tab.webView.setVisibility(View.VISIBLE);
            if (tab == activeTab) updateControls();
        }

        @Override
        public void onPageFinished(WebView view, String url) {
            if (!tabs.contains(tab)) return;
            tab.loading = false;
            tab.progress = 100;
            if (isWebUrl(url)) tab.url = url;
            if (tab == activeTab) updateControls();
        }

        @Override
        public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
            if (request.isForMainFrame()) showError(tab, error.getDescription().toString());
        }

        @Override
        public void onReceivedHttpError(WebView view, WebResourceRequest request, WebResourceResponse response) {
            // Leave site-provided 4xx/5xx pages visible; only network and TLS failures get an overlay.
        }

        @Override
        public void onReceivedSslError(WebView view, SslErrorHandler handler, SslError error) {
            handler.cancel();
            showError(tab, "A conexão não é segura. A página foi bloqueada.");
        }

    }

    private final class BrowserChrome extends WebChromeClient {
        private final BrowserTab tab;

        BrowserChrome(BrowserTab tab) { this.tab = tab; }

        @Override
        public void onProgressChanged(WebView view, int progress) {
            if (!tabs.contains(tab)) return;
            tab.progress = progress;
            if (tab == activeTab) updateControls();
        }

        @Override
        public void onReceivedTitle(WebView view, String title) {
            if (!tabs.contains(tab)) return;
            if (title != null && !title.isEmpty() && !"about:blank".equals(title)) {
                tab.title = title;
                renderTabs();
            }
        }

        @Override
        public void onReceivedIcon(WebView view, Bitmap icon) {
            if (!tabs.contains(tab)) return;
            tab.favicon = icon;
            renderTabs();
        }

        @Override
        public boolean onCreateWindow(WebView view, boolean isDialog, boolean isUserGesture, Message resultMsg) {
            // Target=_blank and user-triggered window.open() are regular, isolated tabs in the same profile.
            if (!isUserGesture || isDialog || resultMsg == null) return false;
            BrowserTab newTab = addTab(null, true, false);
            WebView.WebViewTransport transport = (WebView.WebViewTransport) resultMsg.obj;
            transport.setWebView(newTab.webView);
            resultMsg.sendToTarget();
            return true;
        }

        @Override
        public void onCloseWindow(WebView window) {
            for (BrowserTab candidate : new ArrayList<>(tabs)) {
                if (candidate.webView == window) {
                    closeTab(candidate);
                    break;
                }
            }
        }

        @Override
        public boolean onShowFileChooser(WebView webView, ValueCallback<Uri[]> callback, FileChooserParams params) {
            if (fileChooserCallback != null) fileChooserCallback.onReceiveValue(null);
            fileChooserCallback = callback;
            fileChooserAllowsMultiple = params.getMode() == FileChooserParams.MODE_OPEN_MULTIPLE;
            try {
                Intent picker = params.createIntent();
                picker.addCategory(Intent.CATEGORY_OPENABLE);
                startActivityForResult(picker, FILE_CHOOSER_REQUEST);
                return true;
            } catch (Exception error) {
                fileChooserCallback = null;
                fileChooserAllowsMultiple = false;
                callback.onReceiveValue(null);
                toast("Nenhum aplicativo para escolher arquivo");
                return true;
            }
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == FILE_CHOOSER_REQUEST && fileChooserCallback != null) {
            fileChooserCallback.onReceiveValue(verifiedPickerUris(resultCode, data));
            fileChooserCallback = null;
            fileChooserAllowsMultiple = false;
        }
    }

    /** Never pass an untrusted picker URI to WebView without a picker read grant. */
    private Uri[] verifiedPickerUris(int resultCode, Intent result) {
        if (resultCode != RESULT_OK || result == null
                || (result.getFlags() & Intent.FLAG_GRANT_READ_URI_PERMISSION) == 0) return null;

        List<Uri> selected = new ArrayList<>();
        ClipData clipData = result.getClipData();
        if (clipData != null) {
            int count = clipData.getItemCount();
            if (count < 1 || count > MAX_SELECTED_FILES || (!fileChooserAllowsMultiple && count > 1)) return null;
            for (int i = 0; i < count; i++) {
                Uri uri = clipData.getItemAt(i).getUri();
                if (!isSafePickerUri(uri)) return null; // Reject the entire result, not just one item.
                if (!selected.contains(uri)) selected.add(uri);
            }
        } else {
            Uri uri = result.getData();
            if (!isSafePickerUri(uri)) return null;
            selected.add(uri);
        }
        return selected.isEmpty() ? null : selected.toArray(new Uri[0]);
    }

    private boolean isSafePickerUri(Uri uri) {
        if (uri == null || !"content".equals(uri.getScheme())) return false;
        String authority = uri.getAuthority();
        if (authority == null || authority.isEmpty()) return false;
        String ownPackage = getPackageName();
        if (authority.equals(ownPackage) || authority.startsWith(ownPackage + ".")) return false;
        ProviderInfo provider = getPackageManager().resolveContentProvider(authority, 0);
        if (provider != null && ownPackage.equals(provider.packageName)) return false;
        return checkUriPermission(uri, Process.myPid(), Process.myUid(),
                Intent.FLAG_GRANT_READ_URI_PERMISSION) == PackageManager.PERMISSION_GRANTED;
    }

    @Override
    public void onBackPressed() {
        if (activeTab != null && activeTab.webView.canGoBack()) activeTab.webView.goBack();
        else if (tabs.size() > 1) closeTab(activeTab);
        else super.onBackPressed();
    }

    @Override
    protected void onPause() {
        if (activeTab != null) activeTab.webView.onPause();
        super.onPause();
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (activeTab != null) activeTab.webView.onResume();
    }

    @Override
    protected void onDestroy() {
        if (fileChooserCallback != null) {
            fileChooserCallback.onReceiveValue(null);
            fileChooserCallback = null;
        }
        for (BrowserTab tab : tabs) {
            pageHost.removeView(tab.page);
            tab.webView.destroy();
        }
        tabs.clear();
        super.onDestroy();
    }

    private TextView text(String value, int sizeSp, int color) {
        TextView view = new TextView(this);
        view.setText(value);
        view.setTextSize(sizeSp);
        view.setTextColor(color);
        return view;
    }

    private GradientDrawable rounded(int color, int radiusDp) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setColor(color);
        drawable.setCornerRadius(dp(radiusDp));
        return drawable;
    }

    private int dp(float value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    private void toast(String message) {
        Toast.makeText(this, message, Toast.LENGTH_SHORT).show();
    }

    private static final class Bookmark {
        final String url;
        final String title;

        Bookmark(String url, String title) {
            this.url = url;
            this.title = title == null || title.isEmpty() ? url : title;
        }
    }

    private static final class BrowserTab {
        FrameLayout page;
        WebView webView;
        View home;
        View error;
        TextView errorDetails;
        String title;
        String url;
        Bitmap favicon;
        int progress;
        boolean loading;
    }
}
