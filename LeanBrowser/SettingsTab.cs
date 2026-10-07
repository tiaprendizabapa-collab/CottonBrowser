using System.Drawing.Drawing2D;
using System.Globalization;

namespace LeanBrowser;

/// <summary>Preferências com navegação por categoria, busca e controles nativos.</summary>
public sealed class SettingsTab : TabPage
{
    private readonly Action<bool> _setTheme;
    private readonly Action<Color> _setAccent;
    private readonly Action _clearPasswords;
    private readonly Func<IReadOnlyList<Bookmark>> _loadBookmarks;
    private readonly Action _addBookmark;
    private readonly Action<string> _removeBookmark;
    private readonly Panel _sidebar = new() { Dock = DockStyle.Left };
    private readonly Panel _workspace = new() { Dock = DockStyle.Fill };
    private readonly Panel _header = new() { Dock = DockStyle.Top };
    private readonly Panel _body = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly FlowLayoutPanel _nav = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    private readonly PictureBox _brand = new() { SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Label _brandTitle = Caption("Configurações", 12f, true);
    private readonly Label _sidebarHint = Caption("SUAS PREFERÊNCIAS", 8f, muted: true);
    private readonly Label _footer = Caption("Personalize sua navegação.", 8.5f, muted: true);
    private readonly SearchSurface _search = new();
    private readonly TextBox _searchInput = new() { BorderStyle = BorderStyle.None, PlaceholderText = "Pesquisar nas configurações", Font = new Font(Theme.UiFont, 10f), AccessibleName = "Pesquisar nas configurações" };
    private readonly SettingsButton _clearSearch = new("", "\uE711") { AccessibleName = "Limpar pesquisa", Visible = false };
    private readonly Label _title = Caption("Geral", 25f, true);
    private readonly Label _subtitle = Caption("Tudo o que você precisa para deixar o navegador do seu jeito.", 10f, muted: true);
    private readonly Label _noResults = Caption("Nenhuma configuração encontrada. Tente outra palavra.", 10f, muted: true);
    private readonly List<SettingsCard> _cards = new();
    private readonly Dictionary<SettingsCard, (Control View, int Height)> _featureCards = new();
    private readonly Dictionary<string, SettingsButton> _navigation = new();
    private readonly ToolTip _navigationTips = new();
    private readonly ThemeChoice _light = new(false);
    private readonly ThemeChoice _dark = new(true);
    private readonly List<ColorChoice> _colors = new();
    private readonly Label _accentValue = Caption("", 9f, muted: true);
    private readonly SettingsButton _accentButton = new("Escolher cor…", "\uE790");
    private readonly ListBox _favorites = new() { BorderStyle = BorderStyle.None, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, AccessibleName = "Sites favoritos" };
    private readonly Label _emptyFavorites = Caption("Seus sites favoritos aparecem aqui.\nUse a estrela na barra de endereço para salvar uma página.", 10f, muted: true);
    private readonly SettingsButton _refresh = new("Atualizar", "\uE72C");
    private readonly SettingsButton _add = new("Salvar página anterior", "\uE734");
    private readonly SettingsButton _open = new("Abrir", "\uE8A7");
    private readonly SettingsButton _remove = new("Remover", "\uE74D");
    private readonly SettingsButton _clear = new("Apagar senhas salvas…", "\uE74D");
    private readonly SettingsCard _themeCard;
    private readonly SettingsCard _accentCard;
    private readonly SettingsCard _favoritesCard;
    private readonly SettingsCard _historyCard;
    private HistoryView? _historyView;
    private BookmarkManagerView? _bookmarkManager;
    private readonly SettingsCard _newTabCard;
    private readonly NewTabCustomizationView _newTabCustomization = new();
    private readonly SettingsCard? _passwordCard;
    private readonly SettingsCard _sessionCard;
    private readonly SettingsCard _performanceCard;
    private readonly SettingsCard _searchEngineCard;
    private readonly SettingsCard _aboutCard;
    private readonly AboutUpdatesView _aboutUpdates = new();
    private readonly CheckBox _restoreSession = new() { Text = "Continuar de onde parei", AccessibleName = "Restaurar abas ao iniciar", AutoSize = false };
    private readonly CheckBox _memorySaver = new() { Text = "Ativar economia de memória", AccessibleName = "Ativar economia de memória", AutoSize = false };
    private readonly NumericUpDown _suspendDelay = new() { Minimum = 1, Maximum = 240, AccessibleName = "Minutos antes de suspender abas inativas" };
    private readonly Label _suspendLabel = Caption("Suspender abas inativas após", 10f);
    private readonly Label _minutesLabel = Caption("minutos", 10f, muted: true);
    private readonly Label _exceptionsLabel = Caption("Manter estes sites sempre ativos (um por linha):", 10f);
    private readonly TextBox _memoryExceptions = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, PlaceholderText = "exemplo.com\ntrabalho.exemplo.com", AccessibleName = "Sites que não devem ser suspensos" };
    private readonly SettingsButton _saveExceptions = new("Salvar exceções", "\uE74E");
    private readonly Label _memoryNote = Caption("Áudio, chamadas, downloads e páginas com alterações pendentes são preservados. Exceções também incluem subdomínios.", 9.5f, muted: true);
    private readonly Label _sessionNote = Caption("As abas normais são recuperadas ao abrir o navegador, inclusive após um encerramento inesperado. Abas anônimas não são salvas.", 10f, muted: true);
    private readonly ComboBox _searchEngine = new() { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Mecanismo de pesquisa padrão" };
    private readonly Label _searchEngineNote = Caption("Escolha o serviço usado ao pesquisar pela barra de endereço.", 10f, muted: true);
    private bool _updatingPreferences;
    private bool _exceptionsDirty;
    private string _section = "Geral";
    private bool _layoutRunning;
    public event Action<string>? FavoriteSelected;
    public event Action? ProfileRequested;
    public event Action? BrowserCenterRequested;
    public event Action<string>? HistoryEntrySelected;
    public event Action? UpdateCheckRequested;
    public event Action? UpdateInstallRequested;

    public SettingsTab(Action<bool> setTheme, Action clearPasswords, Func<IReadOnlyList<Bookmark>> loadBookmarks,
        Action addBookmark, Action<string> removeBookmark, bool isAdmin, Action<Color> setAccent, string? userName = null) : base("Configurações")
    {
        _setTheme = setTheme; _setAccent = setAccent; _clearPasswords = clearPasswords;
        _loadBookmarks = loadBookmarks; _addBookmark = addBookmark; _removeBookmark = removeBookmark;
        Font = new Font(Theme.UiFont, 9.5f); Padding = Padding.Empty;
        Controls.Add(_workspace); Controls.Add(_sidebar);
        _workspace.Controls.Add(_body); _workspace.Controls.Add(_header);
        _sidebar.Controls.AddRange(new Control[] { _brand, _brandTitle, _sidebarHint, _nav, _footer });
        _brand.Image = Draw.LoadCottonIcon();
        _search.Controls.Add(_searchInput); _search.Controls.Add(_clearSearch);
        _header.Controls.AddRange(new Control[] { _search, _title, _subtitle });
        _body.Controls.Add(_noResults);
        AddNavigation("Geral", "\uE80F"); AddNavigation("Aparência", "\uE790"); AddNavigation("Favoritos", "\uE734");
        AddNavigation("Histórico", "\uE81C");
        AddNavigation("Nova guia", "\uE710");
        AddNavigation("Inicialização", "\uE777"); AddNavigation("Desempenho", "\uE945"); AddNavigation("Pesquisa", "\uE721");
        if (isAdmin) AddNavigation("Senhas salvas", "\uE72E");
        AddNavigation("Privacidade e proteção", "\uEA18");
        AddNavigation("Sobre e atualizações", "\uE946");

        var profile = AddCard("Geral", "Seu perfil", "Conta e permissões desta sessão.");
        profile.AddRow((userName ?? Environment.UserName).Split('\\').Last(), isAdmin ? "Modo avançado · Configurações adicionais disponíveis" : "Modo padrão", "\uE77B", () => ProfileRequested?.Invoke());
        var shortcuts = AddCard("Geral", "Preferências do navegador", "Encontre rapidamente o que deseja ajustar.", searchable: false);
        shortcuts.AddRow("Aparência", "Tema claro ou escuro e sua cor de destaque", "\uE790", () => SelectSection("Aparência"));
        shortcuts.AddRow("Favoritos", "Organize os sites que você acessa mais", "\uE734", ShowBookmarks);
        shortcuts.AddRow("Histórico", "Consulte e gerencie as páginas visitadas", "\uE81C", () => SelectSection("Histórico"));
        shortcuts.AddRow("Nova guia", "Escolha o fundo e seus atalhos", "\uE710", ShowNewTab);
        shortcuts.AddRow("Inicialização", "Recupere as abas da última sessão", "\uE777", () => SelectSection("Inicialização"));
        shortcuts.AddRow("Desempenho", "Economia de memória e sites que ficam ativos", "\uE945", () => SelectSection("Desempenho"));
        shortcuts.AddRow("Pesquisa", "Escolha Google, Bing ou DuckDuckGo", "\uE721", () => SelectSection("Pesquisa"));
        if (isAdmin) shortcuts.AddRow("Senhas salvas", "Gerencie as credenciais deste perfil", "\uE72E", () => SelectSection("Senhas salvas"));
        shortcuts.AddRow("Privacidade e proteção", "Acesse as ferramentas de segurança", "\uEA18", () => SelectSection("Privacidade e proteção"));
        shortcuts.AddRow("Sobre e atualizações", "Confira a versão e as novidades do CottonBrowser", "\uE946", ShowAboutUpdates);

        _aboutCard = AddCard("Sobre e atualizações", "CottonBrowser", "Versão instalada, atualizações e novidades do navegador.", "sobre versao versão atualizacao atualização atualizar novidades release");
        _aboutCard.Controls.Add(_aboutUpdates);
        _aboutUpdates.CheckRequested += () => UpdateCheckRequested?.Invoke();
        _aboutUpdates.InstallRequested += () => UpdateInstallRequested?.Invoke();

        _historyCard = AddCard("Histórico", "Histórico de navegação", "Pesquise páginas visitadas ou apague visitas e períodos.", "historico histórico visitas páginas apagar excluir");
        _newTabCard = AddCard("Nova guia", "Personalizar a nova guia", "Escolha o fundo, os elementos visíveis e até 12 atalhos.", "nova guia fundo atalhos personalizar relógio saudacao");
        _newTabCard.Controls.Add(_newTabCustomization);
        _sessionCard = AddCard("Inicialização", "Ao abrir o navegador", "Suas abas prontas para continuar a navegação.", "sessao sessão restaurar recuperar continuar iniciar abas");
        _sessionCard.Controls.AddRange(new Control[] { _restoreSession, _sessionNote });
        _restoreSession.CheckedChanged += (_, _) =>
        {
            if (_updatingPreferences) return;
            BrowserPreferences.Current.RestoreSession = _restoreSession.Checked; SavePreferences();
        };
        _performanceCard = AddCard("Desempenho", "Economia de memória", "Libere recursos ao deixar abas sem uso em repouso.", "memoria memória desempenho suspender inativas minutos exceções");
        _performanceCard.Controls.AddRange(new Control[] { _memorySaver, _suspendLabel, _suspendDelay, _minutesLabel,
            _exceptionsLabel, _memoryExceptions, _saveExceptions, _memoryNote });
        _memorySaver.CheckedChanged += (_, _) =>
        {
            _suspendDelay.Enabled = _memorySaver.Checked;
            if (_updatingPreferences) return;
            BrowserPreferences.Current.MemorySaverEnabled = _memorySaver.Checked; SavePreferences();
        };
        _suspendDelay.ValueChanged += (_, _) =>
        {
            if (_updatingPreferences) return;
            BrowserPreferences.Current.SuspendAfterMinutes = (int)_suspendDelay.Value; SavePreferences();
        };
        _memoryExceptions.TextChanged += (_, _) => { if (!_updatingPreferences) _exceptionsDirty = true; };
        _saveExceptions.Click += (_, _) => SaveMemoryExceptions();
        _searchEngineCard = AddCard("Pesquisa", "Mecanismo de pesquisa", "Pesquise com o serviço de sua preferência.", "pesquisa buscador google bing duckduckgo mecanismo padrão");
        _searchEngine.Items.AddRange(new object[] { "Google", "Bing", "DuckDuckGo" });
        _searchEngineCard.Controls.AddRange(new Control[] { _searchEngine, _searchEngineNote });
        _searchEngine.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingPreferences || _searchEngine.SelectedItem is not string engine) return;
            BrowserPreferences.Current.SearchEngine = engine; SavePreferences();
        };
        BrowserPreferences.Changed += PreferencesChanged;
        ReloadPreferences();

