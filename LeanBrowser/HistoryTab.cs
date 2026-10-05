namespace LeanBrowser;

/// <summary>Página interna de histórico com lista virtualizada e exclusão por visita ou período.</summary>
internal sealed class HistoryTab : TabPage
{
    private readonly HistoryView _view;

    public event Action<string>? OpenRequested;

    public HistoryTab(NavigationHistoryStore store) : base("Histórico")
    {
        Padding = Padding.Empty;
        _view = new HistoryView(store) { Dock = DockStyle.Fill };
        _view.OpenRequested += url => OpenRequested?.Invoke(url);
        Controls.Add(_view);
        ApplyTheme();
    }

    public void FocusSearch() => _view.FocusSearch();
    public void ApplyTheme() { BackColor = Theme.Chrome; _view.ApplyTheme(); }
}

/// <summary>Lista de visitas compartilhada pela aba de histórico e pelas configurações.</summary>
internal sealed class HistoryView : UserControl
{
    private readonly NavigationHistoryStore _store;
    private readonly bool _embedded;
    private readonly Panel _header = new() { Dock = DockStyle.Top };
    private readonly Label _title = new() { Text = "Histórico", Font = new Font(Theme.UiFont, 24, FontStyle.Bold), AutoSize = false };
    private readonly Label _summary = new() { AutoSize = false };
    private readonly FlowLayoutPanel _tools = new() { WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
    private readonly TextBox _search = new() { PlaceholderText = "Pesquisar título ou endereço", AccessibleName = "Pesquisar no histórico" };
    private readonly Button _open = ActionButton("Abrir");
    private readonly Button _deleteSelected = ActionButton("Apagar seleção");
    private readonly FlowLayoutPanel _rangeTools = new() { WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
    private readonly Panel _dates = new() { Visible = false, Margin = new Padding(0, 0, 10, 8) };
    private readonly ComboBox _period = new() { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Período a apagar" };
    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Short, AccessibleName = "Data inicial" };
    private readonly DateTimePicker _until = new() { Format = DateTimePickerFormat.Short, AccessibleName = "Data final" };
    private readonly Label _untilLabel = new() { Text = "até", AutoSize = false };
    private readonly Button _deleteRange = ActionButton("Apagar período…");
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true,
        HideSelection = false, VirtualMode = true, BorderStyle = BorderStyle.None, AccessibleName = "Visitas do histórico"
    };
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 180 };
    private IReadOnlyList<NavigationHistoryEntry> _entries = [];
    private int _refreshVersion;
    private bool _layoutRunning;

    public event Action<string>? OpenRequested;

