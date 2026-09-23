using System.Drawing.Drawing2D;

namespace LeanBrowser;

/// <summary>Faixa compacta dos favoritos, com excesso acessível pelo botão à direita.</summary>
internal sealed class BookmarksBar : Panel
{
    private readonly List<BookmarkButton> _buttons = new();
    private readonly Dictionary<string, Image> _favicons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _faviconOrder = new();
    private readonly ToolTip _toolTip = new();
    private readonly ContextMenuStrip _overflowMenu = new() { ShowImageMargin = false };
    private readonly Button _more = new()
    {
        Text = "»", AccessibleName = "Mais favoritos", Size = new Size(30, 30),
        FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, TabStop = false,
        Visible = false, UseVisualStyleBackColor = false
    };
    private Control? _trailingControl;

    public event Action<string, bool>? OpenRequested;

    public void SetTrailingControl(Control control)
    {
        if (_trailingControl is not null) Controls.Remove(_trailingControl);
        _trailingControl = control;
        Controls.Add(control);
        control.BringToFront();
        LayoutButtons();
    }

    public BookmarksBar()
    {
        Dock = DockStyle.Top;
        Height = 38;
        Padding = new Padding(8, 3, 8, 3);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer, true);
        _more.FlatAppearance.BorderSize = 0;
        _more.Click += (_, _) => _overflowMenu.Show(_more, new Point(0, _more.Height));
        Controls.Add(_more);
        ApplyTheme();
    }

    public void SetBookmarks(IReadOnlyList<Bookmark> bookmarks)
    {
        SuspendLayout();
        try
        {
            foreach (var button in _buttons) { Controls.Remove(button); button.Dispose(); }
            _buttons.Clear();
            foreach (var bookmark in bookmarks)
            {
                var button = new BookmarkButton(this, bookmark);
                button.MouseUp += (_, e) =>
                {
                    if (e.Button is MouseButtons.Left or MouseButtons.Middle)
                        OpenRequested?.Invoke(bookmark.Url, e.Button == MouseButtons.Middle || ModifierKeys.HasFlag(Keys.Control));
                };
                _toolTip.SetToolTip(button, $"{bookmark.Title}\n{bookmark.Url}");
                _buttons.Add(button);
                Controls.Add(button);
            }
            LayoutButtons();
        }
        finally { ResumeLayout(true); }
    }

    public void RememberFavicon(string url, Image favicon)
    {
        if (IsDisposed || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        var host = uri.Host;
        var copy = new Bitmap(favicon);
        if (_favicons.Remove(host, out var old)) old.Dispose();
        else _faviconOrder.Enqueue(host);
        _favicons[host] = copy;
        while (_favicons.Count > 64 && _faviconOrder.Count > 0)
        {
            var oldest = _faviconOrder.Dequeue();
            if (_favicons.Remove(oldest, out var evicted)) evicted.Dispose();
        }
        foreach (var button in _buttons)
            if (button.Host.Equals(host, StringComparison.OrdinalIgnoreCase)) button.Invalidate();
    }

    internal Image? FaviconFor(string host) => _favicons.GetValueOrDefault(host);

    public void ApplyTheme()
    {
        BackColor = Theme.Chrome;
        _more.BackColor = Theme.Chrome;
        _more.ForeColor = Theme.InkMuted;
        _more.FlatAppearance.MouseOverBackColor = Theme.SurfaceHot;
        _overflowMenu.BackColor = Theme.Surface;
        _overflowMenu.ForeColor = Theme.Ink;
        foreach (var button in _buttons) button.Invalidate();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutButtons();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Divider);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }

    private void LayoutButtons()
    {
        if (IsDisposed || Width <= 0) return;
        var trailingWidth = _trailingControl is null ? 0 : _trailingControl.Width + 4;
        var available = Math.Max(0, ClientSize.Width - Padding.Horizontal - trailingWidth);
        var required = _buttons.Sum(button => button.Width + 3);
        var overflow = required > available;
        if (overflow) available = Math.Max(0, available - _more.Width - 4);
        foreach (var item in _overflowMenu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose();
        _overflowMenu.Items.Clear();
        var x = Padding.Left;
        var y = Math.Max(0, (Height - 1 - 30) / 2);
        var clipped = false;
        foreach (var button in _buttons)
        {
            var fits = !clipped && x + button.Width <= Padding.Left + available;
            button.Visible = fits;
            if (fits) { button.Location = new Point(x, y); x += button.Width + 3; }
            else
            {
                clipped = true;
                var bookmark = button.Bookmark;
                var item = new ToolStripMenuItem(bookmark.Title) { ToolTipText = bookmark.Url };
                item.Click += (_, _) => OpenRequested?.Invoke(bookmark.Url, false);
                _overflowMenu.Items.Add(item);
            }
        }
        _more.Visible = _overflowMenu.Items.Count > 0;
        _more.Location = new Point(Math.Max(Padding.Left, ClientSize.Width - Padding.Right - trailingWidth - _more.Width), y);
        if (_trailingControl is not null)
            _trailingControl.Location = new Point(
                Math.Max(Padding.Left, ClientSize.Width - Padding.Right - _trailingControl.Width),
                Math.Max(0, (Height - _trailingControl.Height) / 2));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var image in _favicons.Values) image.Dispose();
            _toolTip.Dispose();
            _overflowMenu.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class BookmarkButton : Control
    {
        private readonly BookmarksBar _owner;
        private bool _hovered;
        public Bookmark Bookmark { get; }
        public string Host { get; }

        public BookmarkButton(BookmarksBar owner, Bookmark bookmark)
        {
            _owner = owner;
            Bookmark = bookmark;
            Host = new Uri(bookmark.Url).Host;
            var title = string.IsNullOrWhiteSpace(bookmark.Title) ? Host : bookmark.Title;
            Font = new Font(Theme.UiFont, 9f);
            Width = Math.Clamp(TextRenderer.MeasureText(title, Font).Width + 34, 70, 185);
            Height = 30;
            AccessibleRole = AccessibleRole.Link;
            AccessibleName = title;
            Cursor = Cursors.Hand;
            TabStop = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Chrome);
            if (_hovered)
            {
                using var path = Draw.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 7);
                using var brush = new SolidBrush(Theme.SurfaceHot);
                e.Graphics.FillPath(brush, path);
            }

            var favicon = _owner.FaviconFor(Host);
            if (favicon is not null)
                e.Graphics.DrawImage(favicon, new Rectangle(8, 7, 16, 16));
            else
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var brush = new SolidBrush(Theme.SurfaceHot);
                e.Graphics.FillEllipse(brush, 8, 7, 16, 16);
                TextRenderer.DrawText(e.Graphics, Host[..1].ToUpperInvariant(), Font,
                    new Rectangle(8, 7, 16, 16), Theme.InkMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            var title = string.IsNullOrWhiteSpace(Bookmark.Title) ? Host : Bookmark.Title;
            TextRenderer.DrawText(e.Graphics, title, Font,
                new Rectangle(29, 0, Width - 34, Height), Theme.Ink,
                TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Font.Dispose();
            base.Dispose(disposing);
        }
    }
}
