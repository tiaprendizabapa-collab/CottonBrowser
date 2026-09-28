using System.Reflection;
using LeanBrowser;
using CottonBrowser.Shared;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var tabs = new BrowserTabControl();
        var allowlist = new SiteAllowlist(new SiteExceptionStore(Path.Combine(Path.GetTempPath(),
            "CottonTabChecks-" + Guid.NewGuid().ToString("N") + ".json")));
        var normal = new BrowserTab(allowlist) { Text = "Página normal" };
        var settings = new TabPage("Configurações");
        var privateTab = new BrowserTab(allowlist, isPrivate: true) { Text = "Anônima" };
        var downloads = new TabPage("Downloads");
        tabs.TabPages.AddRange([normal, settings, privateTab, downloads]);
        _ = tabs.Handle;

        // Não inicializa WebView2: nenhuma navegação, cookie ou perfil real é usado.
        var manager = new BrowserTabManager(tabs, null!, allowlist);
        tabs.SelectedTab = normal;
        foreach (var expected in new TabPage[] { settings, privateTab, downloads, normal })
        {
            manager.SelectNext(1);
            Check(tabs.SelectedTab == expected, "Ctrl+Tab deve percorrer páginas e guias auxiliares.");
        }
        manager.SelectNext(-1);
        Check(tabs.SelectedTab == downloads, "Ctrl+Shift+Tab deve voltar à última guia.");

        var headers = tabs.HeaderStrip.Controls.Cast<Control>()
            .Where(control => control.AccessibleRole == AccessibleRole.PageTab).ToArray();
        Check(headers.Select(header => header.AccessibleName).SequenceEqual(
                tabs.TabPages.Cast<TabPage>().Select(tab => tab.Text)),
            "A ordem dos cabeçalhos deve acompanhar a ordem das guias.");

        BrowserTab? browserClose = null;
        TabPage? auxiliaryClose = null;
        tabs.CloseRequested += tab => browserClose = tab;
        tabs.AuxiliaryCloseRequested += tab => auxiliaryClose = tab;
        MiddleClick(headers[2]);
        Check(browserClose == privateTab && tabs.SelectedTab == downloads,
            "O clique do meio deve fechar a guia anônima apontada sem selecionar outra guia.");
        MiddleClick(headers[1]);
        Check(auxiliaryClose == settings,
            "O clique do meio deve também fechar guias auxiliares.");

        tabs.SelectedTab = privateTab;
        manager.Close(privateTab);
        Check(tabs.SelectedTab == downloads,
            "Fechar uma página deve selecionar a vizinha, incluindo Downloads.");
        Check(privateTab.IsDisposed && tabs.TabCount == 3,
            "Fechar uma guia deve descartá-la e preservar as demais.");
        Check(normal.Web.CoreWebView2 is null,
            "A verificação não deve inicializar o perfil do navegador.");
        Console.WriteLine("PASS: ordem, alternância, fechamento e clique do meio em guias normais, anônimas e auxiliares.");
        DownloadChecks.Run();
        MenuChecks.Run();
    }

    private static void MiddleClick(Control header) =>
        typeof(Control).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(header, [new MouseEventArgs(MouseButtons.Middle, 1, 20, 20, 0)]);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
