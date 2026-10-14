namespace LeanBrowser;

internal static class UiDialogs
{
    public static string[]? Fields(IWin32Window owner, string title, params (string Label, string Value)[] fields)
    {
        using var form = new Form { Text = title, ClientSize = new Size(460, 70 + fields.Length * 72),
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, Font = new Font(Theme.UiFont, 10), BackColor = Theme.Chrome, ForeColor = Theme.Ink };
        var inputs = new List<TextBox>();
        for (var i = 0; i < fields.Length; i++)
        {
            form.Controls.Add(new Label { Text = fields[i].Label, Bounds = new Rectangle(20, 16 + i * 72, 420, 24) });
            var input = new TextBox { Text = fields[i].Value, AccessibleName = fields[i].Label,
                Bounds = new Rectangle(20, 42 + i * 72, 420, 28), BackColor = Theme.Surface, ForeColor = Theme.Ink };
            form.Controls.Add(input); inputs.Add(input);
        }
        var ok = new Button { Text = "Salvar", DialogResult = DialogResult.OK, Bounds = new Rectangle(240, form.ClientSize.Height - 42, 95, 30) };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(345, form.ClientSize.Height - 42, 95, 30) };
        form.Controls.AddRange([ok, cancel]); form.AcceptButton = ok; form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK ? inputs.Select(t => t.Text.Trim()).ToArray() : null;
    }
    public static void Error(IWin32Window owner, Exception error) => MessageBox.Show(owner, error.Message, "CottonBrowser", MessageBoxButtons.OK, MessageBoxIcon.Information);
}
