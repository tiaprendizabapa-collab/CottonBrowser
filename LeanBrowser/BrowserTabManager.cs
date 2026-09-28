using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using CottonBrowser.Shared;

namespace LeanBrowser;

public sealed class BrowserTab : TabPage
{
    public bool IsPrivate { get; }
    public WebView2 Web { get; } = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Theme.Chrome };
    public AdBlocker Blocker { get; }
    public PopupPolicy Popups { get; } = new();
    public DocumentProtection DocumentProtection { get; } = new();
    public NetworkProtection? NetworkProtection { get; set; }
    public IDisposable? PermissionSubscription { get; set; }
    public bool Loading { get; set; }
    public bool FocusOmniboxOnFirstLoad { get; set; }
    internal TabNavigationState Navigation { get; } = new();
    public BrowserTab(SiteAllowlist siteAllowlist, bool isPrivate = false) : base(isPrivate ? "Guia anônima" : "Nova aba")
    {
        IsPrivate = isPrivate;
        Blocker = new AdBlocker(siteAllowlist);
        Controls.Add(Web);
    }

    public void NavigateOrQueue(string url)
    {
        if (IsDisposed) return;
        FocusOmniboxOnFirstLoad = false;
        if (Navigation.Request(url) is { } target)
            Web.CoreWebView2.Navigate(target);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            NetworkProtection?.Dispose();
            PermissionSubscription?.Dispose();
        }
        base.Dispose(disposing);
    }
}

public sealed class BrowserTabManager(BrowserTabControl view, CoreWebView2Environment environment,
    SiteAllowlist siteAllowlist)
{
    public BrowserTab? Active => view.SelectedTab as BrowserTab;
    public Func<BrowserTab, Task>? InitializeTabAsync { get; set; }

    public async Task<BrowserTab?> CreateAsync(string url, bool isPrivate = false,
        bool focusOmniboxOnFirstLoad = false)
    {
        var tab = new BrowserTab(siteAllowlist, isPrivate)
        {
            FocusOmniboxOnFirstLoad = focusOmniboxOnFirstLoad
        };
        // As guias de páginas e as de configurações compartilham a mesma ordem.
        view.TabPages.Add(tab);
        view.SelectedTab = tab;
        try
        {
            if (isPrivate)
            {
                var options = environment.CreateCoreWebView2ControllerOptions();
                options.ProfileName = "CottonBrowserPrivate";
                options.IsInPrivateModeEnabled = true;
                await tab.Web.EnsureCoreWebView2Async(environment, options);
                if (!tab.Web.CoreWebView2.Profile.IsInPrivateModeEnabled)
                    throw new InvalidOperationException("O modo InPrivate não foi ativado pelo WebView2.");
            }
            else
                await tab.Web.EnsureCoreWebView2Async(environment);
            if (tab.IsDisposed || view.IsDisposed) return null;
            WebContentIsolation.ConfigureUntrustedTab(tab.Web);
            // A política de senhas é compartilhada pelo perfil; aplique antes de
            // qualquer inicialização assíncrona ou navegação de outra guia.
            PasswordManager.Configure(tab.Web.CoreWebView2, tab.IsPrivate);
            if (InitializeTabAsync is not null) await InitializeTabAsync(tab);
            if (tab.IsDisposed || view.IsDisposed) return null;
            tab.Web.CoreWebView2.Navigate(tab.Navigation.CompleteInitialization(url));
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
        if (wasActive && view.TabCount > 0)
            view.SelectedIndex = Math.Min(index, view.TabCount - 1);
        tab.Dispose(); // encerra o WebView e seus handlers
    }

    public void SelectNext(int direction)
    {
        var count = view.TabCount;
        if (count > 0)
            view.SelectedIndex = (Math.Max(0, view.SelectedIndex) + direction + count) % count;
    }
}
