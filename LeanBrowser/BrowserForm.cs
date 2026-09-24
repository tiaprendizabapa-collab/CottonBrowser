using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using CottonBrowser.Shared;

namespace LeanBrowser;

public sealed class BrowserForm : Form
{
    private const string HomePage = TrustedBrowserBridge.NewTabUrl;
    private const int ResizeBorder = 6;

    private readonly Panel      _toolbar  = new();
    private readonly BookmarksBar _bookmarksBar = new();
    private readonly ToolButton _back     = new("\uE72B", "Voltar");
    private readonly ToolButton _forward  = new("\uE72A", "Avancar");
    private readonly ToolButton _reload   = new("\uE72C", "Recarregar");
    private readonly Omnibox    _omnibox  = new();
    private readonly SuggestionPanel _suggestionPanel = new();
    private readonly NavigationHistoryStore _navigationHistory = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeanBrowser", "history.json"));
    private readonly SearchSuggestionClient _searchSuggestions = new();
    private readonly System.Windows.Forms.Timer _suggestionDebounce = new() { Interval = 300 };
    private CancellationTokenSource? _suggestionRequest;
    private int _suggestionVersion;
    private SuggestionItem? _inlineSuggestion;
    private string _typedQuery = string.Empty;
    private bool _applyingInlineCompletion;
    private bool _skipInlineCompletionOnce;
    private readonly System.Windows.Forms.Timer _zoomFade = new() { Interval = 40 };
    private long _zoomShownAt;
    private readonly BrowserTabControl _tabView = new() { Dock = DockStyle.Fill };
    private readonly Panel _tabBar = new() { Dock = DockStyle.Top, Height = 50, BackColor = Theme.Chrome };
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
    private readonly WindowCaptionButton _minimizeButton = new(WindowCaptionAction.Minimize);
    private readonly WindowCaptionButton _maximizeButton = new(WindowCaptionAction.Maximize);
    private readonly WindowCaptionButton _closeButton = new(WindowCaptionAction.Close);
    private readonly ContextMenuStrip _overflowMenu = new()
    {
        ShowImageMargin = false,
        ShowCheckMargin = false,
        AutoClose = true,
        AutoSize = true,
        Padding = new Padding(4)
    };
    private readonly OverflowDismissFilter _overflowDismissFilter;
    private readonly System.Windows.Forms.Timer _overflowOutsideClickTimer = new() { Interval = 15 };
    private bool _overflowAwaitingRelease;
    private readonly ToolTip _zoomTip = new();
    private readonly BrowserUpdateService _updates = new();
    private readonly CancellationTokenSource _updateLifetime = new();
    private readonly System.Windows.Forms.Timer _updatePoll = new() { Interval = 3 * 60 * 60 * 1000 };
    private readonly ToolStripMenuItem _updateItem = new("Atualizar CottonBrowser");
    private BrowserUpdate? _availableUpdate;
    private Version? _offeredUpdateVersion;
    private bool _updateBusy;
    private readonly string _themePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LeanBrowser", "theme.txt");
    private readonly string _accentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LeanBrowser", "accent.txt");
    private readonly AccessControl _access = new();
    private readonly NavigationTelemetry _telemetry;
    private SettingsTab? _settingsTab;
    private ProfileTab? _profileTab;
    private readonly AdProtection _adProtection = new();
    private readonly SiteAllowlist _siteAllowlist = new(new SiteExceptionStore());
    private readonly ISecretStore _secrets = new DpapiSecretStore();
    private readonly WebRiskReputationService _urlReputation;
    private readonly PermissionPolicy _permissionPolicy = new();
    private bool _permissionCenterOpening;
    private bool _changingProtection;
    private bool _protectionFailureShown;
    private readonly BookmarkStore _bookmarks = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeanBrowser", "bookmarks.json"));
    private readonly DownloadHistoryStore _downloadHistory = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeanBrowser", "downloads.json"));
    private readonly Dictionary<CoreWebView2DownloadOperation, DownloadEntry> _activeDownloads = new();
    private readonly Dictionary<Guid, int> _lastPersistedDownloadPercent = new();
    private DownloadsTab? _downloadsTab;
    private int _downloadRefreshPending;
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
    private bool _updatingOmnibox;
    private bool _selectAllOnClick;
    private bool _browserFullscreen;
    private bool _windowFullscreen;
    private FormBorderStyle _restoreBorderStyle;
    private FormWindowState _restoreWindowState;
    private Rectangle _restoreBounds;
    private bool _restoreTopMost;
    private bool _screenFitPending;
    private bool _movingWindow;
    private string? _lastScreenName;
    private Rectangle _lastScreenWorkArea;

    public BrowserForm()
    {
        SuspendLayout();

        _telemetry = new NavigationTelemetry(_access.UserName);
        _urlReputation = new WebRiskReputationService(_secrets);

        try { Theme.SetDark(File.Exists(_themePath) && File.ReadAllText(_themePath).Trim() == "dark"); } catch { }
        try { if (File.Exists(_accentPath)) Theme.SetAccent(ColorTranslator.FromHtml(File.ReadAllText(_accentPath).Trim())); } catch { }

        Text = "CottonBrowser";
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        FormBorderStyle = FormBorderStyle.None;
        Padding = new Padding(6);
        BackColor = Theme.Chrome;
        var initialArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1200, 780);
        ClientSize = new Size(Math.Min(1200, Math.Max(560, initialArea.Width - 32)),
            Math.Min(780, Math.Max(360, initialArea.Height - 32)));
        MinimumSize = new Size(560, 360);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font(Theme.UiFont, 9f);
        DoubleBuffered = true;
        KeyPreview = true;
        Activated += (_, _) => { if (_windowFullscreen) TopMost = true; };
        Deactivate += (_, _) => { if (_windowFullscreen) TopMost = false; };

        _overflowDismissFilter = new OverflowDismissFilter(this);
        Application.AddMessageFilter(_overflowDismissFilter);
        _overflowMenu.Opened += (_, _) =>
        {
            _overflowAwaitingRelease = true;
            _overflowOutsideClickTimer.Start();
        };
        _overflowMenu.Closed += (_, _) => _overflowOutsideClickTimer.Stop();
        _overflowOutsideClickTimer.Tick += (_, _) => DismissOverflowOnOutsideClick();

        BuildToolbar();
        BuildMenus();
        _permissionPolicy.PromptCreated += OnPermissionPromptCreated;
        _tabView.BrandClicked += OnBrandClicked;

