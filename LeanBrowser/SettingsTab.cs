namespace LeanBrowser;

/// <summary>Painel de configurações com navegação lateral e cartões, inspirado em um hub moderno.</summary>
public sealed class SettingsTab : TabPage
{
    private readonly Action<bool> _setTheme;
    private readonly Action _clearPasswords;
    private readonly Func<IReadOnlyList<Bookmark>> _loadBookmarks;
    private readonly Action _addBookmark;
    private readonly Action<string> _removeBookmark;
    private readonly bool _isAdmin;
    private readonly Panel _sidebar = new() { Dock = DockStyle.Left, Width = 238, Padding = new Padding(18, 24, 12, 18) };
    private readonly Panel _main = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(48, 28, 48, 36) };
    private readonly Panel _search = new() { Height = 42 };
    private readonly TextBox _searchInput = new() { BorderStyle = BorderStyle.None, Font = new Font(Theme.UiFont, 10f), Text = "Pesquisar nas configurações" };
    private readonly Label _searchIcon = new() { Text = "⌕", AutoSize = false, Width = 34, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Theme.UiFont, 16f) };
    private readonly Label _title = Header("Configurações", 24f, true);
    private readonly Label _subtitle = Header("Personalize o CottonBrowser do seu jeito.", 10f, false);
    private readonly SettingsCard _appearanceCard = new();
    private readonly SettingsCard _favoritesCard = new();
    private readonly SettingsCard _passwordCard = new();
    private readonly RadioButton _light = new() { Text = "Modo claro", AutoSize = true };
    private readonly RadioButton _dark = new() { Text = "Modo escuro", AutoSize = true };
    private readonly ListBox _favorites = new() { IntegralHeight = false };
    private readonly Button _refresh = ActionButton("Atualizar");
    private readonly Button _add = ActionButton("Adicionar página atual");
    private readonly Button _remove = ActionButton("Remover selecionado");
    private readonly Button _clear = ActionButton("Apagar senhas salvas");
    private readonly List<Button> _navigation = new();
    private bool _syncingTheme;
    public event Action<string>? FavoriteSelected;

    public SettingsTab(Action<bool> setTheme, Action clearPasswords, Func<IReadOnlyList<Bookmark>> loadBookmarks, Action addBookmark, Action<string> removeBookmark, bool isAdmin)
        : base("Configurações")
    {
        _setTheme = setTheme; _clearPasswords = clearPasswords; _loadBookmarks = loadBookmarks; _addBookmark = addBookmark; _removeBookmark = removeBookmark; _isAdmin = isAdmin;
        Controls.Add(_main);
        Controls.Add(_sidebar);
        BuildSidebar(); BuildMain();
        _searchInput.GotFocus += (_, _) => { if (_searchInput.Text == "Pesquisar nas configurações") { _searchInput.Text = ""; _searchInput.ForeColor = Theme.Ink; } };
        _searchInput.LostFocus += (_, _) => { if (string.IsNullOrWhiteSpace(_searchInput.Text)) { _searchInput.Text = "Pesquisar nas configurações"; _searchInput.ForeColor = Theme.InkMuted; } };
        _light.CheckedChanged += (_, _) => { if (!_syncingTheme && _light.Checked) _setTheme(false); };
        _dark.CheckedChanged += (_, _) => { if (!_syncingTheme && _dark.Checked) _setTheme(true); };
        _refresh.Click += (_, _) => ReloadBookmarks();
        _add.Click += (_, _) => { _addBookmark(); ReloadBookmarks(); };
        _remove.Click += (_, _) => { if (_favorites.SelectedItem is BookmarkItem item) { _removeBookmark(item.Url); ReloadBookmarks(); } };
        _clear.Click += (_, _) => _clearPasswords();
        _favorites.DoubleClick += (_, _) => { if (_favorites.SelectedItem is BookmarkItem item) FavoriteSelected?.Invoke(item.Url); };
        Resize += (_, _) => LayoutMain();
        ApplyTheme();
    }

    public void ReloadBookmarks()
    {
        _favorites.Items.Clear();
        foreach (var bookmark in _loadBookmarks()) _favorites.Items.Add(new BookmarkItem(bookmark.Title, bookmark.Url));
    }

    public void ApplyTheme()
    {
        BackColor = Theme.Chrome; _sidebar.BackColor = Theme.Surface; _main.BackColor = Theme.Chrome; _search.BackColor = Theme.Surface;
        foreach (var nav in _navigation) { nav.BackColor = Theme.Surface; nav.ForeColor = Theme.InkMuted; nav.FlatAppearance.MouseOverBackColor = Theme.SurfaceHot; }
        foreach (var card in new[] { _appearanceCard, _favoritesCard, _passwordCard }) card.ApplyTheme();
        _searchInput.BackColor = Theme.Surface; _searchInput.ForeColor = _searchInput.Text == "Pesquisar nas configurações" ? Theme.InkMuted : Theme.Ink; _searchIcon.BackColor = Theme.Surface; _searchIcon.ForeColor = Theme.InkMuted;
        _favorites.BackColor = Theme.SurfaceHot; _favorites.ForeColor = Theme.Ink;
        _syncingTheme = true; _light.Checked = !Theme.IsDark; _dark.Checked = Theme.IsDark; _syncingTheme = false; Invalidate(true);
    }

    private void BuildSidebar()
    {
        var brand = new PictureBox { Dock = DockStyle.Top, Height = 58, SizeMode = PictureBoxSizeMode.Zoom, Padding = new Padding(0, 0, 8, 0), BackColor = Color.Transparent, Image = LoadLogo() };
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 18, 0, 0) };
        AddNav(nav, "⌂   Geral", _main); AddNav(nav, "◐   Aparência", _appearanceCard); AddNav(nav, "☆   Favoritos", _favoritesCard);
        if (_isAdmin) AddNav(nav, "▣   Senhas salvas", _passwordCard);
        AddNav(nav, "◈   Privacidade e proteção", _main);
        _sidebar.Controls.Add(nav); _sidebar.Controls.Add(brand);
    }

    private static Image? LoadLogo()
    {
        using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("LeanBrowser.Assets.AbapaLogo.png");
        if (stream is null) return null;
        using var source = Image.FromStream(stream);
        var logo = new Bitmap(source);
        KeepCloudWhite(logo);
        return logo;
    }

    private static void KeepCloudWhite(Bitmap logo)
    {
        // A versão transparente da marca deixa o interior do algodão revelar
        // o fundo escuro. Preenchemos apenas a área fechada do ícone; o fundo
        // externo continua transparente e o texto mantém seus vazados.
        var iconWidth = Math.Min(logo.Width, (int)(logo.Height * 1.08f));
        var transparent = new bool[iconWidth, logo.Height];
        var outside = new bool[iconWidth, logo.Height];
        var queue = new Queue<Point>();

        for (var y = 0; y < logo.Height; y++)
        for (var x = 0; x < iconWidth; x++)
            transparent[x, y] = logo.GetPixel(x, y).A == 0;

        for (var x = 0; x < iconWidth; x++)
        {
            EnqueueIfTransparent(x, 0);
            EnqueueIfTransparent(x, logo.Height - 1);
        }
        for (var y = 1; y < logo.Height - 1; y++)
        {
            EnqueueIfTransparent(0, y);
            EnqueueIfTransparent(iconWidth - 1, y);
        }

        while (queue.Count > 0)
        {
            var point = queue.Dequeue();
            foreach (var next in new[]
            {
                new Point(point.X - 1, point.Y), new Point(point.X + 1, point.Y),
                new Point(point.X, point.Y - 1), new Point(point.X, point.Y + 1)
            })
            {
                if (next.X < 0 || next.X >= iconWidth || next.Y < 0 || next.Y >= logo.Height || outside[next.X, next.Y]) continue;
                if (!transparent[next.X, next.Y]) continue;
                outside[next.X, next.Y] = true;
                queue.Enqueue(next);
            }
        }

        for (var y = 0; y < logo.Height; y++)
        for (var x = 0; x < iconWidth; x++)
        {
            if (!transparent[x, y] || outside[x, y]) continue;
            logo.SetPixel(x, y, Color.White);
        }

        void EnqueueIfTransparent(int x, int y)
        {
            if (transparent[x, y] && !outside[x, y])
            {
                outside[x, y] = true;
                queue.Enqueue(new Point(x, y));
            }
        }
    }

    private void BuildMain()
    {
        _search.Controls.Add(_searchInput); _search.Controls.Add(_searchIcon); _searchInput.Dock = DockStyle.Fill; _searchInput.Padding = new Padding(4);
        _main.Controls.Add(_search); _main.Controls.Add(_title); _main.Controls.Add(_subtitle); _main.Controls.Add(_appearanceCard); _main.Controls.Add(_favoritesCard);
        if (_isAdmin) _main.Controls.Add(_passwordCard);
        AddCardHeader(_appearanceCard, "Aparência", "Escolha como o navegador, as abas e as páginas devem aparecer."); _appearanceCard.Controls.Add(_light); _appearanceCard.Controls.Add(_dark);
        AddCardHeader(_favoritesCard, "Favoritos", "Acesse suas páginas favoritas sem sair das configurações."); _favoritesCard.Controls.Add(_favorites); _favoritesCard.Controls.Add(_add); _favoritesCard.Controls.Add(_remove); _favoritesCard.Controls.Add(_refresh);
        AddCardHeader(_passwordCard, "Senhas salvas", "Gerencie as senhas armazenadas neste perfil."); _passwordCard.Controls.Add(_clear);
    }

    private void AddNav(FlowLayoutPanel nav, string text, Control target)
    {
        var button = new Button { Text = text, Width = 204, Height = 42, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0), FlatStyle = FlatStyle.Flat, Font = new Font(Theme.UiFont, 9.5f, FontStyle.Bold), Cursor = Cursors.Hand };
        button.FlatAppearance.BorderSize = 0; button.Click += (_, _) => _main.ScrollControlIntoView(target); _navigation.Add(button); nav.Controls.Add(button);
    }

    private void AddCardHeader(SettingsCard card, string title, string description)
    {
        var heading = Header(title, 13f, true); heading.Location = new Point(24, 18); card.Controls.Add(heading);
        var hint = Header(description, 9.5f, false); hint.Location = new Point(24, 48); card.Controls.Add(hint);
    }

    private void LayoutMain()
    {
        var width = Math.Max(520, _main.ClientSize.Width - _main.Padding.Horizontal);
        _search.Location = new Point(0, 0); _search.Width = Math.Min(760, width); _title.Location = new Point(0, 64); _subtitle.Location = new Point(0, 101);
        _appearanceCard.Location = new Point(0, 140); _appearanceCard.Size = new Size(width, 128); _light.Location = new Point(24, 86); _dark.Location = new Point(150, 86);
        _favoritesCard.Location = new Point(0, 286); _favoritesCard.Size = new Size(width, 276); _favorites.Location = new Point(24, 82); _favorites.Size = new Size(Math.Min(width - 48, 680), 132);
        _add.Location = new Point(24, 226); _remove.Location = new Point(190, 226); _refresh.Location = new Point(352, 226);
        _passwordCard.Location = new Point(0, 580); _passwordCard.Size = new Size(width, 116); _clear.Location = new Point(24, 70); _main.AutoScrollMinSize = new Size(0, _isAdmin ? 730 : 580);
    }

    private static Label Header(string text, float size, bool bold) => new() { Text = text, AutoSize = true, Font = new Font(Theme.UiFont, size, bold ? FontStyle.Bold : FontStyle.Regular), BackColor = Color.Transparent, ForeColor = Theme.Ink };
    private static Button ActionButton(string text) => new() { Text = text, AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, Padding = new Padding(12, 0, 12, 0), Cursor = Cursors.Hand };
    private sealed record BookmarkItem(string Title, string Url) { public override string ToString() => Title; }

    private sealed class SettingsCard : Panel
    {
        public SettingsCard() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Padding = new Padding(18); }
        public void ApplyTheme() { BackColor = Theme.Surface; ForeColor = Theme.Ink; foreach (Control child in Controls) { child.ForeColor = Theme.Ink; if (child is Button button) { button.BackColor = Theme.SurfaceHot; button.ForeColor = Theme.Ink; button.FlatAppearance.BorderSize = 0; } } Invalidate(true); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using var path = Draw.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 12); using var fill = new SolidBrush(Theme.Surface); using var border = new Pen(Theme.Divider); e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
    }
}
