namespace LeanBrowser;

internal sealed class TabSearchDialog : Form
{
    private sealed record SearchEntry(TabPage Page, string Title, string Url, string Metadata)
    {
        public override string ToString() => string.Join(" • ", new[] { Title, Metadata, Url }.Where(value => value.Length > 0));
    }
    private readonly BrowserTabControl _owner;
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, AccessibleName = "Pesquisar abas", PlaceholderText = "Pesquisar por título, endereço ou grupo" };
    private readonly ListBox _results = new() { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed,
        IntegralHeight = false, BorderStyle = BorderStyle.None, AccessibleName = "Abas encontradas" };
    private readonly Label _count = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _open = new() { Text = "Ir para a aba", AutoSize = true, FlatStyle = FlatStyle.Flat };

    public TabPage? SelectedPage => (_results.SelectedItem as SearchEntry)?.Page;

    public TabSearchDialog(BrowserTabControl owner)
    {
        _owner = owner;
        Text = "Pesquisar abas";
        Font = new Font(Theme.UiFont, 10f);
        BackColor = Theme.Surface;
        ForeColor = Theme.Ink;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        ClientSize = new Size(600, 450);
        MinimumSize = new Size(430, 320);
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        _search.BackColor = Theme.SurfaceHot;
        _search.ForeColor = Theme.Ink;
        _search.TextChanged += (_, _) => Filter();
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode is not (Keys.Down or Keys.Up)) return;
            if (_results.Items.Count > 0)
                _results.SelectedIndex = Math.Clamp(_results.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1), 0, _results.Items.Count - 1);
            e.Handled = e.SuppressKeyPress = true;
        };
        _results.BackColor = Theme.Surface;
        _results.ForeColor = Theme.Ink;
        _results.ItemHeight = 60;
        _results.DrawItem += DrawResult;
        _results.SelectedIndexChanged += (_, _) => _open.Enabled = SelectedPage is not null;
        _results.DoubleClick += (_, _) => SelectPage();
        _results.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            SelectPage();
            e.Handled = e.SuppressKeyPress = true;
        };
        _open.BackColor = Theme.Accent;
        _open.ForeColor = Theme.Accent.GetBrightness() > .5 ? Color.Black : Color.White;
        _open.Click += (_, _) => SelectPage();
        var cancel = new Button { Text = "Fechar", AutoSize = true, FlatStyle = FlatStyle.Flat,
            BackColor = Theme.SurfaceHot, ForeColor = Theme.Ink, DialogResult = DialogResult.Cancel };
        AcceptButton = _open;
        CancelButton = cancel;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(_search, 0, 0);
        layout.Controls.Add(_results, 0, 1);
        layout.Controls.Add(_count, 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        buttons.Controls.Add(_open);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        Shown += (_, _) =>
        {
            _results.ItemHeight = LogicalToDeviceUnits(60);
            Filter();
            _search.Focus();
        };
    }

    private void Filter()
    {
        var query = _search.Text.Trim();
        var entries = _owner.TabPages.Cast<TabPage>().Where(page => !page.IsDisposed).Select(page =>
        {
            var url = "";
            var metadata = "";
            if (page is BrowserTab tab)
            {
                url = tab.LastKnownUrl;
                try { url = tab.Web.Source?.AbsoluteUri ?? url; }
                catch (InvalidOperationException) { }
                catch (System.Runtime.InteropServices.COMException) { }
                metadata = string.Join(" • ", new[] { tab.IsPinned ? "Fixada" : null, tab.GroupName,
                    tab.IsPrivate ? "Anônima" : null, tab.IsSuspended ? "Suspensa" : null }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            }
            else metadata = "Página do navegador";
            return new SearchEntry(page, page.Text, url, metadata);
        }).Where(entry => query.Length == 0 || entry.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || entry.Url.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.Metadata.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        var selected = SelectedPage ?? _owner.SelectedTab;
        _results.BeginUpdate();
        _results.Items.Clear();
        _results.Items.AddRange(entries.Cast<object>().ToArray());
        if (entries.Length > 0)
        {
            var index = Array.FindIndex(entries, entry => entry.Page == selected);
            _results.SelectedIndex = Math.Max(0, index);
        }
        _results.EndUpdate();
        _open.Enabled = entries.Length > 0;
        _count.Text = entries.Length == 0 ? "Nenhuma aba encontrada" : $"{entries.Length} de {_owner.TabCount} abas • Enter para selecionar";
        _count.ForeColor = Theme.InkMuted;
    }

    private void SelectPage()
    {
        if (SelectedPage is { IsDisposed: false } page && _owner.TabPages.Contains(page)) DialogResult = DialogResult.OK;
        else Filter();
    }

    private void DrawResult(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _results.Items.Count || _results.Items[e.Index] is not SearchEntry entry) return;
        using var background = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Theme.SurfaceHot : Theme.Surface);
        e.Graphics.FillRectangle(background, e.Bounds);
        var pad = LogicalToDeviceUnits(10);
        var bounds = new Rectangle(e.Bounds.Left + pad, e.Bounds.Top + LogicalToDeviceUnits(4), e.Bounds.Width - 2 * pad, e.Bounds.Height / 2);
        TextRenderer.DrawText(e.Graphics, entry.Title, Font, bounds, Theme.Ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        var detail = string.Join(" • ", new[] { entry.Metadata, entry.Url }.Where(value => value.Length > 0));
        bounds.Y += e.Bounds.Height / 2 - LogicalToDeviceUnits(2);
        bounds.Height = e.Bounds.Height / 2 - LogicalToDeviceUnits(6);
        using var detailFont = new Font(Font.FontFamily, 8.5f);
        TextRenderer.DrawText(e.Graphics, detail, detailFont, bounds, Theme.InkMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
}
