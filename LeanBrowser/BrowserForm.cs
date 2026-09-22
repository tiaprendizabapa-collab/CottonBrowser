using System.Drawing.Drawing2D;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LeanBrowser;

public sealed class BrowserForm : Form
{
    private const string HomePage = "https://www.google.com";

    private readonly Panel      _toolbar  = new();
    private readonly ToolButton _back     = new("\uE72B", "Voltar");
    private readonly ToolButton _forward  = new("\uE72A", "Avancar");
    private readonly ToolButton _reload   = new("\uE72C", "Recarregar");
    private readonly Omnibox    _omnibox  = new();
    private readonly BrowserTabControl _tabView = new() { Dock = DockStyle.Fill };
    private readonly Button _overflowButton = new()
    {
        Text = "",
        AccessibleName = "Mais opções",
        AccessibleRole = AccessibleRole.PushButton,
        Size = new Size(36, 36),
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Chrome,
        ForeColor = Theme.Ink,
        Cursor = Cursors.Hand,
        TabStop = false,
        UseVisualStyleBackColor = false
    };
    private readonly ContextMenuStrip _overflowMenu = new()
    {
        ShowImageMargin = false,
        ShowCheckMargin = false,
        AutoSize = true,
        Padding = new Padding(4)
    };
    private readonly string _themePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LeanBrowser", "theme.txt");
    private readonly AccessControl _access = new();
    private readonly NavigationTelemetry _telemetry;
    private SettingsTab? _settingsTab;
    private ProfileTab? _profileTab;
    private readonly AdProtection _adProtection = new();
    private readonly ISecretStore _secrets = new DpapiSecretStore();
    private readonly WebRiskReputationService _urlReputation;
    private readonly PermissionPolicy _permissionPolicy = new();
    private bool _permissionCenterOpening;
    private bool _changingProtection;
    private bool _protectionFailureShown;
    private readonly BookmarkStore _bookmarks = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeanBrowser", "bookmarks.json"));
    private BrowserTabManager? _tabs;
    private CoreWebView2Environment? _browserEnvironment;
    private FoxyJumpscareForm? _foxyJumpscare;
    private int _brandClickCount;
    private long _lastBrandClick;
    private readonly Dictionary<CoreWebView2, CoreWebView2ContextMenuItem> _newTabContextItems = new();
    private readonly Dictionary<CoreWebView2, string> _contextMenuLinks = new();
    private WebView2? _web => _tabs?.Active?.Web;
    private AdBlocker? _blocker => _tabs?.Active?.Blocker;

    private CoreWebView2? _core => _web?.CoreWebView2;
    private bool _loading;
    private bool _omniboxDirty;   // usuario esta editando: nao sobrescrever
    private bool _selectAllOnClick;

    public BrowserForm()
    {
        SuspendLayout();

        _telemetry = new NavigationTelemetry(_access.UserName);
        _urlReputation = new WebRiskReputationService(_secrets);

        try { Theme.SetDark(File.Exists(_themePath) && File.ReadAllText(_themePath).Trim() == "dark"); } catch { }

        Text = "CottonBrowser";
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        BackColor = Theme.Chrome;
        ClientSize = new Size(1200, 780);
        MinimumSize = new Size(560, 360);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font(Theme.UiFont, 9f);
        DoubleBuffered = true;
        KeyPreview = true;

        BuildToolbar();
        BuildMenus();
        _permissionPolicy.PromptCreated += OnPermissionPromptCreated;
        _tabView.BrandClicked += OnBrandClicked;

        Controls.Add(_tabView);
        Controls.Add(_toolbar);
        Controls.Add(_tabView.HeaderStrip);
        Controls.Add(_overflowButton);
        Resize += (_, _) => LayoutOverflowButton();
        _tabView.HeaderStrip.Resize += (_, _) => LayoutOverflowButton();

        // Os controles são criados antes de ler o tema persistido; sincroniza
        // a primeira pintura para que o modo escuro já nasça consistente.
        ApplyThemeRecursive(this);
        _tabView.ApplyTheme();
        LayoutOverflowButton();

        ResumeLayout(false);
    }

    // ---------------------------------------------------------------- UI ---

