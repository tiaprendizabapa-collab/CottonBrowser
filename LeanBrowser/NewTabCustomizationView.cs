namespace LeanBrowser;

internal sealed class NewTabCustomizationView : UserControl
{
    private readonly FlowLayoutPanel _tools = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
    private readonly ComboBox _background = new() { Width = 170, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Fundo da nova guia" };
    private readonly ListBox _shortcuts = new() { Dock = DockStyle.Fill, DisplayMember = nameof(Bookmark.Title), AccessibleName = "Atalhos da nova guia" };
    private bool _loading;
    private readonly List<(CheckBox Toggle, Func<BrowserPreferences, bool> Get)> _toggles = [];
    public NewTabCustomizationView()
    {
        Font = new Font(Theme.UiFont, 10); _background.Items.AddRange(["Padrão", "Azul", "Verde", "Pôr do sol"]);
        _tools.Controls.Add(new Label { Text = "Fundo", AutoSize = true, Margin = new Padding(3, 7, 3, 3) }); _tools.Controls.Add(_background);
        Toggle("Mostrar relógio", p => p.NewTabShowClock, (p, v) => p.NewTabShowClock = v);
        Toggle("Mostrar saudação", p => p.NewTabShowGreeting, (p, v) => p.NewTabShowGreeting = v);
        Toggle("Mostrar atalhos", p => p.NewTabShowShortcuts, (p, v) => p.NewTabShowShortcuts = v);
        Add("Adicionar atalho", () => Edit(null)); Add("Editar atalho", () => { if (_shortcuts.SelectedItem is Bookmark b) Edit(b); });
        Add("Remover atalho", () => { if (_shortcuts.SelectedItem is Bookmark b) { BrowserPreferences.Current.NewTabShortcuts = BrowserPreferences.Current.NewTabShortcuts.Where(x => x != b).ToArray(); Save(); } });
        Add("↑", () => MoveShortcut(-1)); Add("↓", () => MoveShortcut(1));
        _background.SelectedIndexChanged += (_, _) => { if (!_loading) { BrowserPreferences.Current.NewTabBackground = _background.Text; Save(); } };
        Controls.Add(_shortcuts); Controls.Add(_tools); Reload(); ApplyTheme();
        BrowserPreferences.Changed += OnPreferencesChanged;
    }
    private void Toggle(string text, Func<BrowserPreferences, bool> get, Action<BrowserPreferences, bool> set)
    {
        var toggle = new CheckBox { Text = text, AutoSize = true, Checked = get(BrowserPreferences.Current), Margin = new Padding(8), AccessibleName = text };
        toggle.CheckedChanged += (_, _) => { if (!_loading) { set(BrowserPreferences.Current, toggle.Checked); Save(); } }; _tools.Controls.Add(toggle);
        _toggles.Add((toggle, get));
    }
    private void Add(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new Padding(4), AccessibleName = text };
        button.Click += (_, _) => { try { action(); } catch (ArgumentException ex) { UiDialogs.Error(this, ex); } }; _tools.Controls.Add(button);
    }
    private void MoveShortcut(int direction)
    {
        var entries = BrowserPreferences.Current.NewTabShortcuts.ToArray(); var index = _shortcuts.SelectedIndex;
        if (index < 0 || index + direction < 0 || index + direction >= entries.Length) return;
        (entries[index], entries[index + direction]) = (entries[index + direction], entries[index]); BrowserPreferences.Current.NewTabShortcuts = entries; Save(); _shortcuts.SelectedIndex = index + direction;
    }
    private void Edit(Bookmark? original)
    {
        if (original is null && BrowserPreferences.Current.NewTabShortcuts.Length >= 12) throw new ArgumentException("Use até 12 atalhos.");
        var input = UiDialogs.Fields(this, "Atalho da nova guia", ("Título", original?.Title ?? ""), ("Endereço", original?.Url ?? "https://"));
        if (input is null) return;
        if (!BookmarkStore.IsWebUrl(input[1]) || string.IsNullOrWhiteSpace(input[0])) throw new ArgumentException("Informe um título e um endereço HTTP ou HTTPS.");
        var entries = BrowserPreferences.Current.NewTabShortcuts.ToList(); var index = original is null ? -1 : entries.IndexOf(original);
        var item = new Bookmark(input[1], input[0]); if (index < 0) entries.Add(item); else entries[index] = item;
        BrowserPreferences.Current.NewTabShortcuts = entries.ToArray(); Save();
    }
    private void Save() { if (!BrowserPreferences.Current.Save()) MessageBox.Show(this, "Não foi possível salvar a personalização."); Reload(); }
    private void Reload()
    {
        _loading = true;
        try
        {
            var selectedUrl = (_shortcuts.SelectedItem as Bookmark)?.Url;
            _background.SelectedItem = BrowserPreferences.Current.NewTabBackground;
            foreach (var (toggle, get) in _toggles) toggle.Checked = get(BrowserPreferences.Current);
            _shortcuts.Items.Clear(); _shortcuts.Items.AddRange(BrowserPreferences.Current.NewTabShortcuts);
            _shortcuts.SelectedItem = _shortcuts.Items.Cast<Bookmark>().FirstOrDefault(b => b.Url == selectedUrl);
        }
        finally { _loading = false; }
    }
    private void OnPreferencesChanged()
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { if (IsHandleCreated) BeginInvoke(new Action(OnPreferencesChanged)); return; }
        Reload();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) BrowserPreferences.Changed -= OnPreferencesChanged;
        base.Dispose(disposing);
    }
    public void ApplyTheme()
    {
        BackColor = _tools.BackColor = _shortcuts.BackColor = _background.BackColor = Theme.Surface;
        ForeColor = _shortcuts.ForeColor = _background.ForeColor = Theme.Ink;
        foreach (Control control in _tools.Controls) { control.BackColor = Theme.Surface; control.ForeColor = Theme.Ink; }
    }
}
