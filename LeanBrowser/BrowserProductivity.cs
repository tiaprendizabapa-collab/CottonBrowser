using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace LeanBrowser;

public sealed partial class BrowserForm
{
    private readonly ProductivityStore _productivity = new(Path.Combine(BrowserPaths.DataDirectory, "productivity.json"));
    private PerformancePanel? _performancePanel;
    private readonly Dictionary<BrowserTab, FloatingVideoWindow> _floatingVideos = new();

    private void InstallProductivityMenus()
    {
        var entries = new (string Label, string Id, Action Action)[]
        {
            ("Vídeo flutuante", "video", () => _ = ToggleFloatingVideoAsync()),
            ("Espaços de trabalho", "workspaces", () => OpenFeatureSettings("Espaços de trabalho")),
            ("Lista de leitura", "reading", () => OpenFeatureSettings("Lista de leitura")),
            ("Painel de desempenho", "performance", ShowPerformance),
            ("Permissões e dados do site", "", () => _ = ShowSiteControlsAsync())
        };
        var index = _overflowMenu.Items.Cast<ToolStripItem>().ToList().FindIndex(i => i.Text == "Configurações");
        foreach (var entry in entries)
        {
            var item = new BrowserMenuItem(entry.Label, "\uE713"); item.Click += (_, _) => entry.Action();
            _overflowMenu.Items.Insert(index++, item);
        }
        _omnibox.SiteControlsRequested += () => _ = ShowSiteControlsAsync();
        _overflowMenu.Opening += (_, _) =>
        {
            foreach (var command in ShortcutCatalog.Commands)
            {
                var label = command.Id == "performance" ? "Painel de desempenho" : command.Label;
                var item = _overflowMenu.Items.OfType<BrowserMenuItem>().FirstOrDefault(i => i.Text == label || (command.Id == "reader" && i.Text == "Sair do modo de leitura"));
                if (item is not null) item.ShortcutKeyDisplayString = ShortcutCatalog.Display(ShortcutCatalog.Get(command.Id, BrowserPreferences.Current));
            }
        };
        FormClosed += (_, _) => { _performancePanel?.Close(); foreach (var video in _floatingVideos.Values.ToArray()) video.Close(); };
    }

