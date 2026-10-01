using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace LeanBrowser;

/// <summary>Paleta Catppuccin Latte/Mocha: suave, consistente e de alto conforto visual.</summary>
public static class Theme
{
    private static Color? _customAccent;
    public static bool IsDark { get; private set; }
    public static Color Chrome { get; private set; } = Color.FromArgb(0xEF, 0xF1, 0xF5); // Latte base
    public static Color Surface { get; private set; } = Color.White;
    public static Color SurfaceHot { get; private set; } = Color.FromArgb(0xE6, 0xE9, 0xEF);
    public static Color Divider { get; private set; } = Color.FromArgb(0xCC, 0xD0, 0xDA);
    public static Color Ink { get; private set; } = Color.FromArgb(0x4C, 0x4F, 0x69);
    public static Color InkMuted { get; private set; } = Color.FromArgb(0x6C, 0x6F, 0x85);
    public static Color InkDisabled { get; private set; } = Color.FromArgb(0x9C, 0xA0, 0xB0);
    public static Color Accent { get; private set; } = Color.FromArgb(0x88, 0x39, 0xEF);
    public static Color Shield { get; private set; } = Color.FromArgb(0x40, 0xA0, 0x2B);

    public static void SetDark(bool dark)
    {
        IsDark = dark;
        Chrome = dark ? Color.FromArgb(0x1E, 0x1E, 0x2E) : Color.FromArgb(0xEF, 0xF1, 0xF5);
        Surface = dark ? Color.FromArgb(0x31, 0x32, 0x44) : Color.White;
        SurfaceHot = dark ? Color.FromArgb(0x29, 0x2A, 0x3D) : Color.FromArgb(0xE6, 0xE9, 0xEF);
        Divider = dark ? Color.FromArgb(0x45, 0x47, 0x5A) : Color.FromArgb(0xCC, 0xD0, 0xDA);
        Ink = dark ? Color.FromArgb(0xCD, 0xD6, 0xF4) : Color.FromArgb(0x4C, 0x4F, 0x69);
        InkMuted = dark ? Color.FromArgb(0xA6, 0xAD, 0xC8) : Color.FromArgb(0x6C, 0x6F, 0x85);
        InkDisabled = dark ? Color.FromArgb(0x6C, 0x70, 0x86) : Color.FromArgb(0x9C, 0xA0, 0xB0);
        Accent = dark ? Color.FromArgb(0xCB, 0xA6, 0xF7) : Color.FromArgb(0x88, 0x39, 0xEF);
        Shield = dark ? Color.FromArgb(0xA6, 0xE3, 0xA1) : Color.FromArgb(0x40, 0xA0, 0x2B);
        RecalculateAccent();
    }

    public static Color? CustomAccent => _customAccent;

    public static void SetAccent(Color? color)
    {
        _customAccent = color is { } selected
            ? Color.FromArgb(selected.R, selected.G, selected.B)
            : null;
        RecalculateAccent();
    }

    private static void RecalculateAccent()
    {
        if (_customAccent is not { } selected)
        {
            Accent = IsDark ? Color.FromArgb(0xCB, 0xA6, 0xF7) : Color.FromArgb(0x88, 0x39, 0xEF);
            Shield = IsDark ? Color.FromArgb(0xA6, 0xE3, 0xA1) : Color.FromArgb(0x40, 0xA0, 0x2B);
            return;
        }

        // 3:1 mantém o indicador e o anel visíveis sobre a superfície.
        Accent = EnsureContrast(selected, Surface, IsDark ? Color.White : Color.Black);

        // O escudo continua semanticamente verde, mas recebe um leve matiz da
        // cor escolhida e mantém contraste sobre a superfície atual.
        var green = IsDark ? Color.FromArgb(0xA6, 0xE3, 0xA1) : Color.FromArgb(0x40, 0xA0, 0x2B);
        Shield = EnsureContrast(Mix(green, selected, 0.12f), Surface,
            IsDark ? Color.White : Color.Black);
    }

