namespace LeanBrowser;

internal sealed class TabGroupDialog : Form
{
    private readonly TextBox _name = new() { Dock = DockStyle.Fill, MaxLength = 48, AccessibleName = "Nome do grupo" };
    private readonly Button _color = new() { Text = "Escolher cor...", AutoSize = true, FlatStyle = FlatStyle.Flat };
    private readonly Button _save = new() { Text = "Salvar", AutoSize = true, FlatStyle = FlatStyle.Flat };
    private Color _selectedColor;

    public string GroupName => _name.Text.Trim();
    public int GroupColorArgb => _selectedColor.ToArgb();

    public TabGroupDialog(string? name, int colorArgb)
    {
        Text = string.IsNullOrWhiteSpace(name) ? "Novo grupo de abas" : "Editar grupo de abas";
        Font = new Font(Theme.UiFont, 10f);
        BackColor = Theme.Surface;
        ForeColor = Theme.Ink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(390, 188);
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        _name.Text = name ?? "";
        _name.BackColor = Theme.SurfaceHot;
        _name.ForeColor = Theme.Ink;
        _selectedColor = colorArgb == 0 ? Theme.Accent : Color.FromArgb(colorArgb);
        _color.BackColor = _selectedColor;
        _color.ForeColor = _selectedColor.GetBrightness() > .5 ? Color.Black : Color.White;
        _color.Click += (_, _) =>
        {
            using var picker = new ColorDialog { Color = _selectedColor, FullOpen = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            _selectedColor = picker.Color;
            _color.BackColor = _selectedColor;
            _color.ForeColor = _selectedColor.GetBrightness() > .5 ? Color.Black : Color.White;
        };
        _save.BackColor = Theme.Accent;
        _save.ForeColor = Theme.Accent.GetBrightness() > .5 ? Color.Black : Color.White;
        _save.Enabled = !string.IsNullOrWhiteSpace(_name.Text);
        _name.TextChanged += (_, _) => _save.Enabled = GroupName.Length > 0;
        _save.Click += (_, _) => { if (GroupName.Length > 0) DialogResult = DialogResult.OK; };
        var cancel = new Button { Text = "Cancelar", AutoSize = true, FlatStyle = FlatStyle.Flat,
            BackColor = Theme.SurfaceHot, ForeColor = Theme.Ink, DialogResult = DialogResult.Cancel };
        AcceptButton = _save;
        CancelButton = cancel;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(new Label { Text = "Nome do grupo", Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(_name, 0, 1);
        layout.Controls.Add(_color, 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        buttons.Controls.Add(_save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        Shown += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }
}