    public HistoryView(NavigationHistoryStore store, bool embedded = false)
    {
        _store = store;
        _embedded = embedded;
        _title.Visible = !embedded;
        Font = new Font(Theme.UiFont, 10f);
        Padding = Padding.Empty;
        _list.Columns.Add("Página"); _list.Columns.Add("Endereço"); _list.Columns.Add("Data e hora");
        _list.RetrieveVirtualItem += (_, e) =>
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= _entries.Count) { e.Item = new ListViewItem(); return; }
            var entry = _entries[e.ItemIndex];
            e.Item = new ListViewItem(new[]
            {
                string.IsNullOrWhiteSpace(entry.Title) ? UrlHelper.ForDisplay(entry.Url) : entry.Title,
                entry.Url, entry.VisitedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
            });
        };
        _list.SelectedIndexChanged += (_, _) => UpdateActions();
        _list.VirtualItemsSelectionRangeChanged += (_, _) => UpdateActions();
        _list.DoubleClick += (_, _) => OpenSelection();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { OpenSelection(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Delete) { DeleteSelection(); e.SuppressKeyPress = true; }
        };
        _tools.Controls.AddRange(new Control[] { _search, _open, _deleteSelected });
        _period.Items.AddRange(new object[] { "Última hora", "Últimas 24 horas", "Últimos 7 dias", "Últimas 4 semanas", "Todo o histórico", "Escolher datas" });
        _period.SelectedIndex = 0;
        _period.SelectedIndexChanged += (_, _) =>
        {
            _dates.Visible = _period.SelectedIndex == 5;
            LayoutPage();
        };
        _from.Value = DateTime.Today.AddDays(-7); _until.Value = DateTime.Today;
        _dates.Controls.AddRange(new Control[] { _from, _untilLabel, _until });
        _rangeTools.Controls.AddRange(new Control[] { _period, _dates, _deleteRange });
        _header.Controls.AddRange(new Control[] { _title, _tools, _rangeTools, _summary });
        Controls.Add(_list); Controls.Add(_header);
        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { _search.Clear(); e.SuppressKeyPress = true; } };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = RefreshEntriesAsync(); };
        _open.Click += (_, _) => OpenSelection();
        _deleteSelected.Click += (_, _) => DeleteSelection();
        _deleteRange.Click += (_, _) => DeleteRange();
        _store.Changed += HistoryChanged;
        Resize += (_, _) => LayoutPage();
        DpiChangedAfterParent += (_, _) => LayoutPage();
        ApplyTheme(); UpdateActions();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        LayoutPage();
        _ = RefreshEntriesAsync();
    }

    public void FocusSearch() => _search.Focus();

    private void HistoryChanged()
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke((Action)(() => { if (!IsDisposed) { _debounce.Stop(); _debounce.Start(); } })); }
        catch (InvalidOperationException) { }
    }

    private async Task RefreshEntriesAsync()
    {
        var version = ++_refreshVersion;
        var query = _search.Text;
        await _store.Ready;
        var entries = await Task.Run(() => _store.Search(query));
        if (IsDisposed || version != _refreshVersion) return;
        _list.BeginUpdate();
        try
        {
            _list.SelectedIndices.Clear();
            _list.VirtualListSize = 0;
            _entries = entries;
            _list.VirtualListSize = entries.Count;
        }
        finally { _list.EndUpdate(); }
        _summary.Text = entries.Count == 0 ? "Nenhuma visita encontrada. A navegação anônima não aparece aqui."
            : $"{entries.Count:N0} visita(s) · Mais recentes primeiro · A navegação anônima não é salva.";
        UpdateActions();
    }

    private void OpenSelection()
    {
        if (_list.SelectedIndices.Count > 0)
            OpenRequested?.Invoke(_entries[_list.SelectedIndices[0]].Url);
    }

    private void UpdateActions() => _open.Enabled = _deleteSelected.Enabled = _list.SelectedIndices.Count > 0;

    private void DeleteSelection()
    {
        var selected = _list.SelectedIndices.Cast<int>().Where(index => index >= 0 && index < _entries.Count)
            .Select(index => _entries[index]).ToArray();
        if (selected.Length == 0) return;
        if (MessageBox.Show(this, $"Apagar {selected.Length} visita(s) selecionada(s) do histórico?", "Apagar visitas",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _store.RemoveEntries(selected);
        _ = RefreshEntriesAsync();
    }

    private void DeleteRange()
    {
        var now = DateTimeOffset.Now;
        DateTimeOffset? from = _period.SelectedIndex switch
        {
            0 => now.AddHours(-1), 1 => now.AddDays(-1), 2 => now.AddDays(-7), 3 => now.AddDays(-28),
            5 => new DateTimeOffset(_from.Value.Date), _ => null
        };
        DateTimeOffset? until = _period.SelectedIndex == 5 ? new DateTimeOffset(_until.Value.Date.AddDays(1)) : null;
        if (from.HasValue && until.HasValue && from >= until)
        {
            MessageBox.Show(this, "A data final deve ser igual ou posterior à data inicial.", "Escolher período", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var period = _period.SelectedIndex == 5 ? $"de {_from.Value:dd/MM/yyyy} até {_until.Value:dd/MM/yyyy}" : _period.Text.ToLowerInvariant();
        if (MessageBox.Show(this, $"Apagar as visitas de {period}? Essa exclusão vale para todo o histórico, inclusive fora da pesquisa atual.",
                "Apagar período", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _store.RemoveRange(from, until);
        _ = RefreshEntriesAsync();
    }

    public void ApplyTheme()
    {
        BackColor = _header.BackColor = _tools.BackColor = _rangeTools.BackColor = _dates.BackColor = _embedded ? Theme.Surface : Theme.Chrome;
        _title.ForeColor = Theme.Ink; _summary.ForeColor = Theme.InkMuted; _untilLabel.ForeColor = Theme.Ink;
        _list.BackColor = _search.BackColor = _period.BackColor = Theme.Surface;
        _list.ForeColor = _search.ForeColor = _period.ForeColor = Theme.Ink;
        foreach (var button in new[] { _open, _deleteSelected, _deleteRange })
        {
            button.BackColor = Theme.Surface; button.ForeColor = Theme.Ink; button.FlatAppearance.BorderColor = Theme.Divider;
        }
        Invalidate(true);
    }

    private int D(int value) => (int)Math.Round(value * DeviceDpi / 96f);

    private void LayoutPage()
    {
        if (_layoutRunning || Width < 1) return;
        _layoutRunning = true;
        try
        {
            var inset = _embedded ? 0 : D(24);
            var width = Math.Max(D(180), ClientSize.Width - inset * 2);
            _title.SetBounds(D(24), D(22), width, D(44));
            _search.Size = new Size(Math.Min(D(440), Math.Max(1, width - _search.Margin.Horizontal)), D(30));
            _open.Size = new Size(D(82), D(32)); _deleteSelected.Size = new Size(D(144), D(32));
            _period.Size = new Size(Math.Min(D(195), width), D(30));
            _dates.Size = new Size(D(300), D(32));
            _from.SetBounds(0, D(3), D(128), D(30));
            _untilLabel.SetBounds(D(135), D(7), D(28), D(25));
            _until.SetBounds(D(170), D(3), D(128), D(30));
            _deleteRange.Size = new Size(D(155), D(32));
            _tools.SetBounds(inset, _embedded ? 0 : D(78), width, D(40));
            _tools.MaximumSize = new Size(width, 0); _tools.PerformLayout();
            _rangeTools.SetBounds(inset, _tools.Bottom + D(8), width, D(40));
            _rangeTools.MaximumSize = new Size(width, 0); _rangeTools.PerformLayout();
            _summary.SetBounds(inset, _rangeTools.Bottom + D(9), width, D(44));
            _header.Height = _summary.Bottom + D(14);
            var available = Math.Max(D(200), _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            _list.Columns[0].Width = (int)(available * .36);
            _list.Columns[1].Width = Math.Max(D(100), available - _list.Columns[0].Width - D(165));
            _list.Columns[2].Width = D(165);
        }
        finally { _layoutRunning = false; }
    }

    private static Button ActionButton(string label) => new()
    {
        Text = label, AccessibleName = label, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
        Margin = new Padding(0, 0, 10, 8)
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _store.Changed -= HistoryChanged; _debounce.Dispose(); }
        base.Dispose(disposing);
    }
}
