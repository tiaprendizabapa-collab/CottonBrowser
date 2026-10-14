namespace LeanBrowser;

internal sealed class ProfileManagerDialog : Form
{
    private readonly BrowserProfileStore _store = new(BrowserPaths.Root);
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, DisplayMember = nameof(BrowserProfile.Name), AccessibleName = "Perfis de navegação" };
    public ProfileManagerDialog()
    {
        Text = "Perfis de navegação"; ClientSize = new Size(480, 370); StartPosition = FormStartPosition.CenterParent;
        Font = new Font(Theme.UiFont, 10); BackColor = Theme.Chrome; ForeColor = Theme.Ink; Padding = new Padding(20);
        var note = new Label { Dock = DockStyle.Top, Height = 65, Text = "Cada perfil mantém seus logins, favoritos, histórico e configurações. O perfil escolhido abre em uma janela própria." };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50 };
        foreach (var text in new[] { "Criar perfil", "Renomear", "Abrir perfil" })
        {
            var button = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface, ForeColor = Theme.Ink };
            button.Click += (_, _) =>
            {
                try
                {
                    if (text == "Criar perfil") { var input = UiDialogs.Fields(this, text, ("Nome", "Trabalho")); if (input is not null) { _store.Create(input[0]); Reload(); } }
                    else if (_list.SelectedItem is BrowserProfile profile)
                    {
                        if (text == "Renomear") { var input = UiDialogs.Fields(this, text, ("Nome", profile.Name)); if (input is not null) { _store.Rename(profile.Id, input[0]); Reload(); } }
                        else { if (profile.Id != BrowserPaths.ProfileId) BrowserProfileStore.Open(profile); Close(); }
                    }
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException) { UiDialogs.Error(this, ex); }
            };
            tools.Controls.Add(button);
        }
        _list.BackColor = Theme.Surface; _list.ForeColor = Theme.Ink; Controls.Add(_list); Controls.Add(note); Controls.Add(tools); Reload();
    }
    private void Reload() { _list.Items.Clear(); _list.Items.AddRange(_store.Load().ToArray()); _list.SelectedItem = _list.Items.Cast<BrowserProfile>().FirstOrDefault(p => p.Id == BrowserPaths.ProfileId); }
}