    private void ConfigureProductivitySettings(SettingsTab settings)
    {
        settings.AddFeatureSection("Espaços de trabalho", "Seus espaços", "Conjuntos de abas para trabalho, estudos e navegação pessoal.",
            new ProductivityView(_productivity, false, SaveCurrentWorkspace, workspace => _ = OpenWorkspaceAsync(workspace), url => _ = OpenTabAsync(url)), 440);
        settings.AddFeatureSection("Lista de leitura", "Ler depois e anotações", "Guarde páginas para ler depois e registre suas ideias.",
            new ProductivityView(_productivity, true, SaveCurrentReading, workspace => _ = OpenWorkspaceAsync(workspace), url => _ = OpenTabAsync(url)), 490);
        settings.AddFeatureSection("Downloads", "Onde salvar os arquivos", "Escolha uma pasta e quando perguntar o destino.", new DownloadPreferencesView(), 300);
        settings.AddFeatureSection("Atalhos", "Atalhos do teclado", "Personalize os comandos do navegador.", new ShortcutPreferencesView(), 420);
        settings.AddFeatureSection("Backup e restauração", "Backup deste perfil", "Transfira suas preferências e organização para outro computador.", new BackupView(RefreshRestoredData), 330);
        settings.AddFeatureSection("Privacidade e proteção", "Limpar dados de navegação", "Escolha os tipos de dados e o período que deseja apagar.", new ClearDataView(ClearDataAsync), 330);
        var performance = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false };
        FeatureUi.WrapNotes(performance);
        performance.Controls.Add(FeatureUi.Note("Consulte a memória e a CPU usadas pelo navegador. Veja quais abas estão carregando, tocando áudio ou em repouso."));
        performance.Controls.Add(FeatureUi.Button("Abrir painel de desempenho", ShowPerformance));
        settings.AddFeatureSection("Desempenho", "Uso de recursos", "Acompanhe o navegador e gerencie as abas.", performance, 140);
        var zoom = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false };
        FeatureUi.WrapNotes(zoom);
        zoom.Controls.Add(FeatureUi.Note("O zoom escolhido para cada site é lembrado após fechar o navegador. Abas anônimas usam ajustes temporários."));
        zoom.Controls.Add(FeatureUi.Button("Redefinir zoom de todos os sites", () =>
        {
            BrowserPreferences.Current.SiteZoom.Clear(); BrowserPreferences.Current.Save();
            foreach (var window in Application.OpenForms.OfType<BrowserForm>())
                foreach (var tab in window._tabView.TabPages.OfType<BrowserTab>().Where(t => !t.IsPrivate)) tab.Web.SetPageZoom(1);
        }));
        settings.AddFeatureSection("Aparência", "Zoom por site", "Cada endereço mantém seu tamanho de visualização.", zoom, 140);
    }

    private void OpenFeatureSettings(string section) { OpenSettings(); _settingsTab?.ShowFeature(section); }
    private BrowserTab? SourcePage => ActiveBrowserTab ?? (_settingsBookmarkTab is { IsDisposed: false } previous ? previous : null);
    private void SaveCurrentWorkspace()
    {
        var tabs = _tabView.TabPages.OfType<BrowserTab>().Select(SessionTabState.Capture).OfType<SessionTabState>().ToArray();
        if (tabs.Length == 0) { MessageBox.Show(this, "Abra ao menos uma aba normal para salvar um espaço."); return; }
        if (UiDialogs.Fields(this, "Salvar espaço de trabalho", ("Nome do espaço", "")) is not { } input) return;
        var existing = _productivity.Load().Workspaces.FirstOrDefault(w => string.Equals(w.Name, input[0], StringComparison.CurrentCultureIgnoreCase));
        if (existing is not null && MessageBox.Show(this, "Atualizar as abas do espaço “" + existing.Name + "”?", "Espaços", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        var source = SourcePage; var state = source is null ? null : SessionTabState.Capture(source);
        var selected = state is null ? 0 : Math.Max(0, Array.FindIndex(tabs, t => t == state));
        _productivity.SaveWorkspace(input[0], tabs, selected, existing?.Id);
    }

    private async Task OpenWorkspaceAsync(WorkspaceEntry workspace)
    {
        if (_tabs is null) return;
        try
        {
            ProductivityStore.Validate(new([workspace], []));
            var created = new List<BrowserTab>();
            foreach (var state in workspace.Tabs)
            {
                if (IsDisposed) return;
                var tab = await _tabs.CreateAsync(state.Url, restoredState: state); if (tab is not null) created.Add(tab);
            }
            if (created.Count > 0) _tabView.SelectedTab = created[Math.Min(workspace.SelectedIndex, created.Count - 1)];
            SessionChanged?.Invoke();
        }
        catch (Exception ex) { if (!IsDisposed) UiDialogs.Error(this, ex); }
    }

    private void SaveCurrentReading()
    {
        var tab = SourcePage;
        if (tab is null || tab.IsPrivate || tab.Web.CoreWebView2 is not { } core || !BookmarkStore.IsWebUrl(core.Source) || TrustedBrowserBridge.IsTrustedUiUri(core.Source))
        { MessageBox.Show(this, "Abra uma página em uma aba normal para adicioná-la à lista."); return; }
        _productivity.SaveReading(core.Source, tab.Text);
    }

    private async Task ClearDataAsync(ClearDataRequest request)
    {
        var core = _tabView.TabPages.OfType<BrowserTab>().FirstOrDefault(t => !t.IsPrivate && t.Web.CoreWebView2 is not null)?.Web.CoreWebView2;
        if (core is null) throw new InvalidOperationException("Abra uma aba normal antes de limpar os dados deste perfil.");
        CoreWebView2BrowsingDataKinds kinds = 0;
        if (request.History) kinds |= CoreWebView2BrowsingDataKinds.BrowsingHistory;
        if (request.Cookies) kinds |= CoreWebView2BrowsingDataKinds.Cookies | CoreWebView2BrowsingDataKinds.AllDomStorage;
        if (request.Cache) kinds |= CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage;
        var until = DateTimeOffset.UtcNow;
        if (request.From is { } from) await core.Profile.ClearBrowsingDataAsync(kinds, from.UtcDateTime, until.UtcDateTime);
        else await core.Profile.ClearBrowsingDataAsync(kinds);
        if (request.History)
        {
            await _navigationHistory.Ready;
            _navigationHistory.RemoveRange(request.From, until); await _navigationHistory.FlushAsync();
        }
    }

    private void RefreshRestoredData()
    {
        foreach (var window in Application.OpenForms.OfType<BrowserForm>().ToArray())
        {
            window.RefreshBookmarksBar(); window._settingsTab?.ReloadBookmarks();
            if (File.Exists(_themePath)) window.ApplyTheme(File.ReadAllText(_themePath).Trim() == "dark");
            if (File.Exists(_accentPath)) window.ApplyAccent(ColorTranslator.FromHtml(File.ReadAllText(_accentPath).Trim()));
            foreach (var tab in window._tabView.TabPages.OfType<BrowserTab>().Where(t => !t.IsPrivate && t.Web.CoreWebView2 is not null))
                tab.Web.SetPageZoom(BrowserPreferences.Current.ZoomFor(tab.Web.CoreWebView2.Source));
        }
    }

    private bool IsCustomShortcut(Keys data) => ShortcutCatalog.Commands.Any(c => ShortcutCatalog.Get(c.Id, BrowserPreferences.Current) == data);
    private bool HandleCustomShortcut(Keys data)
    {
        var command = ShortcutCatalog.Commands.FirstOrDefault(c => ShortcutCatalog.Get(c.Id, BrowserPreferences.Current) == data);
        if (command is null) return false;
        switch (command.Id)
        {
            case "history": OpenHistory(); break;
            case "downloads": OpenDownloads(); break;
            case "reader": _ = ToggleReaderAsync(); break;
            case "split": if (_split is not null) EndSplit(); else ChooseSplit(); break;
            case "mute": if (_core is { } core) core.IsMuted = !core.IsMuted; break;
            case "settings": OpenSettings(); break;
            case "workspaces": OpenFeatureSettings("Espaços de trabalho"); break;
            case "reading": OpenFeatureSettings("Lista de leitura"); break;
            case "video": _ = ToggleFloatingVideoAsync(); break;
            case "performance": ShowPerformance(); break;
        }
        return true;
    }

    private void ShowPerformance()
    {
        if (_performancePanel is { IsDisposed: false }) { _performancePanel.Activate(); return; }
        _performancePanel = new PerformancePanel(() => _browserEnvironment,
            () => _tabView.TabPages.OfType<BrowserTab>().ToArray(),
            async tab => _memorySaver is not null && await _memorySaver.SuspendNowAsync(tab),
            tab => _memorySaver?.Resume(tab), tab => { EndSplit(); _tabs?.Close(tab); if (_tabView.TabCount == 0) Close(); });
        _performancePanel.Show(this);
    }

    private void ConfigureSiteZoom(BrowserTab tab)
    {
        if (!tab.IsPrivate)
        {
            tab.Web.SiteZoomLookup = BrowserPreferences.Current.ZoomFor;
            tab.Web.SiteZoomChanged = BrowserPreferences.Current.RememberZoom;
        }
        else
        {
            var temporary = new Dictionary<string, double>();
            tab.Web.SiteZoomLookup = url => BrowserPreferences.TryNormalizeExceptionHost(url, out var host) && temporary.TryGetValue(host, out var zoom) ? zoom : 1;
            tab.Web.SiteZoomChanged = (url, zoom) => { if (BrowserPreferences.TryNormalizeExceptionHost(url, out var host)) temporary[host] = zoom; };
        }
    }

    private async Task ToggleFloatingVideoAsync()
    {
        var tab = ActiveBrowserTab;
        if (tab is null || tab.Web.CoreWebView2 is not { } core) return;
        if (_floatingVideos.TryGetValue(tab, out var existing)) { existing.Close(); return; }
        if (TrustedBrowserBridge.IsTrustedUiUri(core.Source)) return;
        try
        {
            var source = core.Source;
            if (await core.ExecuteScriptAsync(FloatingVideoWindow.EnterScript) != "true")
            { MessageBox.Show(this, "Nenhum vídeo carregado foi encontrado nesta página. Inicie um vídeo e tente novamente."); return; }
            if (tab.IsDisposed || IsDisposed || core.Source != source) return;
            EndSplit(); ExitReader(tab); _memorySaver?.Resume(tab);
            var window = new FloatingVideoWindow(tab); _floatingVideos.Add(tab, window);
            window.FormClosed += (_, _) => _floatingVideos.Remove(tab);
            window.Show();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { if (!IsDisposed) UiDialogs.Error(this, ex); }
    }

    private async Task ShowSiteControlsAsync()
    {
        var tab = ActiveBrowserTab;
        if (tab?.Web.CoreWebView2 is not { } core || !BookmarkStore.IsWebUrl(core.Source) || TrustedBrowserBridge.IsTrustedUiUri(core.Source)) return;
        var uri = new Uri(core.Source); var origin = uri.GetLeftPart(UriPartial.Authority); var isPrivate = tab.IsPrivate;
        using var dialog = new SiteControlsDialog(origin, uri.Scheme == "https", _permissionPolicy, isPrivate,
            async (kind, blocked) =>
            {
                foreach (var window in Application.OpenForms.OfType<BrowserForm>().ToArray())
                {
                    window._permissionPolicy.SetBlocked(origin, kind, isPrivate, blocked);
                    if (!blocked) continue;
                    foreach (var page in window._tabView.TabPages.OfType<BrowserTab>().Where(t => t.IsPrivate == isPrivate).ToArray())
                    {
                        if (page.Web.CoreWebView2 is not { } engine || !Uri.TryCreate(engine.Source, UriKind.Absolute, out var current)
                            || current.GetLeftPart(UriPartial.Authority) != origin) continue;
                        await engine.Profile.SetPermissionStateAsync(Enum.Parse<CoreWebView2PermissionKind>(kind), origin, CoreWebView2PermissionState.Default);
                        if (!page.IsDisposed) engine.Reload();
                    }
                }
            }, async () =>
            {
                if (tab.IsDisposed) throw new InvalidOperationException("A aba foi fechada.");
                var cookies = await core.CookieManager.GetCookiesAsync(null);
                var matches = cookies.Where(c => uri.IdnHost.Equals(c.Domain.TrimStart('.'), StringComparison.OrdinalIgnoreCase)
                    || uri.IdnHost.EndsWith("." + c.Domain.TrimStart('.'), StringComparison.OrdinalIgnoreCase)).ToArray();
                foreach (var cookie in matches) core.CookieManager.DeleteCookie(cookie);
                return matches.Length;
            });
        dialog.ShowDialog(this);
        await Task.CompletedTask;
    }
}