    private static Color EnsureContrast(Color color, Color background, Color target)
    {
        for (var i = 0; i < 20 && Contrast(color, background) < 3.0; i++)
            color = Mix(color, target, 0.10f);
        return color;
    }

    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) =>
            0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

        var x = Luminance(a);
        var y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static Color Mix(Color a, Color b, float amount) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * amount),
        (int)Math.Round(a.G + (b.G - a.G) * amount),
        (int)Math.Round(a.B + (b.B - a.B) * amount));

    private static string AvailableFont(params string[] families)
    {
        using var fonts = new InstalledFontCollection();
        foreach (var family in families)
            if (fonts.Families.Any(item => string.Equals(item.Name, family, StringComparison.OrdinalIgnoreCase)))
                return family;
        return families[^1];
    }

    public static readonly string UiFont = AvailableFont("Segoe UI Variable", "Segoe UI Variable Text", "Segoe UI");
    public static readonly string IconFont = AvailableFont("Segoe Fluent Icons", "Segoe MDL2 Assets");
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
    public readonly ZoomBadge Zoom = new();

    private readonly Label _shield = new();
    private readonly Font _glyphFont = new(Theme.IconFont, 10f);
    private readonly Font _countFont = new(Theme.UiFont, 8.5f, FontStyle.Bold);

    private bool _focused;
    private bool _hot;
    private readonly System.Windows.Forms.Timer _focusTimer = new() { Interval = 15 };
    private readonly System.Windows.Forms.Timer _progressTimer = new() { Interval = 30 };
    private float _focusOpacity;
    private float _focusFrom;
    private float _focusTarget;
    private long _focusStartedAt;
    private float _navigationProgress;
    private bool _navigationLoading;
    private bool _showNavigationProgress;
    private long _progressCompletedAt;

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
        Input.PlaceholderText = "Pesquisar ou digitar endereço";

        _focusTimer.Tick += (_, _) => AdvanceFocusAnimation();
        _progressTimer.Tick += (_, _) => AdvanceNavigationProgress();
        Input.GotFocus  += (_, _) => { AnimateFocus(true); Input.BackColor = Theme.Surface; _shield.BackColor = Theme.Surface; };
        Input.LostFocus += (_, _) => { AnimateFocus(false); Input.BackColor = Theme.Surface; _shield.BackColor = Color.Transparent; };

        MouseEnter += (_, _) => { _hot = true;  Invalidate(); };
        MouseLeave += (_, _) => { _hot = false; Invalidate(); };

        Controls.Add(Input);
        Controls.Add(_shield);
        Controls.Add(Zoom);
        Controls.Add(Favorite);

        // Margem interna: o texto nunca encosta na curva.
        Input.Margin = new Padding(0);
    }

    public void SetFavorite(bool favorite) => Favorite.SetFavorite(favorite);

    private void AnimateFocus(bool focused)
    {
        _focused = focused;
        _focusFrom = _focusOpacity;
        _focusTarget = focused ? 1f : 0f;
        _focusStartedAt = Environment.TickCount64;
        _focusTimer.Start();
        Invalidate();
    }

    private void AdvanceFocusAnimation()
    {
        var t = Math.Clamp((Environment.TickCount64 - _focusStartedAt) / 120f, 0f, 1f);
        var eased = t * t * (3f - 2f * t);
        _focusOpacity = _focusFrom + (_focusTarget - _focusFrom) * eased;
        Invalidate();
        if (t >= 1f) _focusTimer.Stop();
    }

    public void SetNavigationProgress(bool loading)
    {
        if (_navigationLoading == loading) return;
        _navigationLoading = loading;
        if (loading)
        {
            _navigationProgress = 0.06f;
            _showNavigationProgress = true;
        }
        else if (_showNavigationProgress)
        {
            _navigationProgress = 1f;
            _progressCompletedAt = Environment.TickCount64;
        }
        _progressTimer.Start();
        Invalidate();
    }

    private void AdvanceNavigationProgress()
    {
        if (_navigationLoading)
            _navigationProgress = Math.Min(0.9f, _navigationProgress + (0.9f - _navigationProgress) * 0.12f);
        else if (Environment.TickCount64 - _progressCompletedAt >= 180)
        {
            _showNavigationProgress = false;
            _progressTimer.Stop();
        }
        Invalidate();
    }

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

        if (_focusOpacity > 0f)
        {
            using var pen = new Pen(Color.FromArgb((int)(230 * _focusOpacity), Theme.Accent), 1.6f);
            g.DrawPath(pen, path);
        }

        if (_showNavigationProgress)
        {
            var width = Math.Max(0, Width - Height);
            var line = new Rectangle(Height / 2, Height - 3,
                (int)(width * _navigationProgress), 3);
            using var brush = new SolidBrush(Theme.Accent);
            g.FillRectangle(brush, line);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _focusTimer.Dispose();
            _progressTimer.Dispose();
            _glyphFont.Dispose();
            _countFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Indicador de zoom com opacidade desenhada, sem janela transparente.</summary>
public sealed class ZoomBadge : Control
{
    private int _opacity;
    private string _percentage = "100%";
    private bool _hot;

    public ZoomBadge()
    {
        Dock = DockStyle.Right;
        Size = new Size(82, 34);
        Visible = false;
        TabStop = false;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
        BackColor = Theme.Surface;
        AccessibleName = "Zoom: 100%. Clique para redefinir para 100%";
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public void ShowPercentage(double factor)
    {
        _percentage = Math.Round(factor * 100, MidpointRounding.AwayFromZero) + "%";
        AccessibleName = "Zoom: " + _percentage + ". Clique para redefinir para 100%";
        Opacity = 255;
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }

    public int Opacity
    {
        get => _opacity;
        set
        {
            _opacity = Math.Clamp(value, 0, 255);
            Visible = _opacity > 0;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Surface);
        if (_opacity == 0) return;
        if (_hot)
        {
            using var hotPath = Draw.RoundedRect(new Rectangle(1, 2, Width - 3, Height - 5), 9);
            using var hotBrush = new SolidBrush(Theme.SurfaceHot);
            g.FillPath(hotBrush, hotPath);
        }

        using var pen = new Pen(Color.FromArgb(_opacity, Theme.InkMuted), 1.5f);
        g.DrawEllipse(pen, 7, 9, 11, 11);
        g.DrawLine(pen, 17, 19, 22, 24);
        using var textBrush = new SolidBrush(Color.FromArgb(_opacity, Theme.Ink));
        using var format = new StringFormat { LineAlignment = StringAlignment.Center };
        g.DrawString(_percentage, Font, textBrush, new RectangleF(25, 0, Width - 25, Height), format);
    }
}

/// <summary>Interop para janela sem moldura, monitores e efeitos nativos.</summary>
public static class Native
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint SetWindowNoZOrder = 0x0004;
    private const uint SetWindowNoActivate = 0x0010;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MAINWINDOW = 2;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);

    public static void SetMaximizedWorkArea(IntPtr hwnd, IntPtr minMaxInfo)
    {
        if (minMaxInfo == IntPtr.Zero) return;
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;

        var limits = Marshal.PtrToStructure<MinMaxInfo>(minMaxInfo);
        var bounds = WindowLayout.MaximizedBounds(
            Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom),
            Rectangle.FromLTRB(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom));
        limits.MaxPosition = new NativePoint
        {
            X = bounds.X,
            Y = bounds.Y
        };
        limits.MaxSize = new NativePoint
        {
            X = bounds.Width,
            Y = bounds.Height
        };
        Marshal.StructureToPtr(limits, minMaxInfo, false);
    }

    public static void FitWindowToArea(IntPtr hwnd, Rectangle area) =>
        SetWindowPos(hwnd, IntPtr.Zero, area.X, area.Y, area.Width, area.Height,
            SetWindowNoZOrder | SetWindowNoActivate);

    public static void BeginWindowDrag(IntPtr hwnd)
    {
        ReleaseCapture();
        SendMessage(hwnd, 0x00A1, (IntPtr)2, IntPtr.Zero);
    }

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

    public static bool EnableMica(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return false;
        try
        {
            var backdrop = DWMSBT_MAINWINDOW;
            return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE,
                ref backdrop, sizeof(int)) == 0;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    public static void SetDarkCaption(IntPtr hwnd, bool dark)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        try
        {
            var value = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref value, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}
