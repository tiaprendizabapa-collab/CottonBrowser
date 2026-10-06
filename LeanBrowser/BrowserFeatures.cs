using System.Text.Json;
using System.Net;
using Microsoft.Web.WebView2.WinForms;

namespace LeanBrowser;

public sealed partial class BrowserForm
{
    private readonly Panel _contentArea = new() { Dock = DockStyle.Fill };
    private readonly Panel _verticalPanel = new() { Dock = DockStyle.Left, Visible = false };
    private readonly Button _collapseTabs = new() { Dock = DockStyle.Top, Height = 38, Text = "Recolher abas", FlatStyle = FlatStyle.Flat, AccessibleName = "Recolher ou expandir abas verticais" };
    private readonly Label _profileCaption = new() { AutoEllipsis = true, Location = new Point(12, 15), Height = 24 };
    private SplitContainer? _split;
    private BrowserTab? _splitPrimary;
    private BrowserTab? _splitSecondary;
    private BrowserTab? _splitFocusedTab;
    private readonly Dictionary<BrowserTab, ReadingView> _readers = new();
    private readonly Dictionary<BrowserTab, string> _translationOriginals = new();
    private BrowserTab? ActiveBrowserTab => _splitPrimary is not null && _tabView.SelectedTab == _splitPrimary && _splitFocusedTab is { IsDisposed: false } focused ? focused : _tabs?.Active;