        _tabBar.Controls.Add(_tabView.HeaderStrip);
        _bookmarksBar.SetTrailingControl(_overflowButton);
        _tabBar.Controls.Add(_minimizeButton);
        _tabBar.Controls.Add(_maximizeButton);
        _tabBar.Controls.Add(_closeButton);
        _tabBar.Resize += (_, _) => LayoutTabBar();
        _tabBar.MouseDown += OnTitleBarMouseDown;
        _tabBar.MouseDoubleClick += OnTitleBarMouseDoubleClick;
        _tabView.HeaderStrip.MouseDown += OnTitleBarMouseDown;
        _tabView.HeaderStrip.MouseDoubleClick += OnTitleBarMouseDoubleClick;
        _minimizeButton.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _maximizeButton.Click += (_, _) => ToggleMaximize();
        _closeButton.Click += (_, _) => Close();
        _bookmarksBar.OpenRequested += async (url, newTab) =>
        {
            if (!BookmarkStore.IsWebUrl(url)) return;
            if (newTab || _core is null) await OpenTabAsync(url, _tabs?.Active?.IsPrivate == true);
            else _core.Navigate(url);
        };
        Controls.Add(_tabView);
        Controls.Add(_bookmarksBar);
        Controls.Add(_toolbar);
        Controls.Add(_tabBar);
        Controls.Add(_suggestionPanel);
        _suggestionPanel.BringToFront();
        _suggestionPanel.Chosen += NavigateSuggestion;
        _suggestionPanel.Dismissed += DismissSuggestion;
        _suggestionDebounce.Tick += OnSuggestionDebounce;
        _zoomFade.Tick += OnZoomFade;
        _updatePoll.Tick += async (_, _) =>
            await OfferUpdateAsync(await CheckForUpdatesAsync(manual: false));

        // Os controles são criados antes de ler o tema persistido; sincroniza
        // a primeira pintura para que o modo escuro já nasça consistente.
        ApplyThemeRecursive(this);
        _tabView.ApplyTheme();
        _bookmarksBar.ApplyTheme();
        RefreshBookmarksBar();

        ResumeLayout(false);
        LayoutTabBar();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // Preserva o redimensionamento e os comandos de janela sem a barra de título nativa.
            parameters.Style |= 0x00040000 | 0x00080000 | 0x00020000 | 0x00010000;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        const int wmGetMinMaxInfo = 0x0024;
        const int wmDisplayChange = 0x007E;
        const int wmWindowPosChanged = 0x0047;
        const int wmEnterSizeMove = 0x0231;
        const int wmExitSizeMove = 0x0232;
        const int wmDpiChanged = 0x02E0;

