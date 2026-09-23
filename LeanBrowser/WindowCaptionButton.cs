using System.Drawing.Drawing2D;

namespace LeanBrowser;

internal enum WindowCaptionAction { Minimize, Maximize, Close }

internal sealed class WindowCaptionButton : Control
{
    private readonly WindowCaptionAction _action;
    private bool _hovered;
    private bool _pressed;
    private bool _restore;

    public bool RestoreIcon
    {
        get => _restore;
        set { if (_restore != value) { _restore = value; Invalidate(); } }
    }

    public WindowCaptionButton(WindowCaptionAction action)
    {
        _action = action;
        Size = new Size(46, 50);
        TabStop = false;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = action switch
        {
            WindowCaptionAction.Minimize => "Minimizar",
            WindowCaptionAction.Maximize => "Maximizar",
            _ => "Fechar"
        };
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.StandardClick, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var isCloseHot = _action == WindowCaptionAction.Close && _hovered;
        var background = isCloseHot ? Color.FromArgb(_pressed ? 156 : 196, 43, 50)
            : _hovered ? Theme.SurfaceHot : Theme.Chrome;
        e.Graphics.Clear(background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(isCloseHot ? Color.White : Theme.Ink,
            Math.Max(1.3f, DeviceDpi / 96f * 1.35f));
        var x = Width / 2f;
        var y = Height / 2f;
        switch (_action)
        {
            case WindowCaptionAction.Minimize:
                e.Graphics.DrawLine(pen, x - 5, y + 2, x + 5, y + 2);
                break;
            case WindowCaptionAction.Maximize:
                if (_restore)
                {
                    e.Graphics.DrawRectangle(pen, x - 2, y - 6, 9, 9);
                    using var cover = new SolidBrush(background);
                    e.Graphics.FillRectangle(cover, x - 7, y - 2, 11, 10);
                    e.Graphics.DrawRectangle(pen, x - 6, y - 2, 9, 9);
                }
                else e.Graphics.DrawRectangle(pen, x - 5, y - 5, 10, 10);
                break;
            case WindowCaptionAction.Close:
                e.Graphics.DrawLine(pen, x - 5, y - 5, x + 5, y + 5);
                e.Graphics.DrawLine(pen, x + 5, y - 5, x - 5, y + 5);
                break;
        }
    }
}
