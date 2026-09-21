using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace LeanBrowser;

/// <summary>Paleta CottonBrowser: clara, arejada e com contraste suave.</summary>
public static class Theme
{
    public static bool IsDark { get; private set; }
    public static Color Chrome { get; private set; } = Color.FromArgb(0xF7, 0xF9, 0xFC); // barra
    public static Color Surface { get; private set; } = Color.White; // campos e cartões
    public static Color SurfaceHot { get; private set; } = Color.FromArgb(0xEC, 0xF1, 0xF7); // hover
    public static Color Divider { get; private set; } = Color.FromArgb(0xD6, 0xDF, 0xEA);
    public static Color Ink { get; private set; } = Color.FromArgb(0x16, 0x22, 0x33); // texto
    public static Color InkMuted { get; private set; } = Color.FromArgb(0x5E, 0x6D, 0x82); // icones
    public static Color InkDisabled { get; private set; } = Color.FromArgb(0xA9, 0xB5, 0xC4);
    public static Color Accent { get; private set; } = Color.FromArgb(0x1F, 0x6F, 0xD5); // foco
    public static Color Shield { get; private set; } = Color.FromArgb(0x16, 0x8A, 0x5A); // escudo

    public static void SetDark(bool dark)
    {
        IsDark = dark;
        Chrome = dark ? Color.FromArgb(0x14, 0x18, 0x20) : Color.FromArgb(0xF7, 0xF9, 0xFC);
        Surface = dark ? Color.FromArgb(0x21, 0x29, 0x36) : Color.White;
        SurfaceHot = dark ? Color.FromArgb(0x2B, 0x35, 0x44) : Color.FromArgb(0xEC, 0xF1, 0xF7);
        Divider = dark ? Color.FromArgb(0x3A, 0x46, 0x57) : Color.FromArgb(0xD6, 0xDF, 0xEA);
        Ink = dark ? Color.FromArgb(0xF4, 0xF7, 0xFB) : Color.FromArgb(0x16, 0x22, 0x33);
        InkMuted = dark ? Color.FromArgb(0xA9, 0xB6, 0xC8) : Color.FromArgb(0x5E, 0x6D, 0x82);
        InkDisabled = dark ? Color.FromArgb(0x68, 0x76, 0x89) : Color.FromArgb(0xA9, 0xB5, 0xC4);
        Accent = dark ? Color.FromArgb(0x70, 0xA9, 0xFF) : Color.FromArgb(0x1F, 0x6F, 0xD5);
        Shield = dark ? Color.FromArgb(0x63, 0xD3, 0x9A) : Color.FromArgb(0x16, 0x8A, 0x5A);
    }

    public const string UiFont   = "Segoe UI";
    public const string IconFont = "Segoe MDL2 Assets"; // presente desde o Win10
}

/// <summary>Utilitarios de desenho compartilhados.</summary>
public static class Draw
{
    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();

        if (radius <= 0)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// Botao de icone chapado, com realce circular no hover.
/// Desenhado a mao para evitar a pilha de ButtonRenderer/VisualStyles, que
/// alocaria buffers de tema a cada repaint.
/// </summary>
public sealed class ToolButton : Control
{
    private bool _hot;
    private bool _pressed;

    public string Glyph { get; set; } = "";

    public ToolButton(string glyph, string tooltipText, int size = 34)
    {
        Glyph = glyph;
        Size = new Size(size, size);
        TabStop = false;
        BackColor = Theme.Chrome;
        Font = new Font(Theme.IconFont, 11f, FontStyle.Regular, GraphicsUnit.Point);
        AccessibleName = tooltipText;

        SetStyle(ControlStyles.UserPaint
               | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true;  Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true;  Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e)   { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);

        if (Enabled && (_hot || _pressed))
        {
            using var brush = new SolidBrush(_pressed ? Theme.Divider : Theme.SurfaceHot);
            g.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
        }

        var color = Enabled ? Theme.InkMuted : Theme.InkDisabled;
        using var textBrush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        g.DrawString(Glyph, Font, textBrush, new RectangleF(0, 0, Width, Height), format);
    }
}

/// <summary>Botão de favorito desenhado para ficar dentro do omnibox.</summary>
public sealed class FavoriteButton : Control
{
    private bool _hot;
    private bool _pressed;

    public bool IsFavorite { get; private set; }

    public FavoriteButton()
    {
        Size = new Size(38, 34);
        Dock = DockStyle.Right;
        TabStop = false;
        Cursor = Cursors.Hand;
        AccessibleName = "Adicionar aos favoritos";
        BackColor = Theme.Surface;
        SetStyle(ControlStyles.UserPaint
               | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.SupportsTransparentBackColor, true);
    }

    public void SetFavorite(bool favorite)
    {
        if (IsFavorite == favorite) return;
        IsFavorite = favorite;
        AccessibleName = favorite ? "Página adicionada aos favoritos" : "Adicionar aos favoritos";
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Surface);

        if (_hot || _pressed)
        {
            using var hotBrush = new SolidBrush(_pressed ? Theme.SurfaceHot : Color.FromArgb(220, Theme.SurfaceHot));
            g.FillEllipse(hotBrush, 2, 1, Width - 5, Height - 3);
        }

