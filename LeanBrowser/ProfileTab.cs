namespace LeanBrowser;

/// <summary>Página compacta que mostra o perfil e as permissões da sessão atual.</summary>
public sealed class ProfileTab : TabPage
{
    private readonly AccessControl _access;
    private readonly Panel _card = new() { Height = 210, Padding = new Padding(26) };
    private readonly Label _title = new() { AutoSize = true, Font = new Font(Theme.UiFont, 22f, FontStyle.Bold) };
    private readonly Label _subtitle = new() { AutoSize = true, Font = new Font(Theme.UiFont, 10f) };
    private readonly Label _userCaption = new() { AutoSize = true, Text = "Usuário conectado", Font = new Font(Theme.UiFont, 9f, FontStyle.Bold) };
    private readonly Label _userValue = new() { AutoSize = true };
    private readonly Label _roleCaption = new() { AutoSize = true, Text = "Perfil de acesso", Font = new Font(Theme.UiFont, 9f, FontStyle.Bold) };
    private readonly Label _roleValue = new() { AutoSize = true, Font = new Font(Theme.UiFont, 11f, FontStyle.Bold) };
    private readonly Label _permissionValue = new() { AutoSize = true };

    public ProfileTab(AccessControl access) : base("Perfil")
    {
        _access = access;
        Padding = new Padding(48, 34, 48, 48);
        BackColor = Theme.Chrome;

        _title.Text = "Perfil";
        _subtitle.Text = "Veja a conta conectada e as permissões desta sessão.";
        _userValue.Text = access.UserName;
        _roleValue.Text = access.ModeLabel;
        _permissionValue.Text = access.IsAdvancedMode
            ? "Recursos avançados liberados para este perfil."
            : "Recursos avançados ficam ocultos para este perfil.";

        _card.Controls.Add(_title);
        _card.Controls.Add(_subtitle);
        _card.Controls.Add(_userCaption);
        _card.Controls.Add(_userValue);
        _card.Controls.Add(_roleCaption);
        _card.Controls.Add(_roleValue);
        _card.Controls.Add(_permissionValue);
        Controls.Add(_card);
        Resize += (_, _) => LayoutCard();
        LayoutCard();
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        BackColor = Theme.Chrome;
        _card.BackColor = Theme.Surface;
        _title.ForeColor = Theme.Ink;
        _subtitle.ForeColor = Theme.InkMuted;
        _userCaption.ForeColor = Theme.InkMuted;
        _userValue.ForeColor = Theme.Ink;
        _roleCaption.ForeColor = Theme.InkMuted;
        _roleValue.ForeColor = _access.IsAdvancedMode ? Theme.Shield : Theme.Ink;
        _permissionValue.ForeColor = Theme.InkMuted;
        _card.Invalidate();
    }

    private void LayoutCard()
    {
        _card.Location = new Point(0, 124);
        _card.Width = Math.Max(360, ClientSize.Width);
        _title.Location = new Point(26, 22);
        _subtitle.Location = new Point(26, 57);
        _userCaption.Location = new Point(26, 98);
        _userValue.Location = new Point(180, 97);
        _roleCaption.Location = new Point(26, 130);
        _roleValue.Location = new Point(180, 129);
        _permissionValue.Location = new Point(26, 166);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Theme.Divider);
        e.Graphics.DrawRectangle(border, new Rectangle(_card.Left, _card.Top, _card.Width - 1, _card.Height - 1));
    }
}
