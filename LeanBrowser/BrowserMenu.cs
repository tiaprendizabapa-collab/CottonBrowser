using System.Drawing.Drawing2D;

namespace LeanBrowser;

public static class BrowserMenuPalette
{
    public static Color Background => Theme.IsDark ? Color.FromArgb(32, 33, 36) : Color.White;
    public static Color Hover => Theme.IsDark ? Color.FromArgb(53, 54, 58) : Color.FromArgb(241, 243, 244);
    public static Color Ink => Theme.IsDark ? Color.FromArgb(241, 243, 244) : Color.FromArgb(32, 33, 36);
    public static Color Muted => Theme.IsDark ? Color.FromArgb(189, 193, 198) : Color.FromArgb(95, 99, 104);
    public static Color Divider => Theme.IsDark ? Color.FromArgb(71, 72, 75) : Color.FromArgb(224, 226, 229);
}

/// <summary>Menu nativo com desenho próprio, navegação por teclado e rolagem automática.</summary>
public sealed class BrowserOverflowMenu : ContextMenuStrip
{
    private readonly Font _menuFont = new(Theme.UiFont, 9.5f);
    public BrowserOverflowMenu()
    {
        ShowImageMargin = false;
        ShowCheckMargin = false;
        AutoClose = true;
        AutoSize = true;
        DropShadowEnabled = true;
        Font = _menuFont;
        Renderer = new BrowserMenuRenderer();
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        BackColor = BrowserMenuPalette.Background;
        ForeColor = BrowserMenuPalette.Ink;
        Invalidate(true);
        foreach (var zoom in Items.OfType<BrowserMenuZoom>()) zoom.ApplyTheme();
        foreach (var item in Items.OfType<BrowserMenuItem>())
            if (item.HasDropDownItems && item.DropDown is BrowserOverflowMenu child) child.ApplyTheme();
    }

    public void ConstrainTo(Size availableSize, int dpi)
    {
        var scale = dpi / 96f;
        var padding = Math.Max(2, (int)Math.Round(8 * scale));
        Padding = new Padding(padding, padding, padding, padding);
        var width = Math.Min((int)Math.Round(352 * scale), Math.Max(1, availableSize.Width));
        MaximumSize = new Size(width, Math.Max(1, availableSize.Height));
        var contentWidth = Math.Max(1, width - Padding.Horizontal - 2);
        foreach (ToolStripItem item in Items)
        {
            switch (item)
            {
                case BrowserMenuItem row: row.SetMenuSize(contentWidth, dpi); break;
                case BrowserMenuZoom zoom: zoom.SetMenuWidth(contentWidth); break;
                case ToolStripSeparator:
                    item.AutoSize = false;
                    item.Size = new Size(contentWidth, Math.Max(6, (int)Math.Round(12 * scale)));
                    break;
            }
        }
        PerformLayout();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width < 2 || Height < 2) return;
        var radius = Math.Min(Math.Min(Width, Height) / 2, Math.Max(6, DeviceDpi / 8));
        using var path = Draw.RoundedRect(new Rectangle(0, 0, Width, Height), radius);
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _menuFont.Dispose();
    }
}

public sealed class BrowserMenuItem : ToolStripMenuItem
{
    public string Glyph { get; }
    public bool IsProfile { get; init; }
    public bool IsBanner { get; init; }
    public string Detail { get; set; } = "";
    public string Avatar { get; set; } = "";
    private int _dpi = 96;

    public BrowserMenuItem(string text, string glyph = "", string shortcut = "") : base(text)
    {
        Glyph = glyph;
        ShortcutKeyDisplayString = shortcut;
        AutoSize = false;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Size = new Size(334, 34);
        AccessibleName = text;
        TextChanged += (_, _) => AccessibleName = Text;
    }

    public void SetMenuSize(int width, int dpi)
    {
        _dpi = dpi;
        Size = new Size(width, Scale(IsProfile || IsBanner ? 50 : 34));
    }

    protected override ToolStripDropDown CreateDefaultDropDown() => new BrowserOverflowMenu();

