using Microsoft.Web.WebView2.Core;

namespace LeanBrowser;

internal sealed class SiteControlsDialog : Form
{
    public SiteControlsDialog(string origin, bool secure, PermissionPolicy policy, bool isPrivate,
        Func<string, bool, Task> setBlocked, Func<Task<int>> clearCookies)
    {
        Text = "Permissões e dados do site"; ClientSize = new Size(520, 450); MinimumSize = new Size(450, 420);
        StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false;
        Font = new Font(Theme.UiFont, 10); BackColor = Theme.Chrome; ForeColor = Theme.Ink;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16) };
        FeatureUi.WrapNotes(panel);
        panel.Controls.Add(FeatureUi.Note(origin));
        panel.Controls.Add(FeatureUi.Note(secure ? "As permissões são pedidas a cada uso. Bloqueios abaixo valem durante esta sessão."
            : "Câmera, microfone, localização e notificações são bloqueados em páginas sem HTTPS."));
        foreach (var (kind, label) in new[] { ("Camera", "Câmera"), ("Microphone", "Microfone"), ("Geolocation", "Localização"), ("Notifications", "Notificações") })
        {
            var last = policy.LastDecision(origin, kind, isPrivate);
            var check = new CheckBox { AutoSize = true, Text = "Bloquear " + label.ToLowerInvariant(), Checked = !secure || policy.IsBlocked(origin, kind, isPrivate), Enabled = secure };
            panel.Controls.Add(check);
            var state = FeatureUi.Note(label + ": " + (last is true ? "permitida no último pedido" : last is false ? "negada no último pedido" : "nenhum pedido nesta sessão"));
            state.Margin = new Padding(24, 0, 4, 8); panel.Controls.Add(state);
            check.CheckedChanged += async (_, _) =>
            {
                check.Enabled = false;
                try { await setBlocked(kind, check.Checked); state.Text = check.Checked ? label + ": bloqueada; abas do site recarregadas para encerrar o uso." : label + ": perguntar no próximo uso."; }
                catch (Exception ex) { MessageBox.Show(this, "Não foi possível alterar a permissão: " + ex.Message); }
                finally { if (!IsDisposed) check.Enabled = true; }
            };
        }
        var clear = FeatureUi.Button("Apagar cookies deste site", () => {});
        var status = FeatureUi.Note("Inclui cookies compartilhados pelo domínio. Isso pode sair da sua conta.");
        clear.Click += async (_, _) =>
        {
            clear.Enabled = false;
            try { var count = await clearCookies(); status.Text = $"{count} cookies apagados. Recarregue a página para aplicar."; }
            catch (Exception ex) { status.Text = "Não foi possível apagar: " + ex.Message; }
            finally { if (!IsDisposed) clear.Enabled = true; }
        };
        panel.Controls.Add(clear); panel.Controls.Add(status); Controls.Add(panel);
    }
}
