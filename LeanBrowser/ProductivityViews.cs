namespace LeanBrowser;

public sealed partial class SettingsTab
{
    partial void ApplyFeatureTheme()
    {
        foreach (var feature in _featureCards.Values) FeatureUi.ApplyTheme(feature.View);
    }
}

internal static class FeatureUi
{
    public static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(110, 34), FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Surface, ForeColor = Theme.Ink, Margin = new Padding(4), AccessibleName = text };
        button.Click += (_, _) => action(); return button;
    }
    public static Label Note(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(650, 0),
        ForeColor = Theme.InkMuted, Margin = new Padding(4, 8, 4, 8) };
    public static FlowLayoutPanel Tools() => new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 4, 0, 4) };
    public static void Guard(IWin32Window owner, Action action)
    {
        try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { UiDialogs.Error(owner, ex); }
    }
    public static void ApplyTheme(Control root)
    {
        root.BackColor = Theme.Surface; root.ForeColor = root is Label ? Theme.InkMuted : Theme.Ink;
        foreach (Control child in root.Controls) ApplyTheme(child);
    }
    public static void WrapNotes(FlowLayoutPanel panel)
    {
        panel.SizeChanged += (_, _) =>
        {
            var width = Math.Max(1, panel.ClientSize.Width - panel.Padding.Horizontal - 16);
            foreach (var note in panel.Controls.OfType<Label>()) note.MaximumSize = new Size(width, 0);
            foreach (var check in panel.Controls.OfType<CheckBox>()) check.MaximumSize = new Size(width, 0);
        };
    }
}

