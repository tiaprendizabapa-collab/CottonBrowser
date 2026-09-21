using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LeanBrowser;

public sealed class BrowserTab : TabPage
{
    public WebView2 Web { get; } = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
    public AdBlocker Blocker { get; } = new();
    public PopupPolicy Popups { get; } = new();
    public DocumentProtection DocumentProtection { get; } = new();
    public bool Loading { get; set; }
    public BrowserTab() : base("Nova aba") => Controls.Add(Web);
}

public sealed class BrowserTabManager(BrowserTabControl view, CoreWebView2Environment environment)
{
    public BrowserTab? Active => view.SelectedTab as BrowserTab;
    public Func<BrowserTab, Task>? InitializeTabAsync { get; set; }

    public async Task<BrowserTab?> CreateAsync(string url)
    {
        var tab = new BrowserTab();
        view.TabPages.Insert(view.BrowserTabCount, tab);
        view.SelectedTab = tab;
        try
        {
            await tab.Web.EnsureCoreWebView2Async(environment);
            if (tab.IsDisposed || view.IsDisposed) return null;
            if (InitializeTabAsync is not null) await InitializeTabAsync(tab);
            if (tab.IsDisposed || view.IsDisposed) return null;
            tab.Web.CoreWebView2.Navigate(url);
            return tab;
        }
        catch
        {
            if (tab.IsDisposed || view.IsDisposed) return null;
            view.TabPages.Remove(tab);
            tab.Dispose();
            throw;
        }
    }

    public void CloseActive()
    {
        if (Active is not { } tab) return;
        Close(tab);
    }

    public void Close(BrowserTab tab)
    {
        if (!view.TabPages.Contains(tab)) return;
        var index = view.TabPages.IndexOf(tab);
        var wasActive = Active == tab;
        view.TabPages.Remove(tab);
        if (wasActive && view.BrowserTabCount > 0)
            view.SelectedIndex = Math.Min(index, view.BrowserTabCount - 1);
        tab.Dispose(); // encerra o WebView e seus handlers
    }

    public void SelectNext(int direction)
    {
        var count = view.BrowserTabCount;
        if (count > 0)
            view.SelectedIndex = (Math.Max(0, view.SelectedIndex) + direction + count) % count;
    }
}
