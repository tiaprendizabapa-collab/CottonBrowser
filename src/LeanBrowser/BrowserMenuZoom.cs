using System.Drawing.Drawing2D;

namespace LeanBrowser;

/// <summary>Controles de zoom embutidos no menu, sem fechá-lo a cada ajuste.</summary>
public sealed class BrowserMenuZoom : ToolStripControlHost
{
    private readonly ZoomRow _row;

    public BrowserMenuZoom(Action<int> changeZoom, Action toggleFullscreen)
        : base(new ZoomRow(changeZoom, toggleFullscreen))
    {
        _row = (ZoomRow)Control;
        AutoSize = false;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        AccessibleName = "Zoom e tela cheia";
        SetMenuWidth(320);
        _row.DpiChangedAfterParent += (_, _) => SetMenuWidth(Width);
        ApplyTheme();
    }

    public void SetState(double zoomFactor, bool canZoom, bool isFullscreen) =>
        _row.SetState(zoomFactor, canZoom, isFullscreen);

    /// <param name="devicePixels">Largura útil do menu, já ajustada para o DPI atual.</param>
    public void SetMenuWidth(int devicePixels)
    {
        Size = new Size(Math.Max(1, devicePixels), _row.Pixels(40));
        _row.Size = Size;
    }

    public void ApplyTheme()
    {
        BackColor = BrowserMenuPalette.Background;
        ForeColor = BrowserMenuPalette.Ink;
        _row.ApplyTheme();
    }

    private sealed class ZoomRow : Control
    {
        private readonly ZoomButton _minus = new(ZoomButtonKind.Minus, "Reduzir zoom");
        private readonly ZoomButton _reset = new(ZoomButtonKind.Percent, "Restaurar zoom para 100%");
        private readonly ZoomButton _plus = new(ZoomButtonKind.Plus, "Aumentar zoom");
        private readonly ZoomButton _fullscreen = new(ZoomButtonKind.Fullscreen, "Entrar em tela cheia");
        private readonly ToolTip _tips = new();
        private readonly Font _uiFont = new(Theme.UiFont, 9f);

        public ZoomRow(Action<int> changeZoom, Action toggleFullscreen)
        {
            ArgumentNullException.ThrowIfNull(changeZoom);
            ArgumentNullException.ThrowIfNull(toggleFullscreen);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer, true);
            Margin = Padding.Empty;
            TabStop = false;
            AccessibleName = "Zoom e tela cheia";
            AccessibleRole = AccessibleRole.Grouping;
            Font = _uiFont;
            Controls.AddRange([_minus, _reset, _plus, _fullscreen]);
            _minus.TabIndex = 0;
            _reset.TabIndex = 1;
            _plus.TabIndex = 2;
            _fullscreen.TabIndex = 3;
            _minus.Click += (_, _) => changeZoom(-1);
            _reset.Click += (_, _) => changeZoom(0);
            _plus.Click += (_, _) => changeZoom(1);
            _fullscreen.Click += (_, _) => toggleFullscreen();
            _tips.SetToolTip(_minus, "Reduzir zoom (Ctrl+-)");
            _tips.SetToolTip(_reset, "Restaurar zoom (Ctrl+0)");
            _tips.SetToolTip(_plus, "Aumentar zoom (Ctrl++)");
            SetState(1, false, false);
        }