internal sealed class ProductivityView : UserControl
{
    private readonly ProductivityStore _store;
    private readonly bool _reading;
    private readonly Action _saveCurrent;
    private readonly Action<WorkspaceEntry> _openWorkspace;
    private readonly Action<string> _openUrl;
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        HideSelection = false, MultiSelect = false, AccessibleName = "Itens salvos" };
    private readonly TextBox _notes = new() { Multiline = true, MaxLength = 10000, ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Bottom, Height = 90, PlaceholderText = "Anotações sobre esta página…", AccessibleName = "Anotações da leitura" };
    private bool _loading;
    private ReadingEntry? _editing;
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 28, AutoEllipsis = true };
    public ProductivityView(ProductivityStore store, bool reading, Action saveCurrent, Action<WorkspaceEntry> openWorkspace, Action<string> openUrl)
    {
        _store = store; _reading = reading; _saveCurrent = saveCurrent; _openWorkspace = openWorkspace; _openUrl = openUrl;
        Dock = DockStyle.None; Font = new Font(Theme.UiFont, 10);
        _list.Columns.Add(reading ? "Página" : "Espaço", 280); _list.Columns.Add(reading ? "Estado" : "Abas", 90);
        _list.Columns.Add(reading ? "Endereço" : "", 280);
        var tools = FeatureUi.Tools();
        tools.Controls.Add(FeatureUi.Button(reading ? "Salvar página atual" : "Salvar abas atuais", () => Run(() => { FlushNotes(); _saveCurrent(); RefreshList(); })));
        tools.Controls.Add(FeatureUi.Button(reading ? "Abrir página" : "Abrir em novas abas", OpenSelected));
        if (reading)
        {
            tools.Controls.Add(FeatureUi.Button("Marcar lida / não lida", () => Run(() =>
            {
                FlushNotes(); if (SelectedReading() is { } item)
                {
                    var current = _store.Load().ReadingList.Single(r => r.Id == item.Id);
                    _store.UpdateReading(current with { Read = !current.Read });
                }
                RefreshList();
            })));
            tools.Controls.Add(FeatureUi.Button("Salvar anotação", () => Run(() => { FlushNotes(); RefreshList(); _status.Text = "Anotação salva."; })));
        }
        else tools.Controls.Add(FeatureUi.Button("Renomear", () => Run(() =>
        {
            if (SelectedWorkspace() is not { } item) return;
            if (UiDialogs.Fields(this, "Renomear espaço", ("Nome", item.Name)) is { } input) { _store.RenameWorkspace(item.Id, input[0]); RefreshList(); }
        })));
        tools.Controls.Add(FeatureUi.Button("Remover", () => Run(() =>
        {
            if (_list.SelectedItems.Count == 0) return;
            if (MessageBox.Show(this, "Remover o item selecionado?", "Organização", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            if (reading && SelectedReading() is { } item) { _editing = null; _store.DeleteReading(item.Id); }
            else if (SelectedWorkspace() is { } workspace) _store.DeleteWorkspace(workspace.Id);
            RefreshList();
        })));
        var note = FeatureUi.Note(reading ? "Páginas e anotações ficam neste perfil. Selecione uma página para editar suas notas."
            : "Salve até 200 abas por espaço. Abrir um espaço mantém suas abas atuais. Abas anônimas não são salvas.");
        var header = new Panel { Dock = DockStyle.Top, Height = 140 }; tools.Dock = DockStyle.None;
        header.Controls.Add(tools); header.Controls.Add(note);
        var layingOut = false;
        void LayoutHeader()
        {
            if (layingOut || ClientSize.Width < 1) return;
            layingOut = true;
            try
            {
                tools.MaximumSize = new Size(ClientSize.Width, 0); tools.Width = ClientSize.Width;
                tools.PerformLayout();
                note.MaximumSize = new Size(Math.Max(1, ClientSize.Width - 8), 0);
                note.Location = new Point(4, tools.Bottom + 6); header.Height = note.Bottom + 12;
            }
            finally { layingOut = false; }
        }
        SizeChanged += (_, _) => LayoutHeader(); tools.SizeChanged += (_, _) => LayoutHeader();
        Controls.Add(_list); if (reading) Controls.Add(_notes); Controls.Add(_status); Controls.Add(header);
        _list.SelectedIndexChanged += (_, _) => Run(() =>
        {
            if (_loading || !reading) return;
            FlushNotes(); _editing = SelectedReading(); _notes.Text = _editing?.Notes ?? ""; _notes.Enabled = _editing is not null;
        });
        _notes.Leave += (_, _) => Run(FlushNotes);
        _list.DoubleClick += (_, _) => OpenSelected();
        VisibleChanged += (_, _) => { if (Visible) Run(RefreshList); };
        Run(RefreshList);
    }
    private void Run(Action action) => FeatureUi.Guard(this, action);
    private ReadingEntry? SelectedReading() => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as ReadingEntry : null;
    private WorkspaceEntry? SelectedWorkspace() => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as WorkspaceEntry : null;
    private void FlushNotes()
    {
        if (_loading || _editing is null || _editing.Notes == _notes.Text) return;
        _editing = _editing with { Notes = _notes.Text }; _store.UpdateReading(_editing);
    }
    private void OpenSelected() => Run(() =>
    {
        FlushNotes();
        if (_reading && SelectedReading() is { } reading) _openUrl(reading.Url);
        else if (SelectedWorkspace() is { } workspace) _openWorkspace(workspace);
    });
    public void RefreshList()
    {
        var document = _store.Load(); var selected = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag : null;
        _loading = true; _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            if (_reading)
                foreach (var entry in document.ReadingList.OrderBy(r => r.Read).ThenByDescending(r => r.SavedAt))
                {
                    var row = new ListViewItem([entry.Title, entry.Read ? "Lida" : "Não lida", entry.Url]) { Tag = entry };
                    _list.Items.Add(row); if (selected is ReadingEntry old && old.Id == entry.Id) row.Selected = true;
                }
            else foreach (var entry in document.Workspaces)
            {
                var row = new ListViewItem([entry.Name, entry.Tabs.Length.ToString(), ""]) { Tag = entry };
                _list.Items.Add(row); if (selected is WorkspaceEntry old && old.Id == entry.Id) row.Selected = true;
            }
            _editing = SelectedReading(); _notes.Text = _editing?.Notes ?? ""; _notes.Enabled = _editing is not null;
            _status.Text = _list.Items.Count == 0 ? "Nenhum item salvo ainda." : $"{_list.Items.Count} itens salvos.";
        }
        finally { _list.EndUpdate(); _loading = false; }
        _list.BackColor = _notes.BackColor = BackColor = Theme.Surface; _list.ForeColor = _notes.ForeColor = ForeColor = Theme.Ink;
    }
}