        _themeCard = AddCard("Aparência", "Tema do navegador", "Escolha a aparência das abas, menus e páginas internas.", "tema claro escuro visual");
        _themeCard.Controls.AddRange(new Control[] { _light, _dark });
        _light.Click += (_, _) => _setTheme(false); _dark.Click += (_, _) => _setTheme(true);
        _accentCard = AddCard("Aparência", "Cor de destaque", "Dê um toque pessoal aos indicadores e controles do navegador.", "cor destaque personalizar");
        foreach (var hex in new[] { "#8839EF", "#0078D4", "#008272", "#2F855A", "#C45B19", "#BC2F6F" })
        {
            var choice = new ColorChoice(ColorTranslator.FromHtml(hex));
            choice.Click += (_, _) => _setAccent(choice.Color); _colors.Add(choice); _accentCard.Controls.Add(choice);
        }
        _accentCard.Controls.AddRange(new Control[] { _accentButton, _accentValue });
        _accentButton.Click += (_, _) =>
        {
            using var picker = new ColorDialog { Color = Theme.CustomAccent ?? Theme.Accent, FullOpen = true };
            if (picker.ShowDialog(this) == DialogResult.OK) _setAccent(picker.Color);
        };
        _favoritesCard = AddCard("Favoritos", "Seus sites favoritos", "Um atalho para os lugares que você quer ter sempre à mão.", "favoritos sites salvar estrela");
        _favoritesCard.Controls.AddRange(new Control[] { _favorites, _emptyFavorites, _add, _open, _remove, _refresh });
        _favorites.DrawItem += DrawFavorite;
        _favorites.SelectedIndexChanged += (_, _) => UpdateBookmarkActions();
        _favorites.DoubleClick += (_, _) => OpenSelectedBookmark();
        _favorites.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { OpenSelectedBookmark(); e.Handled = true; } };
        _refresh.Click += (_, _) => ReloadBookmarks();
        _add.Click += (_, _) => { _addBookmark(); ReloadBookmarks(); };
        _open.Click += (_, _) => OpenSelectedBookmark();
        _remove.Click += (_, _) => { if (_favorites.SelectedItem is BookmarkItem item) { _removeBookmark(item.Url); ReloadBookmarks(); } };
        if (isAdmin)
        {
            _passwordCard = AddCard("Senhas salvas", "Senhas e preenchimento", "As credenciais deste perfil são protegidas pelo Windows.", "senhas preenchimento automatico credenciais apagar");
            var note = Caption("O navegador oferece as senhas salvas nos campos de login dos sites.\nAo apagar, essas credenciais deixam de estar disponíveis para preenchimento.", 10f, muted: true);
            note.Name = "passwordNote"; _passwordCard.Controls.AddRange(new Control[] { note, _clear });
            _clear.Click += (_, _) => _clearPasswords();
        }
        var privacy = AddCard("Privacidade e proteção", "Ferramentas de privacidade", "Controle sua experiência e consulte as opções de proteção.", "privacidade seguranca protecao anuncios bloqueador filtros");
        privacy.AddRow("Central do navegador", "Abra as ferramentas e informações de segurança", "\uEA18", () => BrowserCenterRequested?.Invoke());
        privacy.AddRow("Perfil e permissões", "Confira o acesso disponível para sua sessão", "\uE77B", () => ProfileRequested?.Invoke());

        _searchInput.TextChanged += (_, _) => { _body.AutoScrollPosition = Point.Empty; UpdateVisibleCards(); };
        _searchInput.Enter += (_, _) => { _search.FocusedInput = true; _search.Invalidate(); };
        _searchInput.Leave += (_, _) => { _search.FocusedInput = false; _search.Invalidate(); };
        _searchInput.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { _searchInput.Clear(); e.SuppressKeyPress = true; } };
        _clearSearch.Click += (_, _) => { _searchInput.Clear(); _searchInput.Focus(); };
        Resize += (_, _) => LayoutPage(); _body.ClientSizeChanged += (_, _) => LayoutPage(); DpiChangedAfterParent += (_, _) => LayoutPage();
        _sidebar.Paint += (_, e) => { using var pen = new Pen(Theme.Divider); e.Graphics.DrawLine(pen, _sidebar.Width - 1, 0, _sidebar.Width - 1, _sidebar.Height); };
        ApplyTheme(); ReloadBookmarks(); UpdateVisibleCards();
    }

    public void ReloadBookmarks()
    {
        _bookmarkManager?.Reload();
        if (_bookmarkManager is not null)
        {
            foreach (var control in new Control[] { _favorites, _emptyFavorites, _refresh, _add, _open, _remove }) control.Visible = false;
            LayoutPage(); return;
        }
        var selectedUrl = (_favorites.SelectedItem as BookmarkItem)?.Url;
        _favorites.BeginUpdate();
        try
        {
            _favorites.Items.Clear();
            foreach (var bookmark in _loadBookmarks()) _favorites.Items.Add(new BookmarkItem(bookmark.Title, bookmark.Url));
            for (var i = 0; i < _favorites.Items.Count; i++) if (((BookmarkItem)_favorites.Items[i]).Url == selectedUrl) _favorites.SelectedIndex = i;
        }
        finally { _favorites.EndUpdate(); }
        _emptyFavorites.Visible = _favorites.Items.Count == 0; _favorites.Visible = _favorites.Items.Count != 0;
        UpdateBookmarkActions(); LayoutPage();
    }

    public void ShowBookmarks() { SelectSection("Favoritos"); if (_favorites.Items.Count != 0) _favorites.Focus(); }
    public void ShowAboutUpdates() => SelectSection("Sobre e atualizações");

    internal void ConfigureUpdates(Version installed, UpdateCheckHistory? history, BrowserUpdate? available,
        bool busy, string? activity)
    {
        _aboutUpdates.SetState(installed, history, available, busy, activity);
        LayoutPage();
    }

    public void ShowHistory() { SelectSection("Histórico"); _historyView?.FocusSearch(); }
    public void ShowNewTab() => SelectSection("Nova guia");
    internal void ShowFeature(string section) => SelectSection(section);
    internal void AddFeatureSection(string section, string title, string description, Control view, int height = 440)
    {
        if (!_navigation.ContainsKey(section))
        {
            AddNavigation(section, section switch
            {
                "Espaços de trabalho" => "\uE8FD", "Lista de leitura" => "\uE736", "Downloads" => "\uE896",
                "Atalhos" => "\uE765", "Backup e restauração" => "\uE74E", _ => "\uE713"
            });
            if (_navigation.TryGetValue("Sobre e atualizações", out var about))
                _nav.Controls.SetChildIndex(_navigation[section], _nav.Controls.GetChildIndex(about));
        }
        var card = AddCard(section, title, description);
        card.Controls.Add(view); _featureCards.Add(card, (view, height));
        UpdateVisibleCards(); ApplyTheme();
    }
    internal void ConfigureBookmarks(BookmarkStore store, Action changed)
    {
        _bookmarkManager?.Dispose();
        _bookmarkManager = new BookmarkManagerView(store);
        _bookmarkManager.OpenRequested += url => FavoriteSelected?.Invoke(url);
        _bookmarkManager.Changed += changed;
        _favoritesCard.Controls.Add(_bookmarkManager);
        foreach (var control in new Control[] { _favorites, _emptyFavorites, _refresh, _add, _open, _remove }) control.Visible = false;
        LayoutPage();
    }

    internal void ConfigureHistory(NavigationHistoryStore store)
    {
        _historyView?.Dispose();
        _historyView = new HistoryView(store, embedded: true);
        _historyView.OpenRequested += url => HistoryEntrySelected?.Invoke(url);
        _historyCard.Controls.Add(_historyView);
        LayoutPage();
    }

    private void PreferencesChanged()
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            if (!IsHandleCreated) return;
            try { BeginInvoke((Action)ReloadPreferences); } catch (InvalidOperationException) { }
        }
        else ReloadPreferences();
    }

    private void ReloadPreferences()
    {
        if (IsDisposed) return;
        _updatingPreferences = true;
        try
        {
            var preferences = BrowserPreferences.Current;
            _restoreSession.Checked = preferences.RestoreSession;
            _memorySaver.Checked = preferences.MemorySaverEnabled;
            _suspendDelay.Value = Math.Clamp(preferences.SuspendAfterMinutes, 1, 240);
            _suspendDelay.Enabled = preferences.MemorySaverEnabled;
            _searchEngine.SelectedItem = preferences.SearchEngine;
            if (!_exceptionsDirty) _memoryExceptions.Text = string.Join(Environment.NewLine, preferences.MemorySaverExceptions);
        }
        finally { _updatingPreferences = false; }
    }

    private void SavePreferences()
    {
        if (!BrowserPreferences.Current.Save())
            MessageBox.Show(this, "O ajuste vale nesta sessão, mas não foi possível salvar suas preferências no computador.",
                "Salvar preferências", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SaveMemoryExceptions()
    {
        var values = _memoryExceptions.Text.Split(new[] { '\r', '\n', ';', ',' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var hosts = new List<string>();
        foreach (var value in values)
        {
            if (!BrowserPreferences.TryNormalizeExceptionHost(value, out var host))
            {
                MessageBox.Show(this, $"Não reconheci o site “{value}”. Use um domínio como exemplo.com ou o endereço completo do site.",
                    "Exceções de memória", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            hosts.Add(host);
        }
        if (hosts.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 200)
        {
            MessageBox.Show(this, "Adicione no máximo 200 sites às exceções.", "Exceções de memória", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        BrowserPreferences.Current.MemorySaverExceptions = hosts.ToArray();
        _exceptionsDirty = false;
        SavePreferences();
    }


    public void ApplyTheme()
    {
        BackColor = Theme.Chrome;
        foreach (var panel in new[] { _sidebar, _workspace, _header, _body, _nav }) panel.BackColor = Theme.Chrome;
        foreach (var label in new[] { _brandTitle, _sidebarHint, _footer, _title, _subtitle, _noResults }) label.ForeColor = label.Tag as string == "muted" ? Theme.InkMuted : Theme.Ink;
        _brand.BackColor = _search.BackColor = Theme.Chrome; _searchInput.BackColor = _clearSearch.BackColor = Theme.Surface; _searchInput.ForeColor = Theme.Ink;
        foreach (var card in _cards) card.ApplyTheme();
        foreach (var feature in _featureCards.Values) FeatureUi.ApplyTheme(feature.View);
        _historyView?.ApplyTheme();
        _aboutUpdates.ApplyTheme();
        _bookmarkManager?.ApplyTheme(); _newTabCustomization.ApplyTheme();
        foreach (var button in _navigation.Values) { button.BackColor = Theme.Chrome; button.Invalidate(); }
        _favorites.BackColor = Theme.Surface; _favorites.ForeColor = Theme.Ink;
        foreach (var control in new Control[] { _memoryExceptions, _suspendDelay, _searchEngine }) { control.BackColor = Theme.Surface; control.ForeColor = Theme.Ink; }
        foreach (var control in new Control[] { _restoreSession, _memorySaver }) { control.BackColor = Theme.Surface; control.ForeColor = Theme.Ink; }
        _light.Selected = !Theme.IsDark; _dark.Selected = Theme.IsDark;
        var selected = Theme.CustomAccent ?? Theme.Accent;
        foreach (var color in _colors) { color.Selected = selected.ToArgb() == color.Color.ToArgb(); color.Invalidate(); }
        _accentValue.Text = "Cor atual  " + ColorTranslator.ToHtml(selected).ToUpperInvariant();
        _light.Invalidate(); _dark.Invalidate(); Invalidate(true);
    }

    private void AddNavigation(string name, string glyph)
    {
        var button = new SettingsButton(name, glyph) { Navigation = true, Margin = new Padding(0, 0, 0, 6) };
        button.Click += (_, _) => SelectSection(name);
        _navigation.Add(name, button); _nav.Controls.Add(button); _navigationTips.SetToolTip(button, name);
    }
    private SettingsCard AddCard(string section, string title, string description, string keywords = "", bool searchable = true)
    {
        var card = new SettingsCard(section, title, description, keywords, searchable);
        _cards.Add(card); _body.Controls.Add(card); return card;
    }
    private void SelectSection(string section)
    {
        if (!_navigation.ContainsKey(section)) return;
        _section = section; _searchInput.Clear(); _body.AutoScrollPosition = Point.Empty; UpdateVisibleCards();
    }
    private void UpdateVisibleCards()
    {
        var query = _searchInput.Text.Trim(); var searching = query.Length != 0;
        _clearSearch.Visible = searching; _title.Text = searching ? "Resultados da pesquisa" : _section;
        _subtitle.Text = searching ? $"Configurações relacionadas a “{query}”." : _section switch
        {
            "Aparência" => "Um visual que combina com você.", "Favoritos" => "Organize seus destinos preferidos.",
            "Histórico" => "Consulte e gerencie sua navegação sem sair das configurações.",
            "Sobre e atualizações" => "Confira sua versão e acompanhe as novidades do CottonBrowser.",
            "Nova guia" => "Deixe a página inicial com a sua cara.",
            "Espaços de trabalho" => "Organize conjuntos de abas para cada atividade.",
            "Lista de leitura" => "Guarde páginas e anotações para consultar depois.",
            "Downloads" => "Escolha onde seus arquivos serão salvos.",
            "Atalhos" => "Use o teclado do seu jeito.",
            "Backup e restauração" => "Preserve suas preferências e organização.",
            "Senhas salvas" => "Cuide das credenciais armazenadas neste perfil.", "Privacidade e proteção" => "Sua navegação, com mais controle.",
            "Inicialização" => "Continue sua navegação ao abrir o CottonBrowser.", "Desempenho" => "Ajuste o uso de recursos das abas.", "Pesquisa" => "Encontre o que precisa com seu buscador preferido.",
            _ => "Tudo o que você precisa para deixar o navegador do seu jeito."
        };
        foreach (var card in _cards)
        {
            card.Displayed = searching ? card.Searchable && card.Matches(query) : card.Section == _section;
            card.Visible = card.Displayed;
        }
        foreach (var pair in _navigation) { pair.Value.Selected = !searching && pair.Key == _section; pair.Value.Invalidate(); }
        _noResults.Visible = !_cards.Any(card => card.Displayed); LayoutPage();
    }
    private void UpdateBookmarkActions() { _remove.Enabled = _open.Enabled = _favorites.SelectedItem is BookmarkItem; }
    private void OpenSelectedBookmark() { if (_favorites.SelectedItem is BookmarkItem item) FavoriteSelected?.Invoke(item.Url); }
    private int D(int value) => (int)Math.Round(value * DeviceDpi / 96f);

    private void LayoutPage()
    {
        if (_layoutRunning || _themeCard is null || ClientSize.Width < 1) return;
        _layoutRunning = true;
        try
        {
            var compact = ClientSize.Width < D(720);
            _sidebar.Width = D(compact ? 76 : ClientSize.Width >= D(1000) ? 232 : 204);
            _header.Height = D(155);
            PerformLayout(); _workspace.PerformLayout(); _body.SuspendLayout();
            _brand.SetBounds(D(24), D(29), D(30), D(30));
            _brandTitle.SetBounds(D(66), D(30), Math.Max(1, _sidebar.Width - D(80)), D(28));
            _brandTitle.Visible = _sidebarHint.Visible = !compact;
            _sidebarHint.SetBounds(D(24), D(93), _sidebar.Width - D(40), D(20));
            _nav.SetBounds(D(12), D(compact ? 88 : 125), _sidebar.Width - D(24), Math.Max(D(60), _sidebar.Height - D(compact ? 104 : 205)));
            foreach (var button in _navigation.Values) button.Size = new Size(Math.Max(1, _nav.Width - SystemInformation.VerticalScrollBarWidth - D(8)), D(44));
            _footer.SetBounds(D(24), Math.Max(D(375), _sidebar.Height - D(55)), _sidebar.Width - D(36), D(38)); _footer.Visible = !compact && _sidebar.Height >= D(460);
            var available = Math.Max(1, _workspace.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            var width = Math.Min(D(840), Math.Max(1, available - D(56))); var left = Math.Max(D(20), (available - width) / 2);
            _search.SetBounds(left, D(26), width, D(44));
            _searchInput.SetBounds(D(44), D(12), Math.Max(1, width - D(86)), D(24));
            _clearSearch.SetBounds(width - D(38), D(6), D(32), D(32));
            _title.SetBounds(left, D(88), width, D(39)); _subtitle.SetBounds(left + D(1), D(128), width, D(24));
            var scroll = -_body.AutoScrollPosition.Y; _body.AutoScrollPosition = Point.Empty; var y = D(18);
            foreach (var card in _cards)
            {
                // O painel considera os limites anteriores de cartões ocultos ao calcular a rolagem.
                if (!card.Displayed) { card.SetBounds(0, 0, 0, 0); continue; }
                var height = CardHeight(card, width); card.SetBounds(left, y, width, height); y += height + D(20);
            }
            _noResults.SetBounds(left, D(40), width, D(60)); _body.AutoScrollMinSize = new Size(0, y + D(24)); _body.AutoScrollPosition = new Point(0, scroll);
        }
        finally { _body.ResumeLayout(); _layoutRunning = false; }
    }
    private int CardHeight(SettingsCard card, int width)
    {
        if (_featureCards.TryGetValue(card, out var feature))
        {
            var height = D(feature.Height);
            feature.View.SetBounds(D(24), D(96), Math.Max(1, width - D(48)), height);
            return height + D(120);
        }
        if (ReferenceEquals(card, _aboutCard))
        {
            var contentWidth = Math.Max(1, width - D(48));
            var height = _aboutUpdates.HeightForWidth(contentWidth);
            _aboutUpdates.SetBounds(D(24), D(96), contentWidth, height);
            return _aboutUpdates.Bottom + D(24);
        }
        if (ReferenceEquals(card, _newTabCard) || (ReferenceEquals(card, _favoritesCard) && _bookmarkManager is not null))
        {
            var height = Math.Max(D(width < D(600) ? 620 : 490), _body.ClientSize.Height - D(62));
            var view = ReferenceEquals(card, _newTabCard) ? (Control)_newTabCustomization : _bookmarkManager!;
            view.SetBounds(D(24), D(96), Math.Max(1, width - D(48)), height - D(120));
            return height;
        }
        if (ReferenceEquals(card, _historyCard))
        {
            var height = Math.Max(D(width < D(600) ? 560 : 440), _body.ClientSize.Height - D(62));
            _historyView?.SetBounds(D(24), D(96), Math.Max(1, width - D(48)), height - D(120));
            return height;
        }
        if (ReferenceEquals(card, _sessionCard))
        {
            _restoreSession.SetBounds(D(24), D(96), Math.Max(1, width - D(48)), D(32));
            _sessionNote.SetBounds(D(24), D(141), Math.Max(1, width - D(48)), D(width < D(450) ? 105 : 72));
            return _sessionNote.Bottom + D(24);
        }
        if (ReferenceEquals(card, _searchEngineCard))
        {
            _searchEngine.SetBounds(D(24), D(97), Math.Min(D(290), Math.Max(1, width - D(48))), D(32));
            _searchEngineNote.SetBounds(D(24), D(144), Math.Max(1, width - D(48)), D(66)); return D(231);
        }
        if (ReferenceEquals(card, _performanceCard))
        {
            var narrow = width < D(470);
            _memorySaver.SetBounds(D(24), D(96), Math.Max(1, width - D(48)), D(32));
            _suspendLabel.SetBounds(D(24), D(146), Math.Max(1, width - D(48)), D(30));
            _suspendDelay.SetBounds(narrow ? D(24) : D(287), narrow ? D(181) : D(143), D(66), D(32));
            _minutesLabel.SetBounds(narrow ? D(100) : D(363), narrow ? D(184) : D(146), D(78), D(28));
            var y = narrow ? D(230) : D(193);
            _exceptionsLabel.SetBounds(D(24), y, Math.Max(1, width - D(48)), D(50));
            _memoryExceptions.SetBounds(D(24), y + D(51), Math.Max(1, width - D(48)), D(112));
            _saveExceptions.SetBounds(D(24), y + D(175), Math.Min(D(175), Math.Max(1, width - D(48))), D(36));
            _memoryNote.SetBounds(D(24), y + D(226), Math.Max(1, width - D(48)), D(narrow ? 105 : 70));
            return _memoryNote.Bottom + D(25);
        }
        if (ReferenceEquals(card, _themeCard))
        {
            var tile = Math.Max(1, (width - D(60)) / 2); _light.SetBounds(D(22), D(92), tile, D(140)); _dark.SetBounds(D(38) + tile, D(92), tile, D(140)); return D(254);
        }
        if (ReferenceEquals(card, _accentCard))
        {
            var spacing = width < D(470) ? 42 : 48;
            for (var i = 0; i < _colors.Count; i++) _colors[i].SetBounds(D(20 + i * spacing), D(92), D(38), D(38));
            _accentButton.SetBounds(D(22), D(148), D(180), D(36)); _accentValue.SetBounds(D(218), D(157), Math.Max(1, width - D(238)), D(24)); return D(205);
        }
        if (ReferenceEquals(card, _favoritesCard))
        {
            var height = _favorites.Items.Count == 0 ? D(96) : D(Math.Clamp(_favorites.Items.Count, 2, 5) * 62);
            _favorites.ItemHeight = D(62); _favorites.SetBounds(D(12), D(91), Math.Max(1, width - D(24)), height);
            _emptyFavorites.SetBounds(D(24), D(108), Math.Max(1, width - D(48)), D(78));
            var y = D(103) + height; var narrow = width < D(580);
            _add.SetBounds(D(22), y, D(190), D(36)); _refresh.SetBounds(Math.Max(D(226), width - D(142)), y, D(120), D(36));
            _open.SetBounds(narrow ? D(22) : D(224), narrow ? y + D(46) : y, D(86), D(36));
            _remove.SetBounds(narrow ? D(120) : D(322), narrow ? y + D(46) : y, D(110), D(36)); return y + D(narrow ? 102 : 57);
        }
        if (ReferenceEquals(card, _passwordCard))
        {
            card.Controls["passwordNote"]!.SetBounds(D(24), D(96), width - D(48), D(78)); _clear.SetBounds(D(22), D(184), D(222), D(36)); return D(242);
        }
        return D(92 + card.Rows.Count * 72 + 12);
    }
    private void DrawFavorite(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _favorites.Items.Count) return;
        var item = (BookmarkItem)_favorites.Items[e.Index]; var selected = e.State.HasFlag(DrawItemState.Selected);
        var color = selected ? Mix(Theme.Surface, Theme.Accent, .12f) : Theme.Surface;
        using var background = new SolidBrush(color); e.Graphics.FillRectangle(background, e.Bounds);
        var icon = new Rectangle(e.Bounds.X + D(14), e.Bounds.Y + D(13), D(34), D(34));
        using var path = Draw.RoundedRect(icon, D(10)); using var badge = new SolidBrush(Mix(Theme.Surface, Theme.Accent, .16f)); e.Graphics.FillPath(badge, path);
        TextRenderer.DrawText(e.Graphics, string.IsNullOrWhiteSpace(item.Title) ? "☆" : item.Title[..1].ToUpperInvariant(), Font, icon, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        var x = e.Bounds.X + D(62); var width = Math.Max(1, e.Bounds.Width - D(80));
        TextRenderer.DrawText(e.Graphics, item.Title, Font, new Rectangle(x, e.Bounds.Y + D(9), width, D(24)), Theme.Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        using var small = new Font(Theme.UiFont, 8.5f);
        TextRenderer.DrawText(e.Graphics, item.Url, small, new Rectangle(x, e.Bounds.Y + D(34), width, D(19)), Theme.InkMuted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        if (e.State.HasFlag(DrawItemState.Focus)) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -D(3), -D(3)), Theme.Ink, color);
    }
    private static Label Caption(string text, float size, bool bold = false, bool muted = false) => new()
    {
        Text = text, Font = new Font(Theme.UiFont, size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = muted ? Theme.InkMuted : Theme.Ink, BackColor = Color.Transparent, Tag = muted ? "muted" : null, AutoEllipsis = true
    };
    private static Color Mix(Color a, Color b, float t) => Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    protected override void Dispose(bool disposing) { if (disposing) { BrowserPreferences.Changed -= PreferencesChanged; _brand.Image?.Dispose(); _navigationTips.Dispose(); } base.Dispose(disposing); }
    private sealed record BookmarkItem(string Title, string Url);

    private sealed class SearchSurface : Panel
    {
        public bool FocusedInput { get; set; }
        public SearchSurface() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; var s = DeviceDpi / 96f;
            using var path = Draw.RoundedRect(new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3)), (int)(12 * s));
            using var fill = new SolidBrush(Theme.Surface); using var pen = new Pen(FocusedInput ? Theme.Accent : Theme.Divider, FocusedInput ? 1.5f : 1f);
            e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(pen, path);
            using var font = new Font(Theme.IconFont, 12f);
            TextRenderer.DrawText(e.Graphics, "\uE721", font, new Rectangle((int)(13 * s), 0, (int)(24 * s), Height), Theme.InkMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }
    }
    private class SettingsButton : Button
    {
        private bool _hot;
        public string Glyph { get; }
        public bool Navigation { get; init; }
        public bool Selected { get; set; }
        public SettingsButton(string text, string glyph)
        {
            Text = text; Glyph = glyph; AccessibleName = text; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand; Font = new Font(Theme.UiFont, 9f); UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias; var s = DeviceDpi / 96f;
            using var path = Draw.RoundedRect(new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3)), (int)(9 * s));
            using var fill = new SolidBrush(Selected ? Mix(Theme.Chrome, Theme.Accent, .15f) : _hot && Enabled ? Mix(Theme.Surface, Theme.Accent, .08f) : Navigation ? Theme.Chrome : Theme.Surface); g.FillPath(fill, path);
            if (!Navigation) { using var pen = new Pen(Theme.Divider); g.DrawPath(pen, path); }
            if (Selected) { using var rail = new SolidBrush(Theme.Accent); g.FillRectangle(rail, (int)(3 * s), (int)(12 * s), Math.Max(2, (int)(3 * s)), Height - (int)(24 * s)); }
            var ink = !Enabled ? Theme.InkDisabled : Selected ? Theme.Accent : Navigation ? Theme.InkMuted : Theme.Ink;
            if (Navigation && Width < (int)(120 * s))
            {
                using var compactIcons = new Font(Theme.IconFont, 13f);
                TextRenderer.DrawText(g, Glyph, compactIcons, ClientRectangle, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (Focused && ShowFocusCues) { using var focus = new Pen(Theme.Accent); g.DrawPath(focus, path); }
                return;
            }
            var iconWidth = string.IsNullOrEmpty(Glyph) ? 0 : (int)(29 * s);
            if (iconWidth > 0) { using var icons = new Font(Theme.IconFont, 11f); TextRenderer.DrawText(g, Glyph, icons, new Rectangle((int)(10 * s), 0, iconWidth, Height), ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
            var x = iconWidth + (int)(12 * s);
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | (Navigation ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Math.Max(1, Width - x - (int)(10 * s)), Height), ink, flags);
            if (Focused && ShowFocusCues) { using var focus = new Pen(Theme.Accent); g.DrawPath(focus, path); }
        }
    }
    private sealed class SettingsRow : Button
    {
        private bool _hot;
        private readonly string _description;
        private readonly string _glyph;
        public SettingsRow(string text, string description, string glyph, Action action)
        {
            Text = text; _description = description; _glyph = glyph; Font = new Font(Theme.UiFont, 10f);
            AccessibleName = text; AccessibleDescription = description; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Click += (_, _) => action();
        }
        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Surface); g.SmoothingMode = SmoothingMode.AntiAlias; var s = DeviceDpi / 96f;
            if (_hot || Focused) { using var path = Draw.RoundedRect(Rectangle.Inflate(ClientRectangle, -3, -3), (int)(8 * s)); using var fill = new SolidBrush(Mix(Theme.Surface, Theme.Accent, .08f)); g.FillPath(fill, path); }
            using var icons = new Font(Theme.IconFont, 13f);
            TextRenderer.DrawText(g, _glyph, icons, new Rectangle((int)(13 * s), 0, (int)(32 * s), Height), Theme.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            var x = (int)(61 * s); var width = Math.Max(1, Width - x - (int)(45 * s));
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, (int)(13 * s), width, (int)(25 * s)), Theme.Ink, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            using var hint = new Font(Theme.UiFont, 9f);
            TextRenderer.DrawText(g, _description, hint, new Rectangle(x, (int)(39 * s), width, (int)(22 * s)), Theme.InkMuted, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, "\uE76C", icons, new Rectangle(Width - (int)(34 * s), 0, (int)(24 * s), Height), Theme.InkMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            using var divider = new Pen(Mix(Theme.Surface, Theme.Divider, .45f)); g.DrawLine(divider, (int)(60 * s), Height - 1, Width - (int)(14 * s), Height - 1);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -5, -5));
        }
    }
    private sealed class SettingsCard : Panel
    {
        private readonly Label _heading;
        private readonly Label _description;
        private readonly string _keywords;
        public string Section { get; }
        public bool Searchable { get; }
        public bool Displayed { get; set; }
        public List<SettingsRow> Rows { get; } = new();
        public SettingsCard(string section, string title, string description, string keywords, bool searchable)
        {
            Section = section; Searchable = searchable; _keywords = title + " " + description + " " + keywords;
            _heading = Caption(title, 12f, true); _description = Caption(description, 9.5f, muted: true);
            Controls.AddRange(new Control[] { _heading, _description });
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        public bool Matches(string query) => CultureInfo.GetCultureInfo("pt-BR").CompareInfo.IndexOf(_keywords + " " + Section, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
        public void AddRow(string title, string description, string glyph, Action action) { var row = new SettingsRow(title, description, glyph, action); Rows.Add(row); Controls.Add(row); }
        public void ApplyTheme()
        {
            BackColor = Theme.Chrome;
            foreach (Control child in Controls) { child.BackColor = child is Label ? Color.Transparent : Theme.Surface; child.ForeColor = child.Tag as string == "muted" ? Theme.InkMuted : Theme.Ink; }
            Invalidate(true);
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e); var s = DeviceDpi / 96f;
            _heading.SetBounds((int)(24 * s), (int)(21 * s), Math.Max(1, Width - (int)(48 * s)), (int)(26 * s));
            _description.SetBounds((int)(24 * s), (int)(54 * s), Math.Max(1, Width - (int)(48 * s)), (int)(33 * s));
            for (var i = 0; i < Rows.Count; i++) Rows[i].SetBounds((int)(10 * s), (int)((92 + i * 72) * s), Math.Max(1, Width - (int)(20 * s)), (int)(72 * s));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Draw.RoundedRect(new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3)), (int)(14 * DeviceDpi / 96f));
            using var fill = new SolidBrush(Theme.Surface); using var border = new Pen(Mix(Theme.Surface, Theme.Divider, .70f)); e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        }
    }
    private sealed class ThemeChoice : Button
    {
        private readonly bool _dark;
        public bool Selected { get; set; }
        public ThemeChoice(bool dark)
        {
            _dark = dark; Text = dark ? "Modo escuro" : "Modo claro"; AccessibleName = "Usar " + Text.ToLowerInvariant();
            Font = new Font(Theme.UiFont, 10f); Cursor = Cursors.Hand; FlatStyle = FlatStyle.Flat;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Surface); g.SmoothingMode = SmoothingMode.AntiAlias; var s = DeviceDpi / 96f;
            using var outline = Draw.RoundedRect(new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3)), (int)(10 * s));
            using var border = new Pen(Selected || Focused ? Theme.Accent : Theme.Divider, Selected ? 2f : 1f); g.DrawPath(border, outline);
            var preview = new Rectangle((int)(13 * s), (int)(13 * s), Math.Max(1, Width - (int)(26 * s)), (int)(78 * s));
            using var previewPath = Draw.RoundedRect(preview, (int)(6 * s)); using var canvas = new SolidBrush(_dark ? Color.FromArgb(27, 28, 40) : Color.White); g.FillPath(canvas, previewPath);
            using var strip = new SolidBrush(_dark ? Color.FromArgb(36, 37, 52) : Color.FromArgb(234, 237, 244)); g.FillRectangle(strip, preview.X, preview.Y, preview.Width, (int)(26 * s));
            using var ink = new SolidBrush(_dark ? Color.FromArgb(142, 147, 168) : Color.FromArgb(166, 171, 189));
            for (var i = 0; i < 3; i++) g.FillEllipse(ink, preview.X + (int)((9 + i * 9) * s), preview.Y + (int)(9 * s), (int)(4 * s), (int)(4 * s));
            using var address = new SolidBrush(_dark ? Color.FromArgb(64, 65, 84) : Color.White); g.FillRectangle(address, preview.X + (int)(43 * s), preview.Y + (int)(7 * s), Math.Max(1, preview.Width - (int)(55 * s)), (int)(12 * s));
            using var accent = new SolidBrush(Theme.Accent); g.FillRectangle(accent, preview.X + (int)(12 * s), preview.Y + (int)(40 * s), Math.Max(1, preview.Width / 3), (int)(5 * s));
            g.FillRectangle(ink, preview.X + (int)(12 * s), preview.Y + (int)(54 * s), Math.Max(1, preview.Width - (int)(38 * s)), (int)(3 * s));
            TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(15 * s), (int)(104 * s), Math.Max(1, Width - (int)(54 * s)), (int)(25 * s)), Theme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Selected) { using var icons = new Font(Theme.IconFont, 13f); TextRenderer.DrawText(g, "\uE73E", icons, new Rectangle(Width - (int)(35 * s), (int)(104 * s), (int)(24 * s), (int)(25 * s)), Theme.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter); }
        }
    }
    private sealed class ColorChoice : Button
    {
        public Color Color { get; }
        public bool Selected { get; set; }
        public ColorChoice(Color color)
        {
            Color = color; AccessibleName = "Cor " + ColorTranslator.ToHtml(color); Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Surface); g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = Rectangle.Inflate(ClientRectangle, -5, -5); using var fill = new SolidBrush(Color); g.FillEllipse(fill, r);
            if (Selected || Focused) { using var outline = new Pen(Theme.Accent, 2f); g.DrawEllipse(outline, Rectangle.Inflate(r, 3, 3)); }
            if (Selected) { using var font = new Font(Theme.IconFont, 10f); TextRenderer.DrawText(g, "\uE73E", font, ClientRectangle, System.Drawing.Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter); }
        }
    }
}