        public int Pixels(int value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96.0));

        public void SetState(double zoomFactor, bool canZoom, bool isFullscreen)
        {
            if (!double.IsFinite(zoomFactor) || zoomFactor <= 0) zoomFactor = 1;
            _reset.Text = $"{Math.Round(zoomFactor * 100)}%";
            _reset.AccessibleName = $"Restaurar zoom para 100%. Zoom atual: {_reset.Text}";
            _minus.Enabled = _reset.Enabled = _plus.Enabled = canZoom;
            _fullscreen.IsFullscreen = isFullscreen;
            _fullscreen.AccessibleName = isFullscreen ? "Sair da tela cheia" : "Entrar em tela cheia";
            _tips.SetToolTip(_fullscreen, $"{_fullscreen.AccessibleName} (F11)");
            Invalidate(true);
        }

        public void ApplyTheme()
        {
            BackColor = BrowserMenuPalette.Background;
            ForeColor = BrowserMenuPalette.Ink;
            foreach (Control button in Controls)
            {
                button.BackColor = BackColor;
                button.ForeColor = ForeColor;
            }
            Invalidate(true);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            var size = Math.Min(Pixels(28), Math.Max(1, Height - Pixels(8)));
            var y = (Height - size) / 2;
            var right = Width - Pixels(12);
            _fullscreen.SetBounds(right - size, y, size, size);
            right = _fullscreen.Left - Pixels(18);
            _plus.SetBounds(right - size, y, size, size);
            _reset.SetBounds(_plus.Left - Pixels(52), y, Pixels(52), size);
            _minus.SetBounds(_reset.Left - size, y, size, size);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var iconPen = new Pen(BrowserMenuPalette.Muted, Math.Max(1f, DeviceDpi / 96f));
            var x = Pixels(23);
            var y = Height / 2 - Pixels(2);
            var radius = Pixels(4);
            graphics.DrawEllipse(iconPen, x - radius, y - radius, radius * 2, radius * 2);
            graphics.DrawLine(iconPen, x + Pixels(3), y + Pixels(3), x + Pixels(7), y + Pixels(7));
            var labelBounds = new Rectangle(Pixels(46), 0, Math.Max(0, _minus.Left - Pixels(50)), Height);
            TextRenderer.DrawText(graphics, "Zoom", Font, labelBounds, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            using var divider = new Pen(Color.FromArgb(80, BrowserMenuPalette.Muted));
            var separatorX = _fullscreen.Left - Pixels(9);
            graphics.DrawLine(divider, separatorX, Pixels(8), separatorX, Height - Pixels(8));
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (ContainsFocus && keyData is Keys.Left or Keys.Right)
            {
                var focused = Controls.Cast<Control>().FirstOrDefault(button => button.Focused);
                SelectNextControl(focused, keyData == Keys.Right, true, false, true);
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tips.Dispose();
            base.Dispose(disposing);
            if (disposing) _uiFont.Dispose();
        }
    }

    private enum ZoomButtonKind { Minus, Percent, Plus, Fullscreen }

    private sealed class ZoomButton : Button
    {
        private readonly ZoomButtonKind _kind;
        private bool _hot;
        private bool _pressed;
        public bool IsFullscreen { get; set; }

        public ZoomButton(ZoomButtonKind kind, string name)
        {
            _kind = kind;
            AccessibleName = name;
            AccessibleRole = AccessibleRole.PushButton;
            TabStop = true;
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { _pressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
            var hot = Enabled && (_hot || _pressed);
            if (_kind != ZoomButtonKind.Percent || hot)
            {
                using var background = new SolidBrush(hot ? (Theme.IsDark ? ControlPaint.Light(BrowserMenuPalette.Hover, 0.1f) : ControlPaint.Dark(BrowserMenuPalette.Hover, 0.03f)) : BrowserMenuPalette.Hover);
                if (_kind == ZoomButtonKind.Percent)
                {
                    using var path = Draw.RoundedRect(bounds, Math.Min(6, bounds.Height / 2));
                    graphics.FillPath(background, path);
                }
                else graphics.FillEllipse(background, bounds);
            }

            var color = Enabled ? BrowserMenuPalette.Ink : BrowserMenuPalette.Muted;
            var scale = DeviceDpi / 96f;
            using var pen = new Pen(color, 1.4f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var cx = Width / 2f;
            var cy = Height / 2f;
            var radius = 4f * scale;
            if (_kind == ZoomButtonKind.Percent)
                TextRenderer.DrawText(graphics, Text, Font, ClientRectangle, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            else if (_kind is ZoomButtonKind.Minus or ZoomButtonKind.Plus)
            {
                graphics.DrawLine(pen, cx - radius, cy, cx + radius, cy);
                if (_kind == ZoomButtonKind.Plus) graphics.DrawLine(pen, cx, cy - radius, cx, cy + radius);
            }
            else
            {
                var arm = 3f * scale;
                foreach (var horizontal in new[] { -1, 1 })
                foreach (var vertical in new[] { -1, 1 })
                {
                    var cornerX = cx + horizontal * (IsFullscreen ? radius - arm : radius);
                    var cornerY = cy + vertical * (IsFullscreen ? radius - arm : radius);
                    var direction = IsFullscreen ? 1 : -1;
                    graphics.DrawLine(pen, cornerX, cornerY, cornerX + horizontal * arm * direction, cornerY);
                    graphics.DrawLine(pen, cornerX, cornerY, cornerX, cornerY + vertical * arm * direction);
                }
            }

            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(Theme.Accent, Math.Max(1, scale));
                if (_kind == ZoomButtonKind.Percent)
                {
                    using var path = Draw.RoundedRect(bounds, Math.Min(6, bounds.Height / 2));
                    graphics.DrawPath(focus, path);
                }
                else graphics.DrawEllipse(focus, bounds);
            }
        }
    }
}