        var center = new PointF(Width / 2f, Height / 2f);
        var points = new PointF[10];
        const float outer = 8.5f;
        const float inner = 4f;
        for (var i = 0; i < points.Length; i++)
        {
            var radius = i % 2 == 0 ? outer : inner;
            var angle = -MathF.PI / 2f + i * MathF.PI / 5f;
            points[i] = new PointF(center.X + MathF.Cos(angle) * radius,
                center.Y + MathF.Sin(angle) * radius);
        }

        using var border = new Pen(IsFavorite ? Theme.Accent : Theme.InkMuted, 1.5f);
        if (IsFavorite)
        {
            using var fill = new SolidBrush(Theme.Accent);
            g.FillPolygon(fill, points);
        }
        g.DrawPolygon(border, points);
    }
}

/// <summary>
/// Container arredondado da barra de endereco. Hospeda um TextBox sem borda.
/// Repinta apenas em foco/hover/resize - nunca por frame.
/// </summary>
public sealed class Omnibox : Panel
{
    public readonly TextBox Input = new();
    public readonly FavoriteButton Favorite = new();

    private readonly Label _shield = new();
    private readonly Font _glyphFont = new(Theme.IconFont, 10f);
    private readonly Font _countFont = new(Theme.UiFont, 8.5f, FontStyle.Bold);

    /// <summary>Fonte de sugestoes. Preencha com o historico, se quiser.</summary>
    public readonly AutoCompleteStringCollection Suggestions = new();

    private bool _focused;
    private bool _hot;

    public Omnibox()
    {
        Height = 36;
        BackColor = Theme.Chrome;
        Padding = new Padding(0);

        SetStyle(ControlStyles.UserPaint
               | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.ResizeRedraw, true);

        _shield.AutoSize = false;
        _shield.Width = 46;
        _shield.Dock = DockStyle.Left;
        _shield.TextAlign = ContentAlignment.MiddleCenter;
        _shield.Font = _glyphFont;
        _shield.ForeColor = Theme.InkMuted;
        _shield.BackColor = Color.Transparent;
        _shield.Text = "\uE72E"; // cadeado
        _shield.Cursor = Cursors.Default;

        Input.BorderStyle = BorderStyle.None;
        Input.Dock = DockStyle.Fill;
        Input.Font = new Font(Theme.UiFont, 10.5f);
        Input.ForeColor = Theme.Ink;
        Input.BackColor = Theme.Surface;
        // CustomSource precisa existir antes de ligar o modo, senao o
        // autocomplete dispara ArgumentException na primeira digitacao.
        Input.AutoCompleteCustomSource = Suggestions;
        Input.AutoCompleteSource = AutoCompleteSource.CustomSource;
        Input.AutoCompleteMode = AutoCompleteMode.SuggestAppend;

        Input.GotFocus  += (_, _) => { _focused = true;  Invalidate(); Input.BackColor = Theme.Surface; _shield.BackColor = Theme.Surface; };
        Input.LostFocus += (_, _) => { _focused = false; Invalidate(); Input.BackColor = Theme.Surface; _shield.BackColor = Color.Transparent; };

        MouseEnter += (_, _) => { _hot = true;  Invalidate(); };
        MouseLeave += (_, _) => { _hot = false; Invalidate(); };

        Controls.Add(Input);
        Controls.Add(_shield);
        Controls.Add(Favorite);

        // Margem interna: o texto nunca encosta na curva.
        Input.Margin = new Padding(0);
    }

    public void SetFavorite(bool favorite) => Favorite.SetFavorite(favorite);

    /// <summary>
    /// Atualiza o indicador a esquerda (cadeado / escudo com contagem).
    /// As fontes sao criadas uma unica vez: alocar Font aqui vazaria handles
    /// GDI a cada navegacao.
    /// </summary>
    public void SetIndicator(bool secure, int blocked)
    {
        if (blocked > 0)
        {
            _shield.Text = "\uE72E  " + blocked;
            if (!ReferenceEquals(_shield.Font, _countFont)) _shield.Font = _countFont;
            _shield.ForeColor = Theme.Shield;
            _shield.Width = 62;
        }
        else
        {
            _shield.Text = secure ? "\uE72E" : "\uE7BA";
            if (!ReferenceEquals(_shield.Font, _glyphFont)) _shield.Font = _glyphFont;
            _shield.ForeColor = secure ? Theme.InkMuted : Color.FromArgb(0xC5, 0x39, 0x29);
            _shield.Width = 46;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        // Um TextBox de linha unica ignora Dock=Fill na vertical, entao
        // centralizamos via Padding. Reserva tambem o raio na ponta direita,
        // para o texto nunca cruzar a curva.
        var inset = Height / 2;
        var line = Input.PreferredHeight;
        var top = Math.Max(0, (Height - line) / 2);
        var bottom = Math.Max(0, Height - line - top);

        Padding = new Padding(0, top, inset, bottom);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Chrome);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Draw.RoundedRect(rect, Height / 2);

        var fill = _focused ? Theme.Surface : (_hot ? Theme.SurfaceHot : Theme.Surface);
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);

        if (_focused)
        {
            using var pen = new Pen(Theme.Accent, 1.6f);
            g.DrawPath(pen, path);
        }
    }
}

/// <summary>Interop minimo para cantos arredondados nativos no Windows 11.</summary>
public static class Native
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int size);

    public static void EnableRoundedCorners(IntPtr hwnd)
    {
        try
        {
            var preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE,
                                  ref preference, sizeof(int));
        }
        catch
        {
            // Windows 10 ou anterior: sem cantos arredondados, sem drama.
        }
    }
}
