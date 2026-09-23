using System.Drawing.Drawing2D;

namespace LeanBrowser;

/// <summary>Faixa horizontal de abas, separada do conteúdo WebView2.</summary>
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
        Text = "", AutoSize = false, Height = 38, Width = 36,
        TextAlign = ContentAlignment.MiddleCenter, ImageAlign = ContentAlignment.MiddleCenter,
        Padding = new Padding(0),
        BackColor = Theme.Chrome
    };
    private Image? _brandIcon;
    private readonly ToolTip _toolTip = new();
    public FlowLayoutPanel HeaderStrip { get; } = new()
    {
        Height = 50, WrapContents = false, FlowDirection = FlowDirection.LeftToRight,
        AutoScroll = true, BackColor = Theme.Chrome, Padding = new Padding(4, 3, 8, 3)
    };
    public int BrowserTabCount => TabPages.OfType<BrowserTab>().Count();
    public event Action<BrowserTab>? CloseRequested;
    public event Action<TabPage>? AuxiliaryCloseRequested;
    public event Action? NewTabRequested;
    public event Action? BrandClicked;

    // Recebe a propriedade da imagem; o cabeçalho anterior é libertado.
    public void SetFavicon(TabPage tab, Image? favicon)
    {
        if (_headers.TryGetValue(tab, out var header)) header.SetFavicon(favicon);
        else favicon?.Dispose();
    }

    public void ApplyTheme()
    {
        HeaderStrip.BackColor = Theme.Chrome;
        _brandLabel.BackColor = Theme.Chrome;
        _newTabButton.BackColor = Theme.Chrome;
        _newTabButton.ForeColor = Theme.Ink;
        _newTabButton.FlatAppearance.MouseOverBackColor = Theme.SurfaceHot;
        HeaderStrip.Invalidate(true);
        foreach (var header in _headers.Values) header.Invalidate();
    }

    public BrowserTabControl()
    {
        // O TabControl mantém as páginas; HeaderStrip exibe as abas acima do conteúdo.
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(1, 1);
        Appearance = TabAppearance.FlatButtons;
        _newTabButton.FlatAppearance.BorderSize = 0;
        _newTabButton.FlatAppearance.MouseOverBackColor = Theme.SurfaceHot;
        _newTabButton.Paint += (_, e) =>
        {
            var center = new Point(_newTabButton.Width / 2, _newTabButton.Height / 2);
            var arm = Math.Max(5, _newTabButton.LogicalToDeviceUnits(6));
            using var pen = new Pen(Theme.Ink, Math.Max(1.4f, _newTabButton.DeviceDpi / 72f));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawLine(pen, center.X - arm, center.Y, center.X + arm, center.Y);
            e.Graphics.DrawLine(pen, center.X, center.Y - arm, center.X, center.Y + arm);
        };
        _newTabButton.Click += (_, _) => NewTabRequested?.Invoke();
        _toolTip.SetToolTip(_newTabButton, "Nova aba (Ctrl+T)");
        HeaderStrip.Controls.Add(_brandLabel);
        HeaderStrip.Controls.Add(_newTabButton);
        HeaderStrip.Paint += PaintHeaderEdge;
        _brandLabel.Cursor = Cursors.Hand;
        _brandLabel.Click += (_, _) => BrandClicked?.Invoke();
        _toolTip.SetToolTip(_brandLabel, "CottonBrowser — clique 5 vezes rapidamente");
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null)
            {
                _brandIcon = new Bitmap(24, 24);
                using var graphics = Graphics.FromImage(_brandIcon);
                graphics.DrawIcon(icon, new Rectangle(0, 0, 24, 24));
                _brandLabel.Image = _brandIcon;
            }
        }
        catch { }
    }

    private static void PaintHeaderEdge(object? sender, PaintEventArgs e)
    {
        if (sender is not Control control || control.ClientSize.Height < 3 || control.ClientSize.Width < 1) return;
        var edge = new Rectangle(0, control.ClientSize.Height - 3, control.ClientSize.Width, 3);
        using var brush = new LinearGradientBrush(edge,
            Color.FromArgb(0, Color.Black), Color.FromArgb(Theme.IsDark ? 48 : 28, Color.Black),
            LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(brush, edge);
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
            _brandIcon?.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class TabHeader : Control
    {
        private readonly BrowserTabControl _owner;
        private readonly TabPage _tab;
        private Image? _favicon;
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

        public void SetFavicon(Image? favicon)
        {
            var previous = _favicon;
            _favicon = favicon;
            previous?.Dispose();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var selected = _owner.SelectedTab == _tab;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            e.Graphics.Clear(Theme.Chrome);
            var panel = new Rectangle(0, 0, Width - 1, Height - 1);
            using var panelPath = Draw.RoundedRect(panel, LogicalToDeviceUnits(11));
            using var panelBrush = new SolidBrush(selected ? Theme.Surface : Theme.SurfaceHot);
            e.Graphics.FillPath(panelBrush, panelPath);
            var close = CloseBounds;
            var iconSize = LogicalToDeviceUnits(16);
            var iconLeft = LogicalToDeviceUnits(12);
            var titleLeft = iconLeft + iconSize + LogicalToDeviceUnits(8);
            if (_favicon is not null)
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(_favicon,
                    new Rectangle(iconLeft, (Height - iconSize) / 2, iconSize, iconSize));
            }
            var title = new Rectangle(titleLeft, 0,
                Math.Max(0, close.Left - titleLeft - LogicalToDeviceUnits(4)), Height);
            TextRenderer.DrawText(e.Graphics, _tab.Text, Font, title, Theme.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            using var border = new Pen(Theme.Divider, 1f);
            e.Graphics.DrawPath(border, panelPath);
            if (selected)
            {
                using var accent = new SolidBrush(Theme.Accent);
                e.Graphics.FillRectangle(accent, LogicalToDeviceUnits(12), 1,
                    Math.Max(0, Width - LogicalToDeviceUnits(24)), LogicalToDeviceUnits(3));
            }
            if (_hoverClose)
            {
                using var halo = new SolidBrush(Theme.SurfaceHot);
                e.Graphics.FillEllipse(halo, close);
            }
            using var pen = new Pen(_hoverClose ? Theme.Ink : Theme.InkMuted,
                Math.Max(1.6f, DeviceDpi / 60f));
            var inset = close.Width / 3;
            e.Graphics.DrawLine(pen, close.Left + inset, close.Top + inset, close.Right - inset, close.Bottom - inset);
            e.Graphics.DrawLine(pen, close.Right - inset, close.Top + inset, close.Left + inset, close.Bottom - inset);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, title);
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) _favicon?.Dispose();
            base.Dispose(disposing);
        }
    }
}
