namespace LeanBrowser;

/// <summary>Faixa superior Cotton Orbit, com abas compactas e botão de nova aba.</summary>
public sealed class BrowserTabControl : TabControl
{
    private readonly Dictionary<TabPage, TabHeader> _headers = new();
    private readonly Button _newTabButton = new()
    {
        Text = "", AccessibleName = "Nova aba", Size = new Size(36, 36),
        Margin = new Padding(4, 4, 6, 0), FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Chrome, ForeColor = Theme.Ink, Cursor = Cursors.Hand,
        Font = new Font(Theme.UiFont, 15f, FontStyle.Regular),
        TextAlign = ContentAlignment.MiddleCenter, UseVisualStyleBackColor = false
    };
    private readonly Label _brandLabel = new()
    {
        Text = "", AutoSize = false, Height = 38, Width = 42,
        TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(0),
        Font = new Font(Theme.UiFont, 11f, FontStyle.Bold), ForeColor = Theme.Accent,
        BackColor = Theme.Chrome
    };
    private readonly ToolTip _toolTip = new();
    public FlowLayoutPanel HeaderStrip { get; } = new()
    {
        Dock = DockStyle.Top, Height = 50, WrapContents = false,
        FlowDirection = FlowDirection.LeftToRight, AutoScroll = true,
        BackColor = Theme.Chrome, Padding = new Padding(8, 3, 10, 3)
    };
    public int BrowserTabCount => TabPages.OfType<BrowserTab>().Count();
    public event Action<BrowserTab>? CloseRequested;
    public event Action<TabPage>? AuxiliaryCloseRequested;
    public event Action? NewTabRequested;
    public event Action? BrandClicked;

    public void ApplyTheme()
    {
        HeaderStrip.BackColor = Theme.Chrome;
        _brandLabel.BackColor = Theme.Chrome;
        _brandLabel.ForeColor = Theme.Accent;
        _newTabButton.BackColor = Theme.Chrome;
        _newTabButton.ForeColor = Theme.Ink;
        _newTabButton.FlatAppearance.MouseOverBackColor = Theme.SurfaceHot;
        HeaderStrip.Invalidate(true);
        foreach (var header in _headers.Values) header.Invalidate();
    }

