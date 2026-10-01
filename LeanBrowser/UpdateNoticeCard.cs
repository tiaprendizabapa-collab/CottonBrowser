using System.Drawing.Drawing2D;

namespace LeanBrowser;

/// <summary>A nonmodal update invitation shown over the bottom-right corner of the page.</summary>
internal sealed class UpdateNoticeCard : UserControl
{
    private readonly Label _title = new()
    {
        Text = "Atualização disponível", AutoSize = false,
        Font = new Font(Theme.UiFont, 10f, FontStyle.Bold)
    };
    private readonly Label _message = new()
    {
        AutoSize = false, Font = new Font(Theme.UiFont, 9f)
    };
    private readonly CheckBox _neverShow = new()
    {
        Text = "Não mostrar novamente", AutoSize = false,
        Font = new Font(Theme.UiFont, 9f), Cursor = Cursors.Hand
    };
    private readonly Button _update = CreateButton("Atualizar", true);
    private readonly Button _later = CreateButton("Depois", false);
    private readonly Button _close = CreateButton("×", false);

    internal event Action<bool>? UpdateRequested;
    internal event Action<bool>? Dismissed;

    internal UpdateNoticeCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        Size = new Size(350, 158);
        Visible = false;
        TabStop = false;

        _title.SetBounds(18, 12, 284, 25);
        _message.SetBounds(18, 41, 316, 42);
        _neverShow.SetBounds(18, 88, 230, 24);
        _later.SetBounds(225, 118, 106, 30);
        _update.SetBounds(111, 118, 106, 30);
        _close.SetBounds(311, 9, 26, 26);
        _close.Font = new Font(Theme.UiFont, 13f);
        _close.AccessibleName = "Fechar aviso de atualização";

        Controls.AddRange([_title, _message, _neverShow, _update, _later, _close]);
        _update.Click += (_, _) => UpdateRequested?.Invoke(_neverShow.Checked);
        _later.Click += (_, _) => Dismissed?.Invoke(_neverShow.Checked);
        _close.Click += (_, _) => Dismissed?.Invoke(_neverShow.Checked);
        ApplyTheme();
    }

    internal void ShowUpdate(BrowserUpdate update)
    {
        _message.Text = $"CottonBrowser {update.Tag} está pronto.\nO navegador reinicia após atualizar.";
        _neverShow.Checked = false;
        Visible = true;
        BringToFront();
    }

    internal void ShowPreferenceError()
    {
        _message.Text = "Não foi possível salvar sua preferência. Tente novamente.";
    }

    internal void ApplyTheme()
    {
        BackColor = Theme.Surface;
        ForeColor = Theme.Ink;
        _title.ForeColor = Theme.Ink;
        _message.ForeColor = Theme.InkMuted;
        _neverShow.ForeColor = Theme.Ink;
        _neverShow.BackColor = Theme.Surface;
        _update.BackColor = Theme.Accent;
        _update.ForeColor = Theme.IsDark ? Theme.Chrome : Color.White;
        _later.BackColor = Theme.SurfaceHot;
        _later.ForeColor = Theme.Ink;
        _close.BackColor = Theme.Surface;
        _close.ForeColor = Theme.InkMuted;
        Invalidate(true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(Theme.Divider);
        using var path = Draw.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        e.Graphics.DrawPath(border, path);
        using var accent = new SolidBrush(Theme.Accent);
        e.Graphics.FillRectangle(accent, 18, 0, 42, 3);
    }

    private static Button CreateButton(string text, bool primary)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            TabStop = false,
            Font = new Font(Theme.UiFont, 9f, primary ? FontStyle.Bold : FontStyle.Regular),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }
}