    private int Scale(int value) => (int)Math.Round(value * _dpi / 96f);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(BrowserMenuPalette.Background);
        g.FillRectangle(background, new Rectangle(Point.Empty, Size));
        var card = new Rectangle(0, Scale(2), Width, Height - Scale(4));
        var highlighted = Enabled && (Selected || Pressed);
        if (highlighted || IsProfile || IsBanner)
        {
            using var shape = Draw.RoundedRect(card, Scale(IsProfile || IsBanner ? 12 : 6));
            using var fill = new SolidBrush(IsBanner
                ? Theme.IsDark ? Color.FromArgb(0, 76, 108) : Color.FromArgb(211, 232, 253)
                : highlighted && IsProfile ? Theme.Surface : BrowserMenuPalette.Hover);
            g.FillPath(fill, shape);
        }
        var ink = Enabled ? BrowserMenuPalette.Ink : BrowserMenuPalette.Muted;
        var muted = Enabled ? BrowserMenuPalette.Muted : Theme.InkDisabled;
        var iconBounds = new Rectangle(Scale(10), (Height - Scale(24)) / 2, Scale(24), Scale(24));
        if (IsProfile)
        {
            using var avatar = new SolidBrush(Theme.IsDark ? Color.FromArgb(0, 151, 167) : Color.FromArgb(0, 121, 138));
            g.FillEllipse(avatar, iconBounds);
            using var initialsFont = new Font(Theme.UiFont, 8.5f, FontStyle.Bold);
            TextRenderer.DrawText(g, Avatar, initialsFont, iconBounds, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        else
        {
            using var iconFont = new Font(Theme.IconFont, 11.5f);
            TextRenderer.DrawText(g, Checked ? "\uE73E" : Glyph, iconFont, iconBounds, muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        var right = Width - Scale(14);
        if (HasDropDownItems || IsProfile)
        {
            using var arrow = new Pen(muted, Math.Max(1.3f, _dpi / 80f));
            var x = right - Scale(3);
            var y = Height / 2;
            g.DrawLines(arrow, new Point[] { new(x - Scale(3), y - Scale(3)), new(x, y), new(x - Scale(3), y + Scale(3)) });
            right -= Scale(18);
        }
        else if (!string.IsNullOrEmpty(ShortcutKeyDisplayString))
        {
            var shortcutWidth = TextRenderer.MeasureText(ShortcutKeyDisplayString, Font).Width;
            var shortcutBounds = new Rectangle(Math.Max(Scale(42), right - shortcutWidth), 0, shortcutWidth, Height);
            TextRenderer.DrawText(g, ShortcutKeyDisplayString, Font, shortcutBounds, muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            right = shortcutBounds.Left - Scale(16);
        }
        var textBounds = new Rectangle(Scale(44), 0, Math.Max(0, right - Scale(44)), Height);
        if ((IsProfile || IsBanner) && Detail.Length > 0)
        {
            textBounds.Y = Scale(7);
            textBounds.Height = Scale(19);
            using var detailFont = new Font(Theme.UiFont, 8f);
            var detailBounds = new Rectangle(textBounds.X, Scale(26), textBounds.Width, Scale(17));
            TextRenderer.DrawText(g, Detail, detailFont, detailBounds, muted,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
        TextRenderer.DrawText(g, Text, Font, textBounds, ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

internal sealed class BrowserMenuRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.Item.Selected ? BrowserMenuPalette.Hover : BrowserMenuPalette.Background);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(BrowserMenuPalette.Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        if (bounds.Width < 2 || bounds.Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Draw.RoundedRect(bounds, Math.Min(12, Math.Min(bounds.Width, bounds.Height) / 2));
        using var pen = new Pen(BrowserMenuPalette.Divider);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(BrowserMenuPalette.Divider);
        e.Graphics.DrawLine(pen, 0, e.Item.Height / 2, e.Item.Width, e.Item.Height / 2);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = BrowserMenuPalette.Muted;
        base.OnRenderArrow(e);
    }
}