    public BrowserTabControl()
    {
        // O controle nativo mantém as páginas e a seleção; a barra separada
        // transforma as abas em uma faixa superior sem ocupar a área da página.
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(1, 1);
        Appearance = TabAppearance.FlatButtons;
        _newTabButton.FlatAppearance.BorderSize = 0;
        _newTabButton.FlatAppearance.MouseOverBackColor = Theme.SurfaceHot;
        _newTabButton.Paint += (_, e) =>
        {
            var center = new Point(_newTabButton.ClientSize.Width / 2, _newTabButton.ClientSize.Height / 2);
            var arm = Math.Max(5, _newTabButton.LogicalToDeviceUnits(6));
            using var pen = new Pen(Theme.Ink, Math.Max(1.4f, _newTabButton.DeviceDpi / 72f));
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawLine(pen, center.X - arm, center.Y, center.X + arm, center.Y);
            e.Graphics.DrawLine(pen, center.X, center.Y - arm, center.X, center.Y + arm);
        };
        _newTabButton.Click += (_, _) => NewTabRequested?.Invoke();
        _toolTip.SetToolTip(_newTabButton, "Nova aba (Ctrl+T)");
        HeaderStrip.Controls.Add(_brandLabel);
        HeaderStrip.Controls.Add(_newTabButton);
        _brandLabel.Cursor = Cursors.Hand;
        _brandLabel.Click += (_, _) => BrandClicked?.Invoke();
        _toolTip.SetToolTip(_brandLabel, "CottonBrowser — clique 5 vezes rapidamente");
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null)
            {
                var bitmap = new Bitmap(24, 24);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.DrawIcon(icon, new Rectangle(0, 0, 24, 24));
                _brandLabel.Image = bitmap;
            }
            _brandLabel.ImageAlign = ContentAlignment.MiddleLeft;
        }
        catch { }
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is not TabPage tab) return;
        var header = new TabHeader(this, tab);
        _headers.Add(tab, header);
        HeaderStrip.Controls.Add(header);
        HeaderStrip.Controls.SetChildIndex(_newTabButton, HeaderStrip.Controls.Count - 1);
        tab.TextChanged += OnTabTextChanged;
    }

    protected override void OnControlRemoved(ControlEventArgs e)
    {
        base.OnControlRemoved(e);
        if (e.Control is not TabPage tab) return;
        tab.TextChanged -= OnTabTextChanged;
        if (_headers.Remove(tab, out var header)) header.Dispose();
    }

    private void OnTabTextChanged(object? sender, EventArgs e)
    {
        if (sender is TabPage tab && _headers.TryGetValue(tab, out var header))
        {
            header.AccessibleName = tab.Text;
            _toolTip.SetToolTip(header, tab.Text);
            header.Invalidate();
        }
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        foreach (var header in _headers.Values) header.Invalidate();
        if (SelectedTab is { } tab && _headers.TryGetValue(tab, out var selected))
            HeaderStrip.ScrollControlIntoView(TabPages.IndexOf(tab) == TabCount - 1 ? _newTabButton : selected);
    }

    protected override void WndProc(ref Message m)
    {
        // TCM_ADJUSTRECT: as páginas ocupam também a área do cabeçalho nativo,
        // pois o cabeçalho visível está em HeaderStrip.
        if (m.Msg == 0x1328 && !DesignMode)
        {
            m.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            HeaderStrip.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class TabHeader : Control
    {
        private readonly BrowserTabControl _owner;
        private readonly TabPage _tab;
        private bool _hoverClose;
        private Rectangle CloseBounds => new(Width - LogicalToDeviceUnits(30),
            (Height - LogicalToDeviceUnits(24)) / 2, LogicalToDeviceUnits(24), LogicalToDeviceUnits(24));

        public TabHeader(BrowserTabControl owner, TabPage tab)
        {
            _owner = owner;
            _tab = tab;
            Size = new Size(214, 38);
            Margin = new Padding(0, 3, 6, 2);
            AccessibleName = tab.Text;
            AccessibleRole = AccessibleRole.PageTab;
            TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var selected = _owner.SelectedTab == _tab;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            e.Graphics.Clear(Theme.Chrome);
            var panel = new Rectangle(0, 0, Width - 1, Height - 1);
            using var panelPath = TopRoundedRect(panel, LogicalToDeviceUnits(10));
            using var panelBrush = new SolidBrush(selected ? Theme.Surface : Theme.SurfaceHot);
            e.Graphics.FillPath(panelBrush, panelPath);
            var close = CloseBounds;
            var title = new Rectangle(LogicalToDeviceUnits(10), 0,
                Math.Max(0, close.Left - LogicalToDeviceUnits(14)), Height);
            TextRenderer.DrawText(e.Graphics, _tab.Text, Font, title, Theme.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            using var border = new Pen(selected ? Theme.Accent : Theme.Divider, selected ? 1.35f : 1f);
            e.Graphics.DrawPath(border, panelPath);
            using var pen = new Pen(_hoverClose ? Theme.Ink : Theme.InkMuted,
                Math.Max(1.6f, DeviceDpi / 60f));
            var inset = close.Width / 3;
            e.Graphics.DrawLine(pen, close.Left + inset, close.Top + inset, close.Right - inset, close.Bottom - inset);
            e.Graphics.DrawLine(pen, close.Right - inset, close.Top + inset, close.Left + inset, close.Bottom - inset);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, title);
        }

        private static System.Drawing.Drawing2D.GraphicsPath TopRoundedRect(Rectangle rect, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            var diameter = radius * 2;
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddLine(rect.Right, rect.Top + radius, rect.Right, rect.Bottom);
            path.AddLine(rect.Right, rect.Bottom, rect.Left, rect.Bottom);
            path.AddLine(rect.Left, rect.Bottom, rect.Left, rect.Top + radius);
            path.CloseFigure();
            return path;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hover = CloseBounds.Contains(e.Location);
            if (_hoverClose == hover) return;
            _hoverClose = hover;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverClose = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (CloseBounds.Contains(e.Location))
            {
                if (_tab is BrowserTab tab) _owner.CloseRequested?.Invoke(tab);
                else _owner.AuxiliaryCloseRequested?.Invoke(_tab);
            }
            else
                _owner.SelectedTab = _tab;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                _owner.SelectedTab = _tab;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }
    }
}