internal sealed class DownloadPreferencesView : FlowLayoutPanel
{
    private readonly TextBox _folder = new() { ReadOnly = true, Width = 420, AccessibleName = "Pasta de downloads" };
    public DownloadPreferencesView()
    {
        FeatureUi.WrapNotes(this);
        FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoScroll = true;
        Controls.Add(FeatureUi.Note("Pasta padrão dos downloads:")); Controls.Add(_folder);
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.Add(FeatureUi.Button("Escolher pasta…", () => FeatureUi.Guard(this, () =>
        {
            using var picker = new FolderBrowserDialog { Description = "Pasta de downloads", UseDescriptionForTitle = true, SelectedPath = Folder() };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            BrowserPreferences.Current.DownloadFolder = picker.SelectedPath; Save();
        })));
        buttons.Controls.Add(FeatureUi.Button("Usar Downloads do Windows", () => { BrowserPreferences.Current.DownloadFolder = ""; Save(); }));
        Controls.Add(buttons);
        var ask = new CheckBox { Text = "Perguntar onde salvar cada arquivo", AutoSize = true, Checked = BrowserPreferences.Current.AskDownloadLocation, Margin = new Padding(4, 16, 4, 4) };
        ask.CheckedChanged += (_, _) => { BrowserPreferences.Current.AskDownloadLocation = ask.Checked; Save(); }; Controls.Add(ask);
        Controls.Add(FeatureUi.Note("A pasta e a opção de perguntar também são usadas nos downloads de abas anônimas."));
        VisibleChanged += (_, _) => { if (Visible) { _folder.Text = Folder(); ask.Checked = BrowserPreferences.Current.AskDownloadLocation; } };
        SizeChanged += (_, _) => { _folder.Width = Math.Max(100, ClientSize.Width - 16); buttons.Width = Math.Max(100, ClientSize.Width - 16); };
        _folder.Text = Folder();
    }
    public static string Folder() => BrowserPreferences.Current.DownloadFolder is { Length: > 0 } folder ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    private void Save() { if (!BrowserPreferences.Current.Save()) MessageBox.Show(this, "Não foi possível salvar as preferências."); _folder.Text = Folder(); }
}

internal sealed class ShortcutPreferencesView : UserControl
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "Comandos e atalhos" };
    private Keys _captured;
    public ShortcutPreferencesView()
    {
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        header.Controls.Add(FeatureUi.Note("Selecione um comando, clique no campo e pressione o novo atalho."));
        var input = new ShortcutCaptureBox { ReadOnly = true, Width = 200, AccessibleName = "Capturar novo atalho", PlaceholderText = "Pressione as teclas…" };
        input.Captured = data => { _captured = data; input.Text = ShortcutCatalog.Display(data); };
        header.Controls.Add(input);
        header.Controls.Add(FeatureUi.Button("Aplicar", () => FeatureUi.Guard(this, () =>
        {
            if (_list.SelectedIndex < 0) return;
            var command = ShortcutCatalog.Commands[_list.SelectedIndex]; var prefs = BrowserPreferences.Current;
            if (!ShortcutCatalog.IsAvailable(_captured) || ShortcutCatalog.Commands.Any(c => c.Id != command.Id
                && (ShortcutCatalog.Get(c.Id, prefs) == _captured || c.Default == _captured)))
                throw new ArgumentException("Use Ctrl, Alt ou uma tecla de função. Esse atalho é reservado ou já pertence a outro comando.");
            prefs.Shortcuts[command.Id] = (int)_captured;
            if (!prefs.Save()) throw new IOException("Não foi possível salvar o atalho."); Reload();
        })));
        header.Controls.Add(FeatureUi.Button("Restaurar padrões", () => { BrowserPreferences.Current.Shortcuts.Clear(); BrowserPreferences.Current.Save(); Reload(); }));
        Controls.Add(_list); Controls.Add(header); VisibleChanged += (_, _) => { if (Visible) Reload(); }; Reload();
        FeatureUi.WrapNotes(header);
    }
    private void Reload()
    {
        var selected = _list.SelectedIndex; _list.Items.Clear();
        foreach (var command in ShortcutCatalog.Commands) _list.Items.Add(command.Label + " — " + ShortcutCatalog.Display(ShortcutCatalog.Get(command.Id, BrowserPreferences.Current)));
        if (selected >= 0) _list.SelectedIndex = selected;
        _list.BackColor = BackColor = Theme.Surface; _list.ForeColor = ForeColor = Theme.Ink;
    }
}

internal sealed class ShortcutCaptureBox : TextBox
{
    public Action<Keys>? Captured { get; set; }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.Tab or Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
        Captured?.Invoke(keyData); return true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyData is Keys.Tab or Keys.Escape) { base.OnKeyDown(e); return; }
        Captured?.Invoke(e.KeyData); e.Handled = true; e.SuppressKeyPress = true;
    }
}