    private void InstallFeatureSurface()
    {
        _contentArea.Controls.Add(_tabView); _contentArea.Controls.Add(_verticalPanel); Controls.Add(_contentArea); Controls.SetChildIndex(_contentArea, 0);
        _verticalPanel.Controls.Add(_collapseTabs); _tabBar.Controls.Add(_profileCaption);
        _collapseTabs.Click += (_, _) => { BrowserPreferences.Current.VerticalTabsCollapsed = !BrowserPreferences.Current.VerticalTabsCollapsed; BrowserPreferences.Current.Save(); };
        _tabView.ControlRemoved += (_, e) => { if (e.Control == _splitPrimary || e.Control == _splitSecondary) EndSplit(); if (e.Control is BrowserTab tab) { _readers.Remove(tab); _translationOriginals.Remove(tab); } };
        ApplyVerticalLayout();
    }
    private void ApplyVerticalLayout()
    {
        var preferences = BrowserPreferences.Current;
        var vertical = preferences.VerticalTabs;
        _tabView.SetVerticalTabs(vertical, preferences.VerticalTabsCollapsed);
        _verticalPanel.Visible = vertical; _verticalPanel.Width = LogicalToDeviceUnits(preferences.VerticalTabsCollapsed ? 82 : 244);
        _collapseTabs.Text = preferences.VerticalTabsCollapsed ? "»" : "Recolher abas";
        _verticalPanel.BackColor = _contentArea.BackColor = _collapseTabs.BackColor = Theme.Chrome; _collapseTabs.ForeColor = Theme.Ink;
        _profileCaption.ForeColor = Theme.InkMuted; _profileCaption.Visible = vertical;
        _profileCaption.Text = "CottonBrowser · " + new BrowserProfileStore(BrowserPaths.Root).Load().First(p => p.Id == BrowserPaths.ProfileId).Name;
        var strip = _tabView.HeaderStrip;
        if (vertical) { if (strip.Parent != _verticalPanel) _verticalPanel.Controls.Add(strip); strip.Dock = DockStyle.Fill; _collapseTabs.BringToFront(); }
        else { strip.Dock = DockStyle.None; if (strip.Parent != _tabBar) _tabBar.Controls.Add(strip); }
        LayoutTabBar();
    }
    private void InstallFeatureMenus()
    {
        var reader = new BrowserMenuItem("Modo de leitura", "\uE736", "F9"); reader.Click += async (_, _) => await ToggleReaderAsync();
        var translate = new BrowserMenuItem("Traduzir página", "\uE8F2", "Google Tradutor");
        foreach (var (name, code) in new[] { ("Português", "pt"), ("Inglês", "en"), ("Espanhol", "es"), ("Francês", "fr") })
        { var item = new BrowserMenuItem(name); item.Click += (_, _) => TranslatePage(code); translate.DropDownItems.Add(item); }
        var original = new BrowserMenuItem("Mostrar página original"); original.Click += (_, _) => { if (ActiveBrowserTab is { } tab && _translationOriginals.Remove(tab, out var url)) NavigateCurrent(url); }; translate.DropDownItems.Add(original);
        translate.DropDownOpening += (_, _) => { original.Enabled = ActiveBrowserTab is { } tab && _translationOriginals.ContainsKey(tab); PrepareSubmenu(translate); };
        var capture = new BrowserMenuItem("Capturar página", "\uE722");
        foreach (var (name, mode) in new[] { ("Área visível", 0), ("Página inteira", 1), ("Selecionar área…", 2) })
        { var item = new BrowserMenuItem(name); item.Click += async (_, _) => await CapturePageAsync(mode); capture.DropDownItems.Add(item); }
        capture.DropDownOpening += (_, _) => PrepareSubmenu(capture);
        var split = new BrowserMenuItem("Tela dividida", "\uE89F"); split.Click += (_, _) => { if (_split is not null) EndSplit(); else ChooseSplit(); };
        var vertical = new BrowserMenuItem("Abas verticais", "\uE8FD"); vertical.Click += (_, _) => { BrowserPreferences.Current.VerticalTabs = !BrowserPreferences.Current.VerticalTabs; BrowserPreferences.Current.Save(); };
        var profiles = new BrowserMenuItem("Gerenciar perfis", "\uE77B"); profiles.Click += (_, _) => ShowProfiles();
        var home = new BrowserMenuItem("Personalizar nova guia", "\uE790"); home.Click += (_, _) => { OpenSettings(); _settingsTab?.ShowNewTab(); };
        var index = _overflowMenu.Items.Cast<ToolStripItem>().ToList().FindIndex(i => i.Text == "Configurações"); if (index < 0) index = _overflowMenu.Items.Count;
        foreach (var item in new ToolStripItem[] { reader, translate, capture, split, vertical, profiles, home, new ToolStripSeparator() }) _overflowMenu.Items.Insert(index++, item);
        _overflowMenu.Opening += (_, _) =>
        {
            var ready = _core is not null && BookmarkStore.IsWebUrl(_core.Source) && !TrustedBrowserBridge.IsTrustedUiUri(_core.Source);
            reader.Enabled = translate.Enabled = ready; capture.Enabled = _core is not null;
            reader.Text = ActiveBrowserTab is { } tab && _readers.ContainsKey(tab) ? "Sair do modo de leitura" : "Modo de leitura";
            split.Text = _split is null ? "Tela dividida" : "Encerrar tela dividida"; split.Enabled = _split is not null || ActiveBrowserTab is not null;
            vertical.Checked = BrowserPreferences.Current.VerticalTabs;
        };
    }
    private void ShowProfiles()
    {
        try { using var dialog = new ProfileManagerDialog(); dialog.ShowDialog(this); ApplyVerticalLayout(); RefreshActiveTab(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { UiDialogs.Error(this, ex); }
    }
    private async Task ToggleReaderAsync()
    {
        var tab = ActiveBrowserTab; if (tab?.Web.CoreWebView2 is not { } core) return;
        if (_readers.ContainsKey(tab)) { ExitReader(tab); return; }
        if (!BookmarkStore.IsWebUrl(core.Source) || TrustedBrowserBridge.IsTrustedUiUri(core.Source)) return;
        EndSplit();
        _tabView.SelectedTab = tab;
        try
        {
            var source = core.Source;
            using var document = JsonDocument.Parse(await core.ExecuteScriptAsync(ReadingView.ExtractionScript));
            if (tab.IsDisposed || core.Source != source || document.RootElement.ValueKind != JsonValueKind.Object) return;
            var content = document.RootElement.GetProperty("text").GetString() ?? "";
            if (content.Length < 80) { MessageBox.Show(this, "Esta página não possui texto suficiente para o modo de leitura."); return; }
            var reader = new ReadingView(document.RootElement.GetProperty("title").GetString() ?? "Leitura", content) { SourceUrl = source };
            reader.ExitRequested += () => ExitReader(tab); _readers.Add(tab, reader); tab.Controls.Add(reader); reader.BringToFront(); tab.Web.Visible = false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or JsonException) { UiDialogs.Error(this, ex); }
    }
    private async Task RefreshReaderAsync(BrowserTab tab, Func<bool> isCurrentNavigation)
    {
        if (!_readers.TryGetValue(tab, out var reader) || tab.Web.CoreWebView2 is not { } core) return;
        try
        {
            using var document = JsonDocument.Parse(await core.ExecuteScriptAsync(ReadingView.ExtractionScript));
            if (tab.IsDisposed || !isCurrentNavigation() || !_readers.TryGetValue(tab, out var current)
                || current != reader || document.RootElement.ValueKind != JsonValueKind.Object) return;
            reader.UpdateContent(document.RootElement.GetProperty("title").GetString() ?? "Leitura",
                document.RootElement.GetProperty("text").GetString() ?? "");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or JsonException)
        {
            // Keep the existing reading view if the document closes during extraction.
        }
    }
    private void ExitReader(BrowserTab tab)
    {
        if (_readers.Remove(tab, out var reader)) { tab.Controls.Remove(reader); reader.Dispose(); if (!tab.IsDisposed) { tab.Web.Visible = true; tab.Web.BringToFront(); _memorySaver?.Resume(tab); } }
    }
    private void TranslatePage(string language)
    {
        if (ActiveBrowserTab is not { } tab || tab.Web.CoreWebView2 is not { } core) return;
        var source = _translationOriginals.GetValueOrDefault(tab) ?? core.Source;
        if (!TryTranslationUrl(source, language, out var translated)) { MessageBox.Show(this, "A tradução está disponível para páginas públicas HTTP ou HTTPS. Endereços locais e páginas internas não são enviados ao tradutor.", "Traduzir página"); return; }
        ExitReader(tab); _translationOriginals[tab] = source; NavigateCurrent(translated);
    }
    internal static bool TryTranslationUrl(string source, string language, out string translated)
    {
        translated = "";
        if (language is not ("pt" or "en" or "es" or "fr") || !BookmarkStore.IsWebUrl(source) || !Uri.TryCreate(source, UriKind.Absolute, out var uri)) return false;
        var host = uri.IdnHost;
        if (uri.IsLoopback || host == TrustedBrowserBridge.HostName || !host.Contains('.') || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".test", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out _)) return false;
        translated = "https://translate.google.com/translate?sl=auto&tl=" + language + "&u=" + Uri.EscapeDataString(source); return true;
    }
    private void TrackTranslationNavigation(BrowserTab tab, string url)
    {
        if (!_translationOriginals.ContainsKey(tab)) return;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
            (uri.IdnHost == "translate.google.com" || uri.IdnHost.EndsWith(".translate.goog", StringComparison.OrdinalIgnoreCase))) return;
        _translationOriginals.Remove(tab);
    }
    private async Task CapturePageAsync(int mode)
    {
        if (_core is not { } core) return;
        try { var png = mode == 1 ? await PageCapture.FullAsync(core) : await PageCapture.VisibleAsync(core); if (IsDisposed) return; using var preview = new CapturePreviewDialog(png, mode == 2); preview.ShowDialog(this); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or JsonException) { UiDialogs.Error(this, ex); }
    }
    private void ChooseSplit()
    {
        if (ActiveBrowserTab is not { } primary) return;
        var others = _tabView.TabPages.OfType<BrowserTab>().Where(t => t != primary && t.IsPrivate == primary.IsPrivate && t.Web.CoreWebView2 is not null).ToArray();
        if (others.Length == 0) { MessageBox.Show(this, "Abra uma segunda aba para usar a tela dividida."); return; }
        using var dialog = new Form { Text = "Escolha a segunda página", ClientSize = new Size(440, 300), StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Chrome };
        var list = new ListBox { Dock = DockStyle.Fill, DisplayMember = "Text", BackColor = Theme.Surface, ForeColor = Theme.Ink }; list.Items.AddRange(others); list.SelectedIndex = 0;
        var open = new Button { Dock = DockStyle.Bottom, Height = 40, Text = "Dividir tela", DialogResult = DialogResult.OK }; dialog.Controls.Add(list); dialog.Controls.Add(open); dialog.AcceptButton = open;
        if (dialog.ShowDialog(this) == DialogResult.OK && list.SelectedItem is BrowserTab secondary) StartSplit(primary, secondary);
    }
    internal void StartSplit(BrowserTab primary, BrowserTab secondary)
    {
        if (primary == secondary || primary.IsPrivate != secondary.IsPrivate || primary.IsDisposed || secondary.IsDisposed) return;
        EndSplit(); ExitReader(primary); ExitReader(secondary); _tabView.SelectedTab = primary;
        var split = new SplitContainer { Size = new Size(Math.Max(220, primary.ClientSize.Width), Math.Max(150, primary.ClientSize.Height)), Dock = DockStyle.Fill, BackColor = Theme.Divider, Panel1MinSize = 80, Panel2MinSize = 80 };
        primary.Controls.Add(split); split.BringToFront(); split.Panel1.Controls.Add(primary.Web); split.Panel2.Controls.Add(secondary.Web);
        _split = split; _splitPrimary = primary; _splitSecondary = secondary; _splitFocusedTab = primary;
        if (split.Width > 210) split.SplitterDistance = split.Width / 2;
        primary.Web.Visible = secondary.Web.Visible = true; _memorySaver?.Resume(primary); _memorySaver?.Resume(secondary);
    }
    internal void EndSplit()
    {
        if (_split is null) return;
        var split = _split; var primary = _splitPrimary; var secondary = _splitSecondary;
        _split = null; _splitPrimary = _splitSecondary = _splitFocusedTab = null;
        if (primary is { IsDisposed: false }) { primary.Controls.Add(primary.Web); primary.Web.Visible = true; }
        if (secondary is { IsDisposed: false }) { secondary.Controls.Add(secondary.Web); secondary.Web.Visible = true; }
        split.Parent?.Controls.Remove(split); split.Dispose();
        RefreshActiveTab();
    }
}