        if (message.Msg == wmEnterSizeMove) _movingWindow = true;
        if (message.Msg == 0x0084 && !_windowFullscreen && WindowState == FormWindowState.Normal)
        {
            var packed = message.LParam.ToInt64();
            var point = PointToClient(new Point(
                unchecked((short)(packed & 0xffff)),
                unchecked((short)((packed >> 16) & 0xffff))));
            var left = point.X < ResizeBorder;
            var right = point.X >= ClientSize.Width - ResizeBorder;
            var top = point.Y < ResizeBorder;
            var bottom = point.Y >= ClientSize.Height - ResizeBorder;
            var hit = top ? (left ? 13 : right ? 14 : 12)
                : bottom ? (left ? 16 : right ? 17 : 15)
                : left ? 10 : right ? 11 : 0;
            if (hit != 0) { message.Result = (IntPtr)hit; return; }
        }
        base.WndProc(ref message);
        if (message.Msg == wmGetMinMaxInfo)
            Native.SetMaximizedWorkArea(message.HWnd, message.LParam);
        else if (message.Msg == wmWindowPosChanged && IsHandleCreated && !IsDisposed && !Disposing)
        {
            var screen = Screen.FromHandle(Handle);
            var changed = _lastScreenName != screen.DeviceName ||
                _lastScreenWorkArea != screen.WorkingArea;
            _lastScreenName = screen.DeviceName;
            _lastScreenWorkArea = screen.WorkingArea;
            if (!_movingWindow && (changed || _windowFullscreen || WindowState == FormWindowState.Maximized))
                ScheduleScreenFit();
        }
        else if (message.Msg is wmDisplayChange or wmExitSizeMove or wmDpiChanged)
        {
            if (message.Msg == wmExitSizeMove) _movingWindow = false;
            ScheduleScreenFit();
        }
    }

    private void ScheduleScreenFit()
    {
        if (_screenFitPending || !IsHandleCreated || IsDisposed || Disposing) return;
        _screenFitPending = true;
        try
        {
            BeginInvoke(new Action(() =>
            {
                _screenFitPending = false;
                if (IsDisposed || Disposing || WindowState == FormWindowState.Minimized) return;
                FitToCurrentScreen();
            }));
        }
        catch (InvalidOperationException) { _screenFitPending = false; }
    }

    private void FitToCurrentScreen()
    {
        var screen = Screen.FromHandle(Handle);
        var area = _windowFullscreen ? screen.Bounds : screen.WorkingArea;
        if (_windowFullscreen || WindowState == FormWindowState.Maximized)
        {
            var current = Bounds;
            if (Math.Abs(current.Left - area.Left) > 8 || Math.Abs(current.Top - area.Top) > 8 ||
                Math.Abs(current.Width - area.Width) > 8 || Math.Abs(current.Height - area.Height) > 8)
                Native.FitWindowToArea(Handle, area);
        }
        else if (WindowState == FormWindowState.Normal)
        {
            var fitted = WindowLayout.FitNormal(Bounds, area);
            if (Bounds != fitted) Bounds = fitted;
        }
        PerformLayout();
        _tabView.PerformLayout();
        LayoutTabBar();
        LayoutToolbar();
    }

    // ---------------------------------------------------------------- UI ---

    private void BuildToolbar()
    {
        _toolbar.Dock = DockStyle.Top;
        _toolbar.Height = 54;
        _toolbar.BackColor = Theme.Chrome;
        _toolbar.Padding = new Padding(4, 0, 4, 0);

        // Gradiente de 3px no rodapé da barra, em direção ao conteúdo WebView2.
        _toolbar.Paint += (_, e) =>
        {
            if (_toolbar.Width <= 0 || _toolbar.Height < 3) return;
            var edge = new Rectangle(0, _toolbar.Height - 3, _toolbar.Width, 3);
            using var brush = new LinearGradientBrush(edge,
                Color.FromArgb(0, Color.Black), Color.FromArgb(Theme.IsDark ? 48 : 28, Color.Black),
                LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(brush, edge);
        };

        _back.Click    += (_, _) => _core?.GoBack();
        _forward.Click += (_, _) => _core?.GoForward();
        _reload.Click  += (_, _) =>
        {
            if (_loading) _core?.Stop();
            else          _core?.Reload();
        };
        _omnibox.Favorite.Click += (_, _) => AddBookmark();
        _omnibox.Zoom.Click += (_, _) => ChangeZoom(0);
        _zoomTip.SetToolTip(_omnibox.Zoom, "Redefinir zoom para 100%");

        _back.Enabled = false;
        _forward.Enabled = false;

        _omnibox.Input.KeyDown += OnOmniboxKeyDown;
        _omnibox.Input.TextChanged += OnOmniboxTextChanged;
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
            if (IsHandleCreated) BeginInvoke(new Action(() =>
            {
                if (IsDisposed || _omnibox.Input.Focused) return;
                HideSuggestions();
                if (_core is not null) ShowUrl(_core.Source);
            }));
        };

        _toolbar.Controls.Add(_back);
        _toolbar.Controls.Add(_forward);
        _toolbar.Controls.Add(_reload);
        _toolbar.Controls.Add(_omnibox);

        _toolbar.Resize += (_, _) => LayoutToolbar();
        _toolbar.LocationChanged += (_, _) => LayoutSuggestions();
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
        LayoutSuggestions();
    }

    private void LayoutSuggestions()
    {
        _suggestionPanel.Location = new Point(_toolbar.Left + _omnibox.Left,
            _toolbar.Top + _omnibox.Bottom + 2);
        _suggestionPanel.Width = _omnibox.Width;
    }

    private void LayoutTabBar()
    {
        var captionWidth = _minimizeButton.Width + _maximizeButton.Width + _closeButton.Width;
        var captionLeft = Math.Max(0, _tabBar.ClientSize.Width - captionWidth);
        _tabView.HeaderStrip.SetBounds(0, 0,
            captionLeft, _tabBar.ClientSize.Height);
        _minimizeButton.Location = new Point(captionLeft, 0);
        _maximizeButton.Location = new Point(captionLeft + _minimizeButton.Width, 0);
        _closeButton.Location = new Point(captionLeft + _minimizeButton.Width + _maximizeButton.Width, 0);
    }

    private void OnTitleBarMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !_windowFullscreen)
            Native.BeginWindowDrag(Handle);
    }

    private void OnTitleBarMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !_windowFullscreen) ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        if (_windowFullscreen) return;
        if (WindowState == FormWindowState.Maximized)
            WindowState = FormWindowState.Normal;
        else WindowState = FormWindowState.Maximized;
    }

    private void BuildMenus()
    {
        _tabView.NewTabRequested += async () => await OpenNewTabAsync(_tabs?.Active?.IsPrivate == true);
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
            else if (tab == _downloadsTab)
            {
                _tabView.TabPages.Remove(tab);
                tab.Dispose();
                _downloadsTab = null;
            }
        };
        _overflowButton.Click += (_, _) => ShowOverflowMenu(_overflowButton);
        _overflowButton.Paint += (_, e) => PaintOverflowButton(e.Graphics);
        _overflowButton.MouseEnter += (_, _) => _overflowButton.Invalidate();
        _overflowButton.MouseLeave += (_, _) => _overflowButton.Invalidate();
        var privateTab = new ToolStripMenuItem("Nova guia anônima (Ctrl+Shift+N)");
        privateTab.Click += async (_, _) => await OpenNewTabAsync(isPrivate: true);
        var browserCenter = new ToolStripMenuItem("Central do navegador");
        browserCenter.Click += async (_, _) => await OpenTabAsync(TrustedBrowserBridge.UiUrl);
        var configuration = new ToolStripMenuItem("Configurações");
        configuration.Click += (_, _) => OpenSettings();
        var profile = new ToolStripMenuItem("Perfil");
        profile.Click += (_, _) => OpenProfile();
        var downloads = new ToolStripMenuItem("Downloads");
        downloads.Click += (_, _) => OpenDownloads();
        var resetZoom = new ToolStripMenuItem("Redefinir zoom (100%)");
        resetZoom.Click += (_, _) => ChangeZoom(0);
        _updateItem.Click += async (_, _) =>
        {
            var update = _availableUpdate ?? await CheckForUpdatesAsync(manual: true);
            if (update is not null) await InstallUpdateAsync(update);
        };
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
            if (!_access.IsAdvancedMode) return;
            if (_adProtection.DashboardUrl is { } url) await OpenTabAsync(url);
        };
        protection.DropDownOpening += (_, _) =>
        {
            protectionEnabled.Checked = _adProtection.Enabled;
            protectionEnabled.Enabled = !_changingProtection;
            allowPopups.Checked = _tabs?.Active?.Popups.AllowedForCurrentOrigin == true;
            allowPopups.Visible = _access.IsAdvancedMode;
            allowPopups.Enabled = _access.IsAdvancedMode && _adProtection.Enabled && BookmarkStore.IsWebUrl(_core?.Source ?? "");
            settings.Visible = _access.IsAdvancedMode;
            settings.Enabled = _access.IsAdvancedMode && _adProtection.Available;
            protectionStatus.Text = !_adProtection.Enabled ? "Proteção pausada" :
                _adProtection.Available ? "uBlock Origin Lite ativo" : "Somente bloqueio básico ativo";
        };
        protection.DropDownItems.AddRange(new ToolStripItem[] { protectionEnabled, allowPopups, settings, protectionStatus });
        _overflowMenu.Items.AddRange(new ToolStripItem[] { privateTab, new ToolStripSeparator(),
            browserCenter, configuration, profile, downloads, resetZoom, _updateItem, protection });
        _overflowMenu.Font = new Font(Theme.UiFont, 9.5f);
        _overflowMenu.BackColor = Theme.Surface;
        _overflowMenu.ForeColor = Theme.Ink;
        _overflowMenu.Renderer = new OverflowMenuRenderer();
        _tabView.SelectedIndexChanged += (_, _) =>
        {
            _overflowMenu.Close();
            HideSuggestions();
            _zoomFade.Stop();
            _omnibox.Zoom.Opacity = 0;
            RefreshActiveTab();
        };
    }

    private void PaintOverflowButton(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Chrome);
        var point = _overflowButton.PointToClient(Cursor.Position);
        if (_overflowButton.ClientRectangle.Contains(point))
        {
            using var hotBrush = new SolidBrush(Theme.SurfaceHot);
            using var hotPath = Draw.RoundedRect(new Rectangle(1, 1, _overflowButton.Width - 3, _overflowButton.Height - 3), 9);
            g.FillPath(hotBrush, hotPath);
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
        if (_availableUpdate is not null)
        {
            using var updateBrush = new SolidBrush(Theme.Accent);
            g.FillEllipse(updateBrush, _overflowButton.Width - 11, 3, 7, 7);
        }
    }

    private void ShowOverflowMenu(Control anchor)
    {
        if (_overflowMenu.Visible)
        {
            _overflowMenu.Close();
            return;
        }
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

    private void DismissOverflowOnOutsideClick()
    {
        if (!_overflowMenu.Visible) return;
        if (Control.MouseButtons == MouseButtons.None)
        {
            _overflowAwaitingRelease = false;
            return;
        }
        if (_overflowAwaitingRelease) return;
        var point = Cursor.Position;
        if (!IsInsideOverflowMenu(point)
            && !_overflowButton.RectangleToScreen(_overflowButton.ClientRectangle).Contains(point))
            _overflowMenu.Close(ToolStripDropDownCloseReason.AppClicked);
    }

    private bool IsInsideOverflowMenu(Point point) => IsInsideMenu(_overflowMenu, point);

    private static bool IsInsideMenu(ToolStrip strip, Point point)
    {
        if (strip.Visible && strip.Bounds.Contains(point)) return true;
        foreach (ToolStripMenuItem item in strip.Items.OfType<ToolStripMenuItem>())
            if (item.HasDropDownItems && item.DropDown.Visible && IsInsideMenu(item.DropDown, point))
                return true;
        return false;
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
            RefreshBookmarksBar();
            _settingsTab?.ReloadBookmarks();
        }
    });

    private void RefreshBookmarksBar()
    {
        try { _bookmarksBar.SetBookmarks(_bookmarks.Load()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        { _bookmarksBar.SetBookmarks(Array.Empty<Bookmark>()); }
        _bookmarksBar.Visible = !_windowFullscreen;
    }

    private void OpenSettings()
    {
        if (_settingsTab is null)
        {
            _settingsTab = new SettingsTab(ApplyTheme, ClearSavedPasswords, () =>
            {
                try { return _bookmarks.Load(); } catch { return Array.Empty<Bookmark>(); }
            }, AddBookmark, url => WithBookmarkErrors(() =>
            {
                _bookmarks.Remove(url);
                RefreshBookmarksBar();
                _omnibox.SetFavorite(IsFavorite(_core?.Source ?? ""));
            }), _access.IsAdvancedMode, ApplyAccent);
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

    private void OpenDownloads()
    {
        if (_downloadsTab is null || _downloadsTab.IsDisposed)
        {
            _downloadsTab = new DownloadsTab(_downloadHistory);
            _tabView.TabPages.Add(_downloadsTab);
        }
        _downloadsTab.RefreshEntries();
        _tabView.SelectedTab = _downloadsTab;
    }

    private void ApplyTheme(bool dark)
    {
        Theme.SetDark(dark);
        try { Directory.CreateDirectory(Path.GetDirectoryName(_themePath)!); File.WriteAllText(_themePath, dark ? "dark" : "light"); } catch { }
        BackColor = Theme.Chrome; _toolbar.BackColor = Theme.Chrome; _tabBar.BackColor = Theme.Chrome;
        _overflowButton.BackColor = Theme.Chrome; _overflowButton.ForeColor = Theme.Ink;
        _minimizeButton.Invalidate(); _maximizeButton.Invalidate(); _closeButton.Invalidate();
        _overflowMenu.BackColor = Theme.Surface; _overflowMenu.ForeColor = Theme.Ink;
        ApplyThemeRecursive(this);
        _tabView.ApplyTheme();
        _bookmarksBar.ApplyTheme();
        _settingsTab?.ApplyTheme();
        _profileTab?.ApplyTheme();
        _downloadsTab?.ApplyTheme();
        _suggestionPanel.Invalidate();
        _omnibox.Zoom.Invalidate();
        foreach (var tab in _tabView.TabPages.OfType<BrowserTab>())
        {
            try { if (tab.Web.CoreWebView2 is { } core) core.Profile.PreferredColorScheme = Theme.IsDark
                ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light; }
            catch { }
        }
        if (IsHandleCreated) Native.SetDarkCaption(Handle, dark);
        _tabView.Invalidate(true); Invalidate(true);
    }

    private void ApplyAccent(Color color)
    {
        Theme.SetAccent(color);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_accentPath)!);
            File.WriteAllText(_accentPath, ColorTranslator.ToHtml(Theme.CustomAccent!.Value));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        ApplyTheme(Theme.IsDark);
        RefreshActiveTab(resetEditing: false);
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

    private sealed class OverflowDismissFilter(BrowserForm owner) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message message)
        {
            if (!owner._overflowMenu.Visible
                || message.Msg is not (0x0201 or 0x0204 or 0x0207 or 0x00A1))
                return false;

            var point = Cursor.Position;
            if (owner.IsInsideOverflowMenu(point)
                || owner._overflowButton.RectangleToScreen(owner._overflowButton.ClientRectangle).Contains(point))
                return false;

            owner._overflowMenu.Close(ToolStripDropDownCloseReason.AppClicked);
            return false; // o clique continua para o controle de destino
        }

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
        if (!_access.IsAdvancedMode) return;
        var core = _core;
        if (core is null || MessageBox.Show(this, "Apagar todas as senhas salvas neste navegador?", "Senhas", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { await PasswordManager.ClearSavedPasswordsAsync(core); MessageBox.Show(this, "Senhas salvas apagadas."); }
        catch { MessageBox.Show(this, "Não foi possível apagar as senhas."); }
    }

    private async Task OpenNewTabAsync(bool isPrivate = false)
    {
        if (_tabs is null || IsDisposed) return;
        try
        {
            var url = isPrivate ? TrustedBrowserBridge.PrivateTabUrl : HomePage;
            var creation = _tabs.CreateAsync(url, isPrivate, focusOmniboxOnFirstLoad: true);
            _omniboxDirty = false;
            FocusOmniboxForNewTab();
            var tab = await creation;
            if (tab is not null && tab.PendingNavigation is null && _tabs?.Active == tab && !_omniboxDirty)
                FocusOmniboxForNewTab();
        }
        catch (Exception ex)
        {
            if (!IsDisposed) MessageBox.Show(this, "Não foi possível abrir a aba.\n" + ex.Message);
        }
    }

    private void FocusOmniboxForNewTab()
    {
        if (IsDisposed || !_omnibox.Input.CanFocus) return;
        _omnibox.Input.Focus();
        _omnibox.Input.SelectAll();
    }

    private async Task OpenTabAsync(string url, bool isPrivate = false)
    {
        if (_tabs is null || IsDisposed) return;
        try { await _tabs.CreateAsync(url, isPrivate); }
        catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, "Não foi possível abrir a aba.\n" + ex.Message); }
    }

    private async Task OpenAdminDashboardAsync()
    {
        // Advanced mode only controls discoverability; the server still requires its Admin token.
        if (!_access.IsAdvancedMode || IsDisposed) return;
        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2),
                MaxResponseContentBufferSize = 1024
            };
            using var response = await client.GetAsync(AdminDashboardEndpoint.HealthUrl);
            response.EnsureSuccessStatusCode();
            using var health = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!health.RootElement.TryGetProperty("service", out var service) ||
                service.GetString() != "cotton-monitoring")
                throw new HttpRequestException("Serviço inesperado na porta do painel Admin.");
            if (!IsDisposed) await OpenTabAsync(AdminDashboardEndpoint.DashboardUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (!IsDisposed)
                MessageBox.Show(this,
                    "O painel Admin não está disponível neste computador.\n" +
                    "Inicie o MonitoringServer em http://localhost:5270 e tente novamente.\n" +
                    "O acesso ao painel ainda exige o token Admin.",
                    "Administração", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task<bool> OpenTabFromBridgeAsync(string url, bool isPrivate)
    {
        if (_tabs is null || IsDisposed) return false;
        try { return await _tabs.CreateAsync(url, isPrivate) is not null; }
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
        Text = BuildTitle(_core?.DocumentTitle ?? "", _tabs?.Active?.IsPrivate == true);
        _telemetry.RecordActiveTab(url, _core?.DocumentTitle);
        _omnibox.SetIndicator(_core?.Source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true,
            _blocker?.BlockedOnCurrentPage ?? 0);
        SynchronizeFullscreen();
    }

    private bool ActivePageIsFullscreen()
    {
        try { return _core?.ContainsFullScreenElement == true; }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { return false; }
    }

    private void SynchronizeFullscreen()
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        var shouldFillScreen = _browserFullscreen || ActivePageIsFullscreen();
        if (shouldFillScreen == _windowFullscreen) return;

        if (shouldFillScreen)
        {
            _restoreBorderStyle = FormBorderStyle;
            _restoreWindowState = WindowState;
            _restoreBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _restoreTopMost = TopMost;
            var screenBounds = Screen.FromControl(this).Bounds;

            SuspendLayout();
            try
            {
                _tabBar.Visible = false;
                _toolbar.Visible = false;
                _bookmarksBar.Visible = false;
                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                Bounds = screenBounds;
                Padding = Padding.Empty;
                TopMost = true;
            }
            finally { ResumeLayout(true); }
            _windowFullscreen = true;
            _web?.Focus();
            return;
        }

        _windowFullscreen = false;
        SuspendLayout();
        try
        {
            TopMost = _restoreTopMost;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = _restoreBorderStyle;
            if (!_restoreBounds.IsEmpty) Bounds = _restoreBounds;
            WindowState = _restoreWindowState;
            _tabBar.Visible = true;
            _toolbar.Visible = true;
            RefreshBookmarksBar();
        }
        finally { ResumeLayout(true); }
        Native.EnableRoundedCorners(Handle);
        Native.EnableMica(Handle);
        Native.SetDarkCaption(Handle, Theme.IsDark);
    }

    private bool IsFavorite(string url)
    {
        if (!BookmarkStore.IsWebUrl(url)) return false;
        try { return _bookmarks.Load().Any(bookmark => bookmark.Url == url); }
        catch { return false; }
    }
    // ----------------------------------------------------- Inicializacao ---

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ScheduleScreenFit();
        try
        {
            await Task.Delay(1500, _updateLifetime.Token);
            await OfferUpdateAsync(await CheckForUpdatesAsync(manual: false));
            if (!IsDisposed) _updatePoll.Start();
        }
        catch (OperationCanceledException) { /* janela encerrada */ }
    }

    private async Task<BrowserUpdate?> CheckForUpdatesAsync(bool manual)
    {
        if (_updateBusy || IsDisposed) return null;
        _updateBusy = true;
        _updateItem.Enabled = false;
        _updateItem.Text = "Verificando atualizações...";
        try
        {
            var current = typeof(BrowserForm).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
            var update = await _updates.CheckAsync(current, _updateLifetime.Token);
            if (IsDisposed) return null;
            _availableUpdate = update;
            _updateItem.Text = update is null
                ? "Atualizar CottonBrowser"
                : $"Atualização disponível ({update.Tag})";
            _overflowButton.Invalidate();
            if (manual && update is null)
                MessageBox.Show(this, "Nenhuma versão nova foi publicada para este navegador.",
                    "Atualizações", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return update;
        }
        catch (OperationCanceledException) when (_updateLifetime.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            _availableUpdate = null;
            if (manual && !IsDisposed)
                MessageBox.Show(this, "Não foi possível verificar as atualizações.\n\n" + ex.Message,
                    "Atualizações", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        finally
        {
            _updateBusy = false;
            if (!IsDisposed)
            {
                _updateItem.Enabled = true;
                if (_updateItem.Text == "Verificando atualizações...")
                    _updateItem.Text = _availableUpdate is null
                        ? "Atualizar CottonBrowser"
                        : $"Atualização disponível ({_availableUpdate.Tag})";
            }
        }
    }

    private async Task OfferUpdateAsync(BrowserUpdate? update)
    {
        if (update is null || IsDisposed || _offeredUpdateVersion == update.Version) return;
        _offeredUpdateVersion = update.Version;
        await InstallUpdateAsync(update);
    }

    private async Task InstallUpdateAsync(BrowserUpdate update)
    {
        if (_updateBusy || IsDisposed) return;
        if (MessageBox.Show(this,
            $"O CottonBrowser {update.Tag} está disponível. Deseja atualizar agora? " +
            "O download acontece aqui no navegador; ele será fechado e reiniciado. " +
            "Se escolher Não, poderá atualizar depois no menu ⋮ > Atualizar CottonBrowser.",
            "Atualização disponível", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _updateBusy = true;
        _updateItem.Enabled = false;
        _updateItem.Text = "Baixando atualização...";
        try
        {
            var progress = new Progress<int>(percent =>
            {
                if (!IsDisposed) _updateItem.Text = $"Baixando atualização... {percent}%";
            });
            var package = await _updates.DownloadAsync(update, progress, _updateLifetime.Token);
            if (IsDisposed) return;

            var start = new ProcessStartInfo(package.UpdaterPath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(package.UpdaterPath)!
            };
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            start.ArgumentList.Add(package.ArchivePath);
            start.ArgumentList.Add(AppContext.BaseDirectory);
            start.ArgumentList.Add(update.Sha256);
            if (Process.Start(start) is null)
                throw new IOException("Não foi possível iniciar o instalador da atualização.");
            Close();
        }
        catch (OperationCanceledException) when (_updateLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!IsDisposed)
                MessageBox.Show(this, "Não foi possível baixar ou iniciar a atualização.\n\n" + ex.Message,
                    "Atualizações", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _updateBusy = false;
            if (!IsDisposed)
            {
                _updateItem.Enabled = true;
                _updateItem.Text = $"Atualização disponível ({update.Tag})";
            }
        }
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        Native.EnableRoundedCorners(Handle);
        Native.EnableMica(Handle);
        Native.SetDarkCaption(Handle, Theme.IsDark);
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
        _updateLifetime.Cancel();
        _updatePoll.Dispose();
        _updates.Dispose();
        _updateLifetime.Dispose();
        Application.RemoveMessageFilter(_overflowDismissFilter);
        _overflowOutsideClickTimer.Dispose();
        _zoomTip.Dispose();
        HideSuggestions();
        _suggestionDebounce.Dispose();
        _zoomFade.Dispose();
        _searchSuggestions.Dispose();
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
        _tabs = new BrowserTabManager(_tabView, env, _siteAllowlist);
        _tabs.InitializeTabAsync = async tab =>
        {
            tab.NetworkProtection = new NetworkProtection(
                tab.Web,
                _urlReputation,
                ConfirmInsecureNavigationAsync,
                _siteAllowlist);
            tab.PermissionSubscription = _permissionPolicy.Attach(tab.Web);
            if (!tab.IsPrivate)
                await _permissionPolicy.ResetPersistedPermissionsAsync(tab.Web.CoreWebView2.Profile);
            TrustedBrowserBridge.Attach(tab.Web, new TrustedBrowserBridgeHandlers(
                url => OpenTabFromBridgeAsync(url, tab.IsPrivate),
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
        await _tabs.CreateAsync(HomePage, focusOmniboxOnFirstLoad: true);
        FocusOmniboxForNewTab();
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
        var faviconRequest = 0;
        ApplySettings(core);
        PasswordManager.Configure(core);
        if (tab.IsPrivate)
        {
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.DownloadStarting += (_, args) => OnPrivateDownloadStarting(args);
        }
        else
            core.DownloadStarting += OnDownloadStarting;
        tab.Blocker.Attach(core);
        core.NavigationStarting += (_, e) =>
        {
            faviconRequest++;
            // Mantém o ícone atual até o WebView2 confirmar uma mudança.
            // Páginas do mesmo site podem reutilizar a favicon sem disparar FaviconChanged.
            tab.Popups.OnNavigation(e.Uri);
            if (!e.IsRedirected) tab.Blocker.ResetPageCounter();
            tab.Loading = true;
            if (_tabs?.Active == tab) SetLoading(true);
        };
        core.FaviconChanged += async (_, _) =>
        {
            var request = ++faviconRequest;
            try
            {
                using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
                using var decoded = Image.FromStream(stream);
                var favicon = new Bitmap(decoded); // independente do stream descartado
                if (tab.IsDisposed || _tabView.IsDisposed || request != faviconRequest)
                    favicon.Dispose();
                else
                {
                    _suggestionPanel.CacheFavicon(core.Source, favicon);
                    _bookmarksBar.RememberFavicon(core.Source, favicon);
                    _tabView.SetFavicon(tab, favicon); // transfere a propriedade da imagem
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException
                or OperationCanceledException or OutOfMemoryException or System.Runtime.InteropServices.COMException)
            {
                if (!tab.IsDisposed && !_tabView.IsDisposed && request == faviconRequest)
                    _tabView.SetFavicon(tab, null);
            }
        };
        core.NavigationCompleted += (_, e) =>
        {
            tab.Loading = false;
            if (e.IsSuccess)
            {
                _telemetry.RecordNavigation(core.Source, core.DocumentTitle, _tabs?.Active == tab);
                if (!tab.IsPrivate) _navigationHistory.Record(core.Source, core.DocumentTitle);
            }
            if (_tabs?.Active == tab) { SetLoading(false); RefreshActiveTab(resetEditing: false); }
            if (tab.FocusOmniboxOnFirstLoad)
            {
                tab.FocusOmniboxOnFirstLoad = false;
                if (e.IsSuccess && (core.Source == HomePage || core.Source == TrustedBrowserBridge.PrivateTabUrl)
                    && _tabs?.Active == tab && !_omniboxDirty && IsHandleCreated)
                    BeginInvoke(new Action(() =>
                    {
                        if (!IsDisposed && !tab.IsDisposed && _tabs?.Active == tab && !_omniboxDirty)
                            FocusOmniboxForNewTab();
                    }));
            }
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
            var shortTitle = string.IsNullOrWhiteSpace(title) ? "Nova aba" : title[..Math.Min(32, title.Length)];
            tab.Text = tab.IsPrivate ? "Anônima · " + shortTitle : shortTitle;
            if (_tabs?.Active == tab) Text = BuildTitle(title, tab.IsPrivate);
        };
        core.ContainsFullScreenElementChanged += (_, _) =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed && !tab.IsDisposed && _tabs?.Active == tab)
                    SynchronizeFullscreen();
            }));
        };
        tab.Web.KeyDown += OnWebKeyDown;
        tab.Web.MouseDown += (_, _) => _overflowMenu.Close();
        tab.Web.ZoomFactorChanged += (_, _) =>
        {
            if (_tabs?.Active == tab && !IsDisposed) ShowZoom(tab.Web.ZoomFactor);
        };
        core.ContextMenuRequested += (_, e) => ReplaceOpenInNewWindowCommand(tab, e);
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if ((_adProtection.Enabled ? tab.Popups.ShouldOpen(e.Uri, e.IsUserInitiated)
                : e.IsUserInitiated && BookmarkStore.IsWebUrl(e.Uri)))
            {
                var url = e.Uri;
                BeginInvoke(new Action(async () => await OpenTabAsync(url, tab.IsPrivate)));
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

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs args)
    {
        try
        {
            var operation = args.DownloadOperation;
            var resultPath = ChooseDownloadPath(args.ResultFilePath, operation.Uri);
            args.ResultFilePath = resultPath;
            args.Handled = true;

            var entry = new DownloadEntry(
                Guid.NewGuid(),
                Path.GetFileName(resultPath),
                operation.Uri,
                resultPath,
                Math.Max(0, operation.BytesReceived),
                DownloadTotalBytes(operation.TotalBytesToReceive),
                DownloadStatus.InProgress,
                null,
                DateTimeOffset.Now,
                null);

            _activeDownloads[operation] = entry;
            _downloadHistory.Upsert(entry);
            _lastPersistedDownloadPercent[entry.Id] = -1;
            operation.BytesReceivedChanged += (_, _) => UpdateDownload(operation);
            operation.StateChanged += (_, _) => UpdateDownload(operation);
            BeginInvoke(new Action(OpenDownloads));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            args.Cancel = true;
            if (!IsDisposed)
                BeginInvoke(new Action(() => MessageBox.Show(this,
                    "Não foi possível preparar o arquivo para download.\n" + ex.Message,
                    "Downloads", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
        }
    }

    private void OnPrivateDownloadStarting(CoreWebView2DownloadStartingEventArgs args)
    {
        try
        {
            args.ResultFilePath = ChooseDownloadPath(args.ResultFilePath, args.DownloadOperation.Uri);
            args.Handled = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            args.Cancel = true;
            if (!IsDisposed)
                BeginInvoke(new Action(() => MessageBox.Show(this,
                    "Não foi possível preparar o arquivo para download.\n" + ex.Message,
                    "Downloads", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
        }
    }

    private void UpdateDownload(CoreWebView2DownloadOperation operation)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => UpdateDownload(operation))); }
            catch (InvalidOperationException) { }
            return;
        }

        if (!_activeDownloads.TryGetValue(operation, out var current)) return;
        var state = operation.State;
        var status = state switch
        {
            CoreWebView2DownloadState.Completed => DownloadStatus.Completed,
            CoreWebView2DownloadState.Interrupted => DownloadStatus.Interrupted,
            _ => DownloadStatus.InProgress
        };
        var updated = current with
        {
            BytesReceived = Math.Max(0, operation.BytesReceived),
            TotalBytes = DownloadTotalBytes(operation.TotalBytesToReceive),
            Status = status,
            Detail = status == DownloadStatus.Interrupted ? operation.InterruptReason.ToString() : null,
            FinishedAt = status == DownloadStatus.InProgress ? null : DateTimeOffset.Now
        };
        _activeDownloads[operation] = updated;

        var percent = GetDownloadPercent(updated);
        var shouldPersist = status != DownloadStatus.InProgress
            || !_lastPersistedDownloadPercent.TryGetValue(updated.Id, out var lastPercent)
            || percent != lastPercent;
        if (shouldPersist)
        {
            _lastPersistedDownloadPercent[updated.Id] = percent;
            _downloadHistory.Upsert(updated);
        }
        ScheduleDownloadRefresh();
    }

    private void ScheduleDownloadRefresh()
    {
        if (_downloadsTab is null || _downloadsTab.IsDisposed || Interlocked.Exchange(ref _downloadRefreshPending, 1) != 0)
            return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                Interlocked.Exchange(ref _downloadRefreshPending, 0);
                _downloadsTab?.RefreshEntries();
            }));
        }
        catch (InvalidOperationException) { Interlocked.Exchange(ref _downloadRefreshPending, 0); }
    }

    private static int GetDownloadPercent(DownloadEntry entry)
    {
        if (entry.Status == DownloadStatus.Completed) return 100;
        if (entry.TotalBytes <= 0) return -1;
        return (int)Math.Clamp(entry.BytesReceived * 100L / entry.TotalBytes, 0, 100);
    }

    private static long DownloadTotalBytes(ulong? total)
    {
        if (!total.HasValue || total.Value > long.MaxValue) return -1;
        return (long)total.Value;
    }

    private static string ChooseDownloadPath(string suggestedPath, string sourceUrl)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(directory);

        var fileName = Path.GetFileName(suggestedPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            try { fileName = Path.GetFileName(Uri.UnescapeDataString(new Uri(sourceUrl).AbsolutePath)); }
            catch (UriFormatException) { }
        }
        fileName = SanitizeDownloadFileName(fileName);
        var candidate = Path.Combine(directory, fileName);
        var suffix = 1;
        while (File.Exists(candidate))
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            candidate = Path.Combine(directory, $"{stem} ({suffix++}){extension}");
        }
        return candidate;
    }

    private static string SanitizeDownloadFileName(string? fileName)
    {
        var value = string.IsNullOrWhiteSpace(fileName) ? "download" : fileName.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        value = value.Trim('.', ' ');
        return string.IsNullOrWhiteSpace(value) ? "download" : value;
    }

    private void ReplaceOpenInNewWindowCommand(BrowserTab tab, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var core = tab.Web.CoreWebView2;
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
                BeginInvoke(new Action(async () => await OpenTabAsync(url, tab.IsPrivate)));
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

    private void OnOmniboxTextChanged(object? sender, EventArgs e)
    {
        if (_applyingInlineCompletion || _updatingOmnibox) return;
        _omniboxDirty = _omnibox.Input.Focused;
        if (!_omniboxDirty) return;

        var skipInline = _skipInlineCompletionOnce;
        _skipInlineCompletionOnce = false;
        HideSuggestions();
        var query = _omnibox.Input.Text.Trim();
        if (query.Length == 0) return;

        if (_tabs?.Active?.IsPrivate == true) return;
        _typedQuery = query;
        if (!skipInline && _omnibox.Input.Text == query
            && _omnibox.Input.SelectionStart == query.Length
            && _omnibox.Input.SelectionLength == 0)
        {
            _inlineSuggestion = _navigationHistory.FindAddressCompletion(query);
            if (_inlineSuggestion?.Url is { } url)
            {
                var display = UrlHelper.ForDisplay(url);
                _applyingInlineCompletion = true;
                try
                {
                    _omnibox.Input.Text = query + display[query.Length..];
                    _omnibox.Input.SelectionStart = query.Length;
                    _omnibox.Input.SelectionLength = display.Length - query.Length;
                }
                finally { _applyingInlineCompletion = false; }
            }
        }
        ShowSuggestions(BuildSuggestions(query, Array.Empty<SuggestionItem>()));
        if (query.Length < 2 || query.Contains('/') || query.Contains(':')) return;
        _suggestionDebounce.Start();
    }

    private IReadOnlyList<SuggestionItem> BuildSuggestions(string query, IReadOnlyList<SuggestionItem> remote)
    {
        var history = _navigationHistory.Find(query);
        var primary = _inlineSuggestion ?? history.FirstOrDefault(item => item.Url is not null
            && UrlHelper.ForDisplay(item.Url).StartsWith(query, StringComparison.OrdinalIgnoreCase));
        return (primary is null ? new[] { new SuggestionItem(query, null) } : new[] { primary })
            .Concat(history).Concat(remote)
            .DistinctBy(item => item.Url ?? item.Text, StringComparer.OrdinalIgnoreCase)
            .Take(8).ToArray();
    }

    private async void OnSuggestionDebounce(object? sender, EventArgs e)
    {
        _suggestionDebounce.Stop();
        var query = _typedQuery;
        var version = _suggestionVersion;
        var request = new CancellationTokenSource();
        _suggestionRequest = request;
        try
        {
            var remote = await _searchSuggestions.FetchAsync(query, request.Token);
            if (IsDisposed || !_omnibox.Input.Focused || version != _suggestionVersion
                || _tabs?.Active?.IsPrivate == true) return;
            ShowSuggestions(BuildSuggestions(query, remote));
        }
        finally
        {
            if (ReferenceEquals(_suggestionRequest, request)) _suggestionRequest = null;
            request.Dispose();
        }
    }

    private void ShowSuggestions(IReadOnlyList<SuggestionItem> items)
    {
        _suggestionPanel.SetItems(items);
        if (items.Count > 0) _suggestionPanel.BringToFront();
    }

    private void HideSuggestions()
    {
        _suggestionVersion++;
        _suggestionDebounce.Stop();
        _suggestionRequest?.Cancel();
        _suggestionRequest = null;
        _inlineSuggestion = null;
        _typedQuery = string.Empty;
        _suggestionPanel.SetItems(Array.Empty<SuggestionItem>());
    }

    private void NavigateSuggestion(SuggestionItem item)
    {
        HideSuggestions();
        _omniboxDirty = false;
        _core?.Navigate(item.Url ?? UrlHelper.Normalize(item.Text));
        _web?.Focus();
    }

    private void DismissSuggestion(SuggestionItem item)
    {
        if (item.Url is null) return;
        var query = _typedQuery;
        _navigationHistory.Remove(item.Url);
        _applyingInlineCompletion = true;
        try
        {
            _omnibox.Input.Text = query;
            _omnibox.Input.SelectionStart = query.Length;
            _omnibox.Input.SelectionLength = 0;
        }
        finally { _applyingInlineCompletion = false; }
        OnOmniboxTextChanged(_omnibox.Input, EventArgs.Empty);
    }

    private void ShowZoom(double factor)
    {
        _omnibox.Zoom.ShowPercentage(factor);
        _zoomShownAt = Environment.TickCount64;
        _zoomFade.Stop();
        _zoomFade.Start();
    }

    private void ChangeZoom(int direction)
    {
        if (_web is not { } web) return;
        double[] levels = [0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5];
        var current = web.ZoomFactor;
        var next = direction == 0 ? 1d : direction > 0
            ? levels.FirstOrDefault(level => level > current + 0.001, levels[^1])
            : levels.LastOrDefault(level => level < current - 0.001, levels[0]);
        web.ZoomFactor = next;
        ShowZoom(web.ZoomFactor);
    }

    private void OnZoomFade(object? sender, EventArgs e)
    {
        var elapsed = Environment.TickCount64 - _zoomShownAt;
        if (elapsed < 2400) return;
        _omnibox.Zoom.Opacity = (int)Math.Clamp((3000 - elapsed) * 255 / 600, 0, 255);
        if (elapsed >= 3000) _zoomFade.Stop();
    }

    private void OnOmniboxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Back or Keys.Delete) _skipInlineCompletionOnce = true;
        if (e.KeyCode == Keys.Escape && _suggestionPanel.Visible)
        {
            var query = _typedQuery;
            HideSuggestions();
            _applyingInlineCompletion = true;
            try { _omnibox.Input.Text = query; _omnibox.Input.SelectionStart = query.Length; }
            finally { _applyingInlineCompletion = false; }
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        if (e.KeyCode is Keys.Down or Keys.Up && _suggestionPanel.Visible)
        {
            _suggestionPanel.MoveSelection(e.KeyCode == Keys.Down ? 1 : -1);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        if (e.KeyCode != Keys.Enter)
            return;

        e.SuppressKeyPress = true; // mata o "ding" do Windows
        e.Handled = true;

        var selected = _suggestionPanel.SelectedItem;
        var enteredQuery = _tabs?.Active?.IsPrivate == true ? _omnibox.Input.Text : _typedQuery;
        var target = selected?.Url ?? UrlHelper.Normalize(selected?.Text ?? enteredQuery);

        HideSuggestions();
        _omniboxDirty = false;
        if (_core is { } core)
        {
            core.Navigate(target);
            _web?.Focus();
        }
        else if (_tabs?.Active is { } tab)
        {
            tab.PendingNavigation = target;
            tab.FocusOmniboxOnFirstLoad = false;
        }
    }

    private void SetLoading(bool loading)
    {
        if (_loading == loading)
            return;

        _loading = loading;
        _omnibox.SetNavigationProgress(loading);
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

        var display = string.Equals(url, TrustedBrowserBridge.NewTabUrl, StringComparison.OrdinalIgnoreCase)
            || string.Equals(url, TrustedBrowserBridge.PrivateTabUrl, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : UrlHelper.ForDisplay(url);
        if (_omnibox.Input.Text != display)
        {
            _updatingOmnibox = true;
            try { _omnibox.Input.Text = display; }
            finally { _updatingOmnibox = false; }
        }
    }

    private string BuildTitle(string docTitle, bool isPrivate = false)
    {
        var title = string.IsNullOrWhiteSpace(docTitle) ? "CottonBrowser" : docTitle + " - CottonBrowser";
        return isPrivate ? "Anônima · " + title : title;
    }

    // --------------------------------------------------------- Atalhos -----

    private void OnWebKeyDown(object? sender, KeyEventArgs e)
    {
        var key = e.KeyCode;
        var ctrl = e.Control;
        var shift = e.Shift;
        var alt = e.Alt;
        var isShortcut = (ctrl && (key == Keys.L || key == Keys.R || key == Keys.T || key == Keys.W || key == Keys.Tab || key == Keys.D
            || (key == Keys.J && !shift && !alt) || key is Keys.Oemplus or Keys.Add or Keys.OemMinus or Keys.Subtract or Keys.D0 or Keys.NumPad0))
            || (ctrl && shift && (key == Keys.A || key == Keys.N))
            || (ctrl && shift && alt && _access.IsAdvancedMode && key == Keys.M)
            || (alt && (key == Keys.D || key == Keys.Left || key == Keys.Right || key == Keys.Home))
            || key is Keys.F5 or Keys.F11
            || (key == Keys.Escape && !ActivePageIsFullscreen() && (_browserFullscreen || _loading));

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
            case Keys.M when ctrl && shift && alt && _access.IsAdvancedMode:
                _ = OpenAdminDashboardAsync();
                return true;
            case Keys.N when ctrl && shift:
                _ = OpenNewTabAsync(isPrivate: true);
                return true;
            case Keys.T when ctrl:
                _ = OpenNewTabAsync(_tabs?.Active?.IsPrivate == true);
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
            case Keys.J when ctrl && !shift && !alt:
                OpenDownloads();
                return true;
            case Keys.Oemplus or Keys.Add when ctrl:
                ChangeZoom(1);
                return true;
            case Keys.OemMinus or Keys.Subtract when ctrl:
                ChangeZoom(-1);
                return true;
            case Keys.D0 or Keys.NumPad0 when ctrl:
                ChangeZoom(0);
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

            case Keys.F11:
                _browserFullscreen = !_browserFullscreen;
                SynchronizeFullscreen();
                return true;

            case Keys.Left when alt:
                if (_core?.CanGoBack == true) _core.GoBack();
                return true;

            case Keys.Right when alt:
                if (_core?.CanGoForward == true) _core.GoForward();
                return true;

            case Keys.Escape:
                if (ActivePageIsFullscreen()) return false;
                if (_browserFullscreen)
                {
                    _browserFullscreen = false;
                    SynchronizeFullscreen();
                    return true;
                }
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

        if (!_windowFullscreen && WindowState != FormWindowState.Minimized)
        {
            var desiredPadding = WindowState == FormWindowState.Maximized ? Padding.Empty : new Padding(ResizeBorder);
            if (!Padding.Equals(desiredPadding)) Padding = desiredPadding;
        }
        _maximizeButton.RestoreIcon = WindowState == FormWindowState.Maximized;
        if (WindowState != FormWindowState.Minimized && !_movingWindow)
            ScheduleScreenFit();

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