internal sealed record ClearDataRequest(DateTimeOffset? From, bool History, bool Cookies, bool Cache);
internal sealed class ClearDataView : FlowLayoutPanel
{
    public ClearDataView(Func<ClearDataRequest, Task> clear)
    {
        FeatureUi.WrapNotes(this);
        FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoScroll = true;
        var period = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250, AccessibleName = "Período de limpeza" };
        period.Items.AddRange(["Última hora", "Últimas 24 horas", "Últimos 7 dias", "Todo o período"]); period.SelectedIndex = 0; Controls.Add(period);
        var history = new CheckBox { Text = "Histórico de navegação", AutoSize = true, Checked = true };
        var cookies = new CheckBox { Text = "Cookies e dados dos sites (pode sair das contas)", AutoSize = true };
        var cache = new CheckBox { Text = "Imagens e arquivos em cache", AutoSize = true };
        Controls.AddRange([history, cookies, cache]); Controls.Add(FeatureUi.Note("Favoritos, senhas salvas, downloads e espaços de trabalho são mantidos."));
        var status = FeatureUi.Note("");
        var button = FeatureUi.Button("Limpar dados selecionados", () => {});
        button.Click += async (_, _) =>
        {
            if (!history.Checked && !cookies.Checked && !cache.Checked) { status.Text = "Selecione ao menos um tipo de dado."; return; }
            DateTimeOffset? from = period.SelectedIndex switch { 0 => DateTimeOffset.UtcNow.AddHours(-1), 1 => DateTimeOffset.UtcNow.AddDays(-1), 2 => DateTimeOffset.UtcNow.AddDays(-7), _ => null };
            var request = new ClearDataRequest(from, history.Checked, cookies.Checked, cache.Checked);
            button.Enabled = period.Enabled = history.Enabled = cookies.Enabled = cache.Enabled = false; status.Text = "Limpando…";
            try { await clear(request); status.Text = "Dados selecionados apagados."; }
            catch (Exception ex) { status.Text = "Não foi possível concluir a limpeza: " + ex.Message; }
            finally { if (!IsDisposed) button.Enabled = period.Enabled = history.Enabled = cookies.Enabled = cache.Enabled = true; }
        };
        Controls.Add(button); Controls.Add(status);
    }
}

internal sealed class BackupView : FlowLayoutPanel
{
    public BackupView(Action restored)
    {
        FeatureUi.WrapNotes(this);
        FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoScroll = true;
        Controls.Add(FeatureUi.Note("Inclui favoritos, pastas, preferências, tema, zoom por site, atalhos, espaços e lista de leitura deste perfil."));
        Controls.Add(FeatureUi.Note("Senhas, cookies e histórico não são incluídos no arquivo."));
        Controls.Add(FeatureUi.Button("Exportar backup…", () => FeatureUi.Guard(this, () =>
        {
            using var dialog = new SaveFileDialog { Filter = "Backup CottonBrowser (*.cottonbackup)|*.cottonbackup", FileName = "CottonBrowser-backup.cottonbackup" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (!BrowserPreferences.Current.Save()) throw new IOException("Não foi possível salvar as preferências antes do backup.");
            BrowserBackup.Export(dialog.FileName, BrowserPaths.DataDirectory, BrowserPaths.ThemeDirectory);
            MessageBox.Show(this, "Backup exportado.");
        })));
        Controls.Add(FeatureUi.Button("Restaurar backup…", () => FeatureUi.Guard(this, () =>
        {
            using var dialog = new OpenFileDialog { Filter = "Backup CottonBrowser (*.cottonbackup)|*.cottonbackup" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (MessageBox.Show(this, "Substituir os favoritos e as preferências incluídos no backup? As abas abertas serão mantidas.",
                "Restaurar backup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var safety = Path.Combine(BrowserPaths.DataDirectory, "Backups", "antes-da-restauracao-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8] + ".cottonbackup");
            BrowserBackup.Export(safety, BrowserPaths.DataDirectory, BrowserPaths.ThemeDirectory);
            BrowserBackup.Restore(dialog.FileName, BrowserPaths.DataDirectory, BrowserPaths.ThemeDirectory);
            BrowserPreferences.Current.Reload(); restored();
            MessageBox.Show(this, "Backup restaurado. Uma cópia dos dados anteriores foi preservada em:\n" + safety);
        })));
    }
}