    private void BuildToolbar()
    {
        _toolbar.Dock = DockStyle.Top;
        _toolbar.Height = 58;
        _toolbar.BackColor = Theme.Chrome;
        _toolbar.Padding = new Padding(4, 0, 4, 0);

        // Linha divisoria de 1px desenhada a mao (mais barato que um Control).
        _toolbar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Divider);
            e.Graphics.DrawLine(pen, 0, _toolbar.Height - 1,
                                     _toolbar.Width, _toolbar.Height - 1);
        };

        _back.Click    += (_, _) => _core?.GoBack();
        _forward.Click += (_, _) => _core?.GoForward();
        _reload.Click  += (_, _) =>
        {
            if (_loading) _core?.Stop();
            else          _core?.Reload();
        };
        _omnibox.Favorite.Click += (_, _) => AddBookmark();

        _back.Enabled = false;
        _forward.Enabled = false;

        _omnibox.Input.KeyDown += OnOmniboxKeyDown;
        _omnibox.Input.TextChanged += (_, _) => _omniboxDirty = _omnibox.Input.Focused;
        _omnibox.Input.GotFocus += (_, _) => _selectAllOnClick = true;
        _omnibox.Input.MouseUp += (_, _) =>
        {
            if (!_selectAllOnClick) return;
            _selectAllOnClick = false;
            if (_omnibox.Input.SelectionLength == 0)
                _omnibox.Input.SelectAll();
        };
        _omnibox.Input.LostFocus += (_, _) =>
        {
            _omniboxDirty = false;
            if (_core is not null) ShowUrl(_core.Source);
        };

        _toolbar.Controls.Add(_back);
        _toolbar.Controls.Add(_forward);
        _toolbar.Controls.Add(_reload);
        _toolbar.Controls.Add(_omnibox);

        _toolbar.Resize += (_, _) => LayoutToolbar();
        LayoutToolbar();
    }

    /// <summary>
    /// Posicionamento manual. Um TableLayoutPanel faria o mesmo, mas roda um
    /// motor de layout completo a cada resize; aqui sao 4 atribuicoes.
    /// </summary>
    private void LayoutToolbar()
    {
        const int gap = 6;
        const int margin = 10;

        var y = (_toolbar.Height - 1 - _back.Height) / 2;
        var x = margin;

        _back.Location = new Point(x, y);
        x += _back.Width + gap;

        _forward.Location = new Point(x, y);
        x += _forward.Width + gap;

        _reload.Location = new Point(x, y);
        x += _reload.Width + gap + 6;

        _omnibox.Location = new Point(x, (_toolbar.Height - 1 - _omnibox.Height) / 2);
        _omnibox.Width = Math.Max(120, _toolbar.Width - x - margin);
    }

    private void BuildMenus()
    {
        _tabView.NewTabRequested += async () => await OpenTabAsync(HomePage);
        _tabView.CloseRequested += tab =>
        {
            _tabs?.Close(tab);
            if (_tabView.BrowserTabCount == 0) Close();
        };
        _tabView.AuxiliaryCloseRequested += tab =>
        {
            if (tab == _settingsTab)
            {
                _tabView.TabPages.Remove(tab);
                tab.Dispose();
                _settingsTab = null;
            }
            else if (tab == _profileTab)
            {
                _tabView.TabPages.Remove(tab);
                tab.Dispose();
                _profileTab = null;
            }
        };
        _overflowButton.Click += (_, _) => ShowOverflowMenu(_overflowButton);
        _overflowButton.Paint += (_, e) => PaintOverflowButton(e.Graphics);
        _overflowButton.MouseEnter += (_, _) => _overflowButton.Invalidate();
        _overflowButton.MouseLeave += (_, _) => _overflowButton.Invalidate();
        var browserCenter = new ToolStripMenuItem("Central do navegador");
        browserCenter.Click += async (_, _) => await OpenTabAsync(TrustedBrowserBridge.UiUrl);
        var configuration = new ToolStripMenuItem("Configurações");
        configuration.Click += (_, _) => OpenSettings();
        var profile = new ToolStripMenuItem("Perfil");
        profile.Click += (_, _) => OpenProfile();
        var protection = new ToolStripMenuItem("Proteção");
        var protectionEnabled = new ToolStripMenuItem("Bloquear anúncios (Ctrl+Shift+A)");
        protectionEnabled.Click += async (_, _) => await ToggleProtectionAsync();
        var allowPopups = new ToolStripMenuItem("Permitir novas janelas neste site");
        allowPopups.Click += (_, _) =>
        {
            if (_tabs?.Active is { } tab)
                tab.Popups.AllowCurrentOrigin(!tab.Popups.AllowedForCurrentOrigin);
        };
        var protectionStatus = new ToolStripMenuItem { Enabled = false };
        var settings = new ToolStripMenuItem("Configurar filtros de anúncios");
        settings.Click += async (_, _) =>
        {
            if (!_access.IsAdmin) return;
            if (_adProtection.DashboardUrl is { } url) await OpenTabAsync(url);
        };
        protection.DropDownOpening += (_, _) =>
        {
            protectionEnabled.Checked = _adProtection.Enabled;
            protectionEnabled.Enabled = !_changingProtection;
            allowPopups.Checked = _tabs?.Active?.Popups.AllowedForCurrentOrigin == true;
            allowPopups.Visible = _access.IsAdmin;
            allowPopups.Enabled = _access.IsAdmin && _adProtection.Enabled && BookmarkStore.IsWebUrl(_core?.Source ?? "");
            settings.Visible = _access.IsAdmin;
            settings.Enabled = _access.IsAdmin && _adProtection.Available;
            protectionStatus.Text = !_adProtection.Enabled ? "Proteção pausada" :
                _adProtection.Available ? "uBlock Origin Lite ativo" : "Somente bloqueio básico ativo";
        };
        protection.DropDownItems.AddRange(new ToolStripItem[] { protectionEnabled, allowPopups, settings, protectionStatus });
        _overflowMenu.Items.AddRange(new ToolStripItem[] { browserCenter, configuration, profile, protection });
        _overflowMenu.Font = new Font(Theme.UiFont, 9.5f);
        _overflowMenu.BackColor = Theme.Surface;
        _overflowMenu.ForeColor = Theme.Ink;
        _overflowMenu.Renderer = new OverflowMenuRenderer();
        _tabView.SelectedIndexChanged += (_, _) => RefreshActiveTab();
    }

    private void LayoutOverflowButton()
    {
        if (IsDisposed) return;
        var header = _tabView.HeaderStrip;
        var top = header.Top + Math.Max(0, (header.Height - _overflowButton.Height) / 2);
        var left = Math.Max(4, ClientSize.Width - _overflowButton.Width - 6);
        _overflowButton.Location = new Point(left, top);
        _overflowButton.BringToFront();
    }

    private void PaintOverflowButton(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Chrome);
        var point = _overflowButton.PointToClient(Cursor.Position);
        if (_overflowButton.ClientRectangle.Contains(point))
        {
            using var hotBrush = new SolidBrush(Theme.SurfaceHot);
            g.FillRectangle(hotBrush, new Rectangle(0, 0, _overflowButton.Width - 1, _overflowButton.Height - 1));
        }

        using var dotBrush = new SolidBrush(Theme.InkMuted);
        var centerX = _overflowButton.Width / 2f;
        var centerY = _overflowButton.Height / 2f;
        const float radius = 2f;
        const float spacing = 6f;
        for (var i = -1; i <= 1; i++)
        {
            var y = centerY + i * spacing;
            g.FillEllipse(dotBrush, centerX - radius, y - radius, radius * 2, radius * 2);
        }
    }

    private void ShowOverflowMenu(Control anchor)
    {
        _overflowMenu.PerformLayout();
        var popupSize = _overflowMenu.GetPreferredSize(Size.Empty);
        var anchorPoint = anchor.PointToScreen(new Point(anchor.Width, anchor.Height + 1));
        var clientOrigin = PointToScreen(Point.Empty);
        var clientBounds = new Rectangle(clientOrigin, ClientSize);

        // Mantém o painel dentro da janela; perto da borda direita ele abre
        // para a esquerda, como nos menus compactos dos navegadores atuais.
        var x = Math.Min(anchorPoint.X - popupSize.Width, clientBounds.Right - popupSize.Width - 8);
        x = Math.Max(clientBounds.Left + 8, x);

        var y = anchorPoint.Y;
        if (y + popupSize.Height > clientBounds.Bottom - 8)
            y = anchorPoint.Y - popupSize.Height - 1;
        y = Math.Max(clientBounds.Top + 8, y);

        _overflowMenu.Show(new Point(x, y));
    }

    private void WithBookmarkErrors(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { MessageBox.Show(this, "Não foi possível acessar os favoritos. O arquivo existente foi preservado.\n" + ex.Message); }
    }

    private void AddBookmark() => WithBookmarkErrors(() =>
    {
        if (_core is not null && BookmarkStore.IsWebUrl(_core.Source))
        {
            _bookmarks.Add(_core.Source, _core.DocumentTitle);
            _omnibox.SetFavorite(true);
        }
    });

    private void OpenSettings()
    {
        if (_settingsTab is null)
        {
            _settingsTab = new SettingsTab(ApplyTheme, ClearSavedPasswords, () =>
            {
                try { return _bookmarks.Load(); } catch { return Array.Empty<Bookmark>(); }
            }, AddBookmark, url => WithBookmarkErrors(() => _bookmarks.Remove(url)), _access.IsAdmin);
            _settingsTab.FavoriteSelected += async url => await OpenTabAsync(url);
            _tabView.TabPages.Add(_settingsTab);
        }
        _settingsTab.ReloadBookmarks();
        _tabView.SelectedTab = _settingsTab;
    }

    private void OpenProfile()
    {
        if (_profileTab is null)
        {
            _profileTab = new ProfileTab(_access);
            _tabView.TabPages.Add(_profileTab);
        }
        _tabView.SelectedTab = _profileTab;
    }

    private void ApplyTheme(bool dark)
    {
        Theme.SetDark(dark);
        try { Directory.CreateDirectory(Path.GetDirectoryName(_themePath)!); File.WriteAllText(_themePath, dark ? "dark" : "light"); } catch { }
        BackColor = Theme.Chrome; _toolbar.BackColor = Theme.Chrome;
        _overflowButton.BackColor = Theme.Chrome; _overflowButton.ForeColor = Theme.Ink;
        _overflowMenu.BackColor = Theme.Surface; _overflowMenu.ForeColor = Theme.Ink;
        ApplyThemeRecursive(this);
        _tabView.ApplyTheme();
        _settingsTab?.ApplyTheme();
        _profileTab?.ApplyTheme();
        foreach (var tab in _tabView.TabPages.OfType<BrowserTab>())
        {
            try { if (tab.Web.CoreWebView2 is { } core) core.Profile.PreferredColorScheme = Theme.IsDark
                ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light; }
            catch { }
        }
        _tabView.Invalidate(true); Invalidate(true);
    }

    private static void ApplyThemeRecursive(Control control)
    {
        if (control is not SettingsTab)
        {
            control.BackColor = control is TextBox or ListBox ? Theme.Surface : Theme.Chrome;
            control.ForeColor = Theme.Ink;
        }
        foreach (Control child in control.Controls) ApplyThemeRecursive(child);
    }

    private sealed class OverflowMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Theme.Surface);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Theme.Divider);
            var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            e.Graphics.DrawRectangle(pen, bounds);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var bounds = new Rectangle(Point.Empty, e.Item.Size);
            using var brush = new SolidBrush(e.Item.Selected ? Theme.SurfaceHot : Theme.Surface);
            e.Graphics.FillRectangle(brush, bounds);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Theme.Ink;
            base.OnRenderItemText(e);
        }
    }

    private async void ClearSavedPasswords()
    {
        if (!_access.IsAdmin) return;
        var core = _core;
        if (core is null || MessageBox.Show(this, "Apagar todas as senhas salvas neste navegador?", "Senhas", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { await PasswordManager.ClearSavedPasswordsAsync(core); MessageBox.Show(this, "Senhas salvas apagadas."); }
        catch { MessageBox.Show(this, "Não foi possível apagar as senhas."); }
    }

    private async Task OpenTabAsync(string url)
    {
        if (_tabs is null || IsDisposed) return;
        try { await _tabs.CreateAsync(url); }
        catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, "Não foi possível abrir a aba.\n" + ex.Message); }
    }

    private async Task<bool> OpenTabFromBridgeAsync(string url)
    {
        if (_tabs is null || IsDisposed) return false;
        try { return await _tabs.CreateAsync(url) is not null; }
        catch { return false; }
    }

    private void OnPermissionPromptCreated(PermissionPrompt prompt)
    {
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke(new Action(() => _ = ShowPermissionPromptAsync(prompt)));
    }

    private async Task ShowPermissionPromptAsync(PermissionPrompt prompt)
    {
        foreach (var tab in _tabView.TabPages.OfType<BrowserTab>())
        {
            if (TrustedBrowserBridge.PublishPermissionPrompt(tab.Web.CoreWebView2, prompt)) return;
        }

        if (_permissionCenterOpening) return;
        _permissionCenterOpening = true;
        try { await OpenTabAsync(TrustedBrowserBridge.UiUrl); }
        finally { _permissionCenterOpening = false; }
    }

    private bool SaveBookmarkFromBridge(string url, string title)
    {
        try
        {
            _bookmarks.Add(url, title);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            MessageBox.Show(this, "Não foi possível salvar o favorito.", "Favoritos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private void OnBrandClicked()
    {
        var now = Environment.TickCount64;
        if (now - _lastBrandClick > 1200) _brandClickCount = 0;
        _lastBrandClick = now;
        if (++_brandClickCount < 5) return;
        _brandClickCount = 0;

        if (_browserEnvironment is null || IsDisposed) return;
        _foxyJumpscare?.Close();
        _foxyJumpscare = new FoxyJumpscareForm(_browserEnvironment);
        _foxyJumpscare.FormClosed += (_, _) =>
        {
            if (_foxyJumpscare is { IsDisposed: true }) _foxyJumpscare = null;
        };
        _foxyJumpscare.Show(this);
    }

    private void CloseTab()
    {
        _tabs?.CloseActive();
        if (_tabView.BrowserTabCount == 0) Close();
    }

    private void RefreshActiveTab(bool resetEditing = true)
    {
        if (resetEditing) _omniboxDirty = false;
        SetLoading(_tabs?.Active?.Loading == true);
        _back.Enabled = _core?.CanGoBack == true;
        _forward.Enabled = _core?.CanGoForward == true;
        var url = _core?.Source ?? "";
        ShowUrl(url);
        _omnibox.SetFavorite(IsFavorite(url));
        Text = BuildTitle(_core?.DocumentTitle ?? "");
        _telemetry.RecordActiveTab(url, _core?.DocumentTitle);
        _omnibox.SetIndicator(_core?.Source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true,
            _blocker?.BlockedOnCurrentPage ?? 0);
    }

    private bool IsFavorite(string url)
    {
        if (!BookmarkStore.IsWebUrl(url)) return false;
        try { return _bookmarks.Load().Any(bookmark => bookmark.Url == url); }
        catch { return false; }
    }
    // ----------------------------------------------------- Inicializacao ---

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        Native.EnableRoundedCorners(Handle);
        AdBlocker.ExportDefaultList();

        try
        {
            await InitializeWebViewAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Nao foi possivel iniciar o motor WebView2.\n\n" + ex.Message +
                "\n\nInstale o 'Microsoft Edge WebView2 Runtime' (Evergreen) e tente de novo.",
                "CottonBrowser", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _foxyJumpscare?.Close();
        _telemetry.Dispose();
        _urlReputation.Dispose();
        _permissionPolicy.Dispose();
        base.OnFormClosed(e);
    }

    private async Task InitializeWebViewAsync()
    {
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LeanBrowser", "WebView2");

        Directory.CreateDirectory(userData);

        var options = WebContentIsolation.CreateEnvironmentOptions(BrowserArguments());

        var env = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,        // usa o runtime Evergreen do sistema
            userDataFolder: userData,
            options: options);

        if (IsDisposed) return;
        _browserEnvironment = env;
        _tabs = new BrowserTabManager(_tabView, env);
        _tabs.InitializeTabAsync = async tab =>
        {
            tab.NetworkProtection = new NetworkProtection(
                tab.Web,
                _urlReputation,
                ConfirmInsecureNavigationAsync);
            tab.PermissionSubscription = _permissionPolicy.Attach(tab.Web);
            await _permissionPolicy.ResetPersistedPermissionsAsync(tab.Web.CoreWebView2.Profile);
            TrustedBrowserBridge.Attach(tab.Web, new TrustedBrowserBridgeHandlers(
                OpenTabFromBridgeAsync,
                SaveBookmarkFromBridge,
                _permissionPolicy.GetPending,
                _permissionPolicy.Resolve));
            await _adProtection.InitializeAsync(tab.Web.CoreWebView2, userData);
            if (tab.IsDisposed || IsDisposed) return;
            await tab.DocumentProtection.SetEnabledAsync(tab.Web.CoreWebView2, _adProtection.Enabled);
            if (tab.IsDisposed || IsDisposed) return;
            tab.Blocker.Enabled = _adProtection.Enabled;
            ConfigureTab(tab);
            if (_adProtection.Failure is { } failure && !_protectionFailureShown)
            {
                _protectionFailureShown = true;
                MessageBox.Show(this, failure, "Proteção de anúncios", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        await _tabs.CreateAsync(HomePage);
        _omnibox.Input.Focus();
    }

    private Task<bool> ConfirmInsecureNavigationAsync(Uri target)
    {
        if (IsDisposed || Disposing) return Task.FromResult(false);

        if (InvokeRequired)
        {
            var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                BeginInvoke(new Action(() =>
                {
                    try { decision.TrySetResult(ShowInsecureNavigationWarning(target)); }
                    catch { decision.TrySetResult(false); }
                }));
            }
            catch { decision.TrySetResult(false); }
            return decision.Task;
        }

        return Task.FromResult(ShowInsecureNavigationWarning(target));
    }

    private bool ShowInsecureNavigationWarning(Uri target) =>
        MessageBox.Show(
            this,
            $"{target.GetLeftPart(UriPartial.Authority)} usa HTTP sem criptografia. " +
            "Dados e credenciais podem ser interceptados ou alterados.\n\n" +
            "Deseja aceitar o risco e continuar somente nesta sessao?",
            "Conexao nao segura",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private void ConfigureTab(BrowserTab tab)
    {
        var core = tab.Web.CoreWebView2;
        ApplySettings(core);
        PasswordManager.Configure(core);
        tab.Blocker.Attach(core);
        core.NavigationStarting += (_, e) =>
        {
            tab.Popups.OnNavigation(e.Uri);
            if (!e.IsRedirected) tab.Blocker.ResetPageCounter();
            tab.Loading = true;
            if (_tabs?.Active == tab) SetLoading(true);
        };
        core.NavigationCompleted += (_, e) =>
        {
            tab.Loading = false;
            if (e.IsSuccess)
                _telemetry.RecordNavigation(core.Source, core.DocumentTitle, _tabs?.Active == tab);
            if (_tabs?.Active == tab) { SetLoading(false); RefreshActiveTab(resetEditing: false); }
        };
        core.SourceChanged += (_, _) =>
        {
            tab.Popups.OnNavigation(core.Source);
            if (_tabs?.Active == tab) ShowUrl(core.Source);
        };
        core.HistoryChanged += (_, _) => { if (_tabs?.Active == tab) UpdateHistoryButtons(); };
        core.DocumentTitleChanged += (_, _) =>
        {
            var title = core.DocumentTitle;
            tab.Text = string.IsNullOrWhiteSpace(title) ? "Nova aba" : title[..Math.Min(32, title.Length)];
            if (_tabs?.Active == tab) Text = BuildTitle(title);
        };
        tab.Web.KeyDown += OnWebKeyDown;
        core.ContextMenuRequested += (_, e) => ReplaceOpenInNewWindowCommand(core, e);
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if ((_adProtection.Enabled ? tab.Popups.ShouldOpen(e.Uri, e.IsUserInitiated)
                : e.IsUserInitiated && BookmarkStore.IsWebUrl(e.Uri)))
            {
                var url = e.Uri;
                BeginInvoke(new Action(async () => await OpenTabAsync(url)));
            }
        };
        core.WindowCloseRequested += (_, _) =>
        {
            _newTabContextItems.Remove(core);
            _contextMenuLinks.Remove(core);
            _tabs?.Close(tab);
            if (_tabView.BrowserTabCount == 0) Close();
        };
        RefreshActiveTab();
    }

    private void ReplaceOpenInNewWindowCommand(CoreWebView2 core, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var target = e.ContextMenuTarget;
        var linkUrl = target.HasLinkUri ? target.LinkUri : null;
        if (!BookmarkStore.IsWebUrl(linkUrl ?? "")) return;

        if (!_newTabContextItems.TryGetValue(core, out var newTabItem))
        {
            newTabItem = core.Environment.CreateContextMenuItem(
                "Abrir link em uma nova aba", null, CoreWebView2ContextMenuItemKind.Command);
            newTabItem.CustomItemSelected += (_, _) =>
            {
                if (!_contextMenuLinks.TryGetValue(core, out var url)) return;
                BeginInvoke(new Action(async () => await OpenTabAsync(url)));
            };
            _newTabContextItems[core] = newTabItem;
        }

        _contextMenuLinks[core] = linkUrl!;
        ReplaceContextMenuItem(e.MenuItems, newTabItem);
    }

    private static bool ReplaceContextMenuItem(
        IList<CoreWebView2ContextMenuItem> items,
        CoreWebView2ContextMenuItem replacement)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (string.Equals(item.Name, "openLinkInNewWindow", StringComparison.Ordinal))
            {
                items[index] = replacement;
                return true;
            }

            if (item.Children is { Count: > 0 } children && ReplaceContextMenuItem(children, replacement))
                return true;
        }

        return false;
    }
    /// <summary>
    /// Flags passadas ao processo do motor. Cada uma remove um subsistema do
    /// Chromium que um navegador minimalista nao usa - e todo subsistema
    /// removido e memoria que nunca e alocada e timers que nunca disparam.
    /// </summary>
    private static string BrowserArguments() => string.Join(' ', new[]
    {
        // --- Subsistemas de rede em background ------------------------------
        "--disable-background-networking", // updates, variacoes, dominios de captive portal
        "--disable-component-update",
        "--disable-domain-reliability",
        "--disable-sync",
        "--no-pings",                      // nao dispara <a ping>
        "--no-default-browser-check",
        "--no-first-run",

        // --- Telemetria e servicos opcionais --------------------------------
        "--disable-breakpad",              // sem coletor de crash dump
        "--disable-crash-reporter",
        "--metrics-recording-only",


        // --- Features desligadas (cada uma tem custo de memoria residente) ---
        "--disable-features=" + string.Join(',', new[]
        {
            "msWebOOUI",                   // UI out-of-process do WebView2 = -1 processo
            "msPdfOOUI",                   // idem, para o visualizador de PDF
            "OptimizationHints",           // baixa modelos de hints periodicamente
            "MediaRouter",                 // descoberta de Cast na rede local
            "Translate",
            "AutofillServerCommunication",
            "InterestFeedContentSuggestions",
            "CalculateNativeWinOcclusion", // ver nota abaixo
        }),

        // --- Heap do V8 -----------------------------------------------------
        // Teto de 256 MB para a old generation. Sites bem comportados nunca
        // chegam perto; sites com vazamento sofrem GC antes de comer 1 GB.
        "--js-flags=--max-old-space-size=256",

        // --- Renderizacao ---------------------------------------------------
        // NAO desabilitamos a GPU de proposito: sem aceleracao, a composicao
        // volta para a CPU e o consumo SOBE. "Leve" nao e sinonimo de "software".
    });

    private static void ApplySettings(CoreWebView2 core)
    {
        core.Profile.PreferredColorScheme = Theme.IsDark
            ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
        var s = core.Settings;

        s.AreDevToolsEnabled          = false; // sem frontend de devtools carregado
        s.IsStatusBarEnabled          = false; // remove um popup/HWND
        s.IsGeneralAutofillEnabled    = false;
        // PasswordManager.Configure aplica a política de senhas do perfil.
        s.IsSwipeNavigationEnabled    = false;
        s.IsPinchZoomEnabled          = false;
        s.AreDefaultContextMenusEnabled = true;

        // Deixamos os aceleradores do motor desligados e tratamos os atalhos
        // no host, para nao haver dois donos do mesmo comando.
        s.AreBrowserAcceleratorKeysEnabled = false;

        // Nota de seguranca: IsReputationCheckingRequired (SmartScreen)
        // permanece LIGADO. Desligar economizaria uma ida a rede por
        // navegacao, mas nao vale o risco em um navegador de uso geral.

        try
        {
            // Bloqueio de rastreadores nativo do motor, gratuito em CPU:
            // roda dentro do processo do browser, junto com a nossa blocklist.
            core.Profile.PreferredTrackingPreventionLevel =
                CoreWebView2TrackingPreventionLevel.Strict;
        }
        catch
        {
            // Runtime antigo: segue sem esse reforco.
        }
    }

    // ------------------------------------------------------- Navegacao -----

    private void OnOmniboxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
            return;

        e.SuppressKeyPress = true; // mata o "ding" do Windows
        e.Handled = true;

        var target = UrlHelper.Normalize(_omnibox.Input.Text);

        _omniboxDirty = false;
        _core?.Navigate(target);
        _web?.Focus();
    }

    private void SetLoading(bool loading)
    {
        if (_loading == loading)
            return;

        _loading = loading;
        _reload.Glyph = loading ? "\uE711" : "\uE72C"; // X : recarregar
        _reload.AccessibleName = loading ? "Parar" : "Recarregar";
        _reload.Invalidate();
    }

    private void UpdateHistoryButtons()
    {
        if (_core is null) return;

        if (_back.Enabled != _core.CanGoBack)
            _back.Enabled = _core.CanGoBack;

        if (_forward.Enabled != _core.CanGoForward)
            _forward.Enabled = _core.CanGoForward;
    }

    private void ShowUrl(string url)
    {
        if (_omniboxDirty) return; // usuario digitando: nao atropelar

        var display = UrlHelper.ForDisplay(url);
        if (_omnibox.Input.Text != display)
            _omnibox.Input.Text = display;
    }

    private string BuildTitle(string docTitle) =>
        string.IsNullOrWhiteSpace(docTitle) ? "CottonBrowser" : docTitle + " - CottonBrowser";

    // --------------------------------------------------------- Atalhos -----

    private void OnWebKeyDown(object? sender, KeyEventArgs e)
    {
        var key = e.KeyCode;
        var ctrl = e.Control;
        var shift = e.Shift;
        var alt = e.Alt;
        var isShortcut = (ctrl && (key == Keys.L || key == Keys.R || key == Keys.T || key == Keys.W || key == Keys.Tab || key == Keys.D))
            || (ctrl && shift && key == Keys.A)
            || (alt && (key == Keys.D || key == Keys.Left || key == Keys.Right || key == Keys.Home))
            || key == Keys.F5 || (key == Keys.Escape && _loading);

        if (!isShortcut) return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        // Evita chamar o motor enquanto ele aguarda o retorno do evento.
        BeginInvoke(new Action(() => HandleShortcut(key, ctrl, shift, alt)));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key   = keyData & Keys.KeyCode;
        var ctrl  = (keyData & Keys.Control) == Keys.Control;
        var shift = (keyData & Keys.Shift)   == Keys.Shift;
        var alt   = (keyData & Keys.Alt)     == Keys.Alt;

        return HandleShortcut(key, ctrl, shift, alt)
            || base.ProcessCmdKey(ref msg, keyData);
    }

    private bool HandleShortcut(Keys key, bool ctrl, bool shift, bool alt)
    {
        switch (key)
        {
            case Keys.T when ctrl:
                _ = OpenTabAsync(HomePage);
                return true;
            case Keys.W when ctrl:
                CloseTab();
                return true;
            case Keys.Tab when ctrl:
                _tabs?.SelectNext(shift ? -1 : 1);
                return true;
            case Keys.D when ctrl:
                AddBookmark();
                return true;
            case Keys.L when ctrl:
            case Keys.D when alt:
                _omnibox.Input.Focus();
                _omnibox.Input.SelectAll();
                return true;

            case Keys.F5:
            case Keys.R when ctrl:
                _core?.Reload();
                return true;

            case Keys.Left when alt:
                if (_core?.CanGoBack == true) _core.GoBack();
                return true;

            case Keys.Right when alt:
                if (_core?.CanGoForward == true) _core.GoForward();
                return true;

            case Keys.Escape:
                if (_loading) { _core?.Stop(); return true; }
                return false;

            case Keys.Home when alt:
                _core?.Navigate(HomePage);
                return true;

            // Liga/desliga o bloqueador sem poluir a barra com mais um botao.
            case Keys.A when ctrl && shift:
                _ = ToggleProtectionAsync();
                return true;

            default:
                return false;
        }
    }

    // ------------------------------------- Ciclo de vida / uso de memoria --

    private async Task ToggleProtectionAsync()
    {
        if (_changingProtection || _tabs is null) return;
        _changingProtection = true;
        try
        {
            await _adProtection.SetEnabledAsync(!_adProtection.Enabled);
            foreach (var tab in _tabView.TabPages.OfType<BrowserTab>().ToArray())
            {
                if (tab.IsDisposed || tab.Web.CoreWebView2 is not { } core) continue;
                tab.Blocker.Enabled = _adProtection.Enabled;
                await tab.DocumentProtection.SetEnabledAsync(core, _adProtection.Enabled);
                if (!tab.IsDisposed) core.Reload();
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed) MessageBox.Show(this, "Não foi possível alterar toda a proteção. Reinicie o navegador.\n" + ex.Message);
        }
        finally { _changingProtection = false; }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (_core is null)
            return;

        if (WindowState == FormWindowState.Minimized)
            SuspendEngine();
        else
            ResumeEngine();
    }

    private async void SuspendEngine()
    {
        foreach (var tab in _tabView.TabPages.OfType<BrowserTab>().ToArray())
        {
            var core = tab.Web.CoreWebView2;
            if (core is null) continue;
            try
            {
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                // Não escondemos o controle: evita corridas ao restaurar durante o await.
                await core.TrySuspendAsync();
                if (!tab.IsDisposed && !IsDisposed && WindowState != FormWindowState.Minimized)
                {
                    if (core.IsSuspended) core.Resume();
                    core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                }
            }
            catch (Exception) { /* Aba fechada ou runtime não permite suspensão. */ }
            if (IsDisposed || WindowState != FormWindowState.Minimized) break;
        }
    }

    private void ResumeEngine()
    {
        foreach (var tab in _tabView.TabPages.OfType<BrowserTab>().ToArray())
        {
            var core = tab.Web.CoreWebView2;
            if (core is null) continue;
            try
            {
                if (core.IsSuspended) core.Resume();
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
            }
            catch (Exception) { /* Controle em encerramento. */ }
        }
    }
}

