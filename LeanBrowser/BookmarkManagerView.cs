namespace LeanBrowser;

internal sealed class BookmarkManagerView : UserControl
{
    private readonly BookmarkStore _store;
    private readonly ComboBox _folders = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, AccessibleName = "Pasta de favoritos" };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, BorderStyle = BorderStyle.None, AccessibleName = "Favoritos da pasta" };
    private readonly FlowLayoutPanel _tools = new() { Dock = DockStyle.Top, WrapContents = true, AutoSize = true };
    public event Action<string>? OpenRequested;
    public event Action? Changed;
    public BookmarkManagerView(BookmarkStore store)
    {
        _store = store; Font = new Font(Theme.UiFont, 9.5f);
        _list.Columns.Add("Título", 220); _list.Columns.Add("Endereço", 300);
        _tools.Controls.Add(_folders);
        Add("Nova pasta", () => { var input = UiDialogs.Fields(this, "Nova pasta", ("Nome (use / para subpastas)", "")); if (input is not null) _store.CreateFolder(input[0]); });
        Add("Renomear pasta", () => { if (Folder.Length == 0) return; var input = UiDialogs.Fields(this, "Renomear pasta", ("Nome", Folder)); if (input is not null) _store.RenameFolder(Folder, input[0]); });
        Add("Adicionar", () => Edit(null)); Add("Editar / mover", () => { if (Selected is { } b) Edit(b); });
        Add("Abrir", Open); Add("Remover", () => { if (Selected is { } b && MessageBox.Show(this, $"Remover “{b.Title}”?", "Favoritos", MessageBoxButtons.YesNo) == DialogResult.Yes) _store.Remove(b.Url); });
        Add("↑", () => { if (Selected is { } b) _store.Move(b.Url, -1); }); Add("↓", () => { if (Selected is { } b) _store.Move(b.Url, 1); });
        Add("Ordenar A–Z", _store.Sort);
        Add("Importar HTML…", () => { using var dialog = new OpenFileDialog { Filter = "Favoritos HTML|*.html;*.htm" }; if (dialog.ShowDialog(this) == DialogResult.OK) { if (new FileInfo(dialog.FileName).Length > 10 * 1024 * 1024) throw new InvalidDataException("O arquivo deve ter até 10 MB."); var count = _store.ImportHtml(File.ReadAllText(dialog.FileName)); MessageBox.Show(this, $"{count} favorito(s) importado(s).", "Importar favoritos"); } });
        Add("Exportar HTML…", () => { using var dialog = new SaveFileDialog { Filter = "Favoritos HTML|*.html", FileName = "Favoritos-CottonBrowser.html" }; if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, _store.ExportHtml()); });
        _folders.SelectedIndexChanged += (_, _) => RefreshList(); _list.DoubleClick += (_, _) => Open();
        Controls.Add(_list); Controls.Add(_tools); Resize += (_, _) => { _list.Columns[0].Width = Math.Max(100, _list.ClientSize.Width / 3); _list.Columns[1].Width = Math.Max(150, _list.ClientSize.Width * 2 / 3 - 20); };
        Reload(); ApplyTheme();
    }
    private string Folder => _folders.SelectedIndex > 1 ? _folders.SelectedItem as string ?? "" : "";
    private Bookmark? Selected => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as Bookmark : null;
    private void Add(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, AccessibleName = text, FlatStyle = FlatStyle.Flat, Margin = new Padding(3), Cursor = Cursors.Hand };
        button.Click += (_, _) => { var selected = Selected?.Url; try { action(); Reload(selected); Changed?.Invoke(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or System.Text.RegularExpressions.RegexMatchTimeoutException) { UiDialogs.Error(this, ex); } };
        _tools.Controls.Add(button);
    }
    private void Edit(Bookmark? bookmark)
    {
        var values = UiDialogs.Fields(this, bookmark is null ? "Adicionar favorito" : "Editar favorito",
            ("Título", bookmark?.Title ?? ""), ("Endereço", bookmark?.Url ?? "https://"), ("Pasta (vazio para a raiz)", bookmark?.Folder ?? Folder));
        if (values is not null) _store.Update(bookmark?.Url ?? "", new(values[1], values[0], values[2]));
    }
    private void Open() { if (Selected is { } b) OpenRequested?.Invoke(b.Url); }
    public void Reload(string? selected = null)
    {
        var folder = _folders.SelectedItem as string;
        _folders.Items.Clear(); _folders.Items.Add("Todas as pastas"); _folders.Items.Add("Sem pasta"); _folders.Items.AddRange(_store.Folders().Cast<object>().ToArray());
        _folders.SelectedItem = folder ?? "Todas as pastas"; if (_folders.SelectedIndex < 0) _folders.SelectedIndex = 0;
        RefreshList(selected);
    }
    private void RefreshList(string? selected = null)
    {
        _list.BeginUpdate();
        try { _list.Items.Clear(); foreach (var b in _store.Load().Where(b => _folders.SelectedIndex == 0 || b.Folder.Equals(Folder, StringComparison.OrdinalIgnoreCase))) { var item = new ListViewItem([b.Title, b.Url]) { Tag = b, ToolTipText = b.Folder }; _list.Items.Add(item); if (b.Url == selected) item.Selected = true; } }
        finally { _list.EndUpdate(); }
    }
    public void ApplyTheme()
    {
        BackColor = _tools.BackColor = _list.BackColor = _folders.BackColor = Theme.Surface;
        ForeColor = _list.ForeColor = _folders.ForeColor = Theme.Ink;
        foreach (Control child in _tools.Controls) { child.BackColor = Theme.Surface; child.ForeColor = Theme.Ink; }
    }
}
