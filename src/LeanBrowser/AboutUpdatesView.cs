using System.Globalization;
using System.Text.RegularExpressions;

namespace LeanBrowser;

internal sealed class AboutUpdatesView : UserControl
{
    private readonly Label _version = Label("", 17f, true);
    private readonly Label _status = Label("", 11f, true);
    private readonly Label _lastCheck = Label("", 9.5f);
    private readonly Label _notesTitle = Label("Novidades", 11f, true);
    private readonly TextBox _notes = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None, Font = new Font(Theme.UiFont, 10f),
        AccessibleName = "Notas da publicação", WordWrap = true
    };
    private readonly Button _check = Button("Verificar atualizações");
    private readonly Button _install = Button("Instalar atualização");
    private bool _current;
    private bool _hasUpdate;

    public event Action? CheckRequested;
    public event Action? InstallRequested;

    public AboutUpdatesView()
    {
        Controls.AddRange(new Control[] { _version, _status, _lastCheck, _check, _install, _notesTitle, _notes });
        _check.Click += (_, _) => CheckRequested?.Invoke();
        _install.Click += (_, _) => InstallRequested?.Invoke();
        _install.Visible = false;
        ApplyTheme();
    }

    internal void SetState(Version installed, UpdateCheckHistory? history, BrowserUpdate? available,
        bool busy, string? activity)
    {
        _version.Text = "Versão instalada: " + FormatVersion(installed);
        _lastCheck.Text = history is null ? "Última verificação: ainda não realizada."
            : "Última verificação: " + history.CheckedAt.ToLocalTime().ToString("dd/MM/yyyy 'às' HH:mm", CultureInfo.GetCultureInfo("pt-BR"))
                + (history.Succeeded ? "." : " — não foi possível consultar as versões.");
        _current = !busy && history?.Succeeded == true && history.Release?.Version == installed;
        _status.Text = busy ? activity ?? "Verificando atualizações…"
            : history?.Succeeded == false ? "Não foi possível verificar as atualizações. Tente novamente."
            : available is not null ? "Atualização disponível: " + FormatVersion(available.Version)
            : history?.Release is not { } release ? "Verifique se há uma nova versão do CottonBrowser."
            : release.Version > installed ? "Uma versão mais recente foi consultada. Verifique novamente para instalar."
            : release.Version < installed ? "A versão instalada é mais recente que a última publicação consultada."
            : "Seu navegador está atualizado conforme a última verificação.";
        _check.Enabled = !busy;
        _hasUpdate = available is not null;
        _install.Visible = _hasUpdate;
        _install.Enabled = !busy && available is not null;
        _install.Text = available is null ? "Instalar atualização" : "Instalar versão " + FormatVersion(available.Version);
        var latest = history?.Release;
        _notesTitle.Text = latest is null ? "Novidades das versões"
            : (latest.Version > installed ? "Novidades da versão " : "Notas da publicação ") + FormatVersion(latest.Version);
        var text = latest is null ? "As novidades aparecerão aqui após uma verificação bem-sucedida."
            : string.IsNullOrWhiteSpace(latest.Notes) ? "Esta publicação não contém notas de versão."
            : PlainNotes(latest.Notes);
        if (_notes.Text != text) _notes.Text = text;
        ApplyTheme();
        PerformLayout();
    }

    internal int HeightForWidth(int width)
    {
        var y = D(4);
        foreach (var label in new[] { _version, _status, _lastCheck })
        {
            var height = TextRenderer.MeasureText(label.Text, label.Font, new Size(Math.Max(1, width), 0),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + D(5);
            label.SetBounds(0, y, width, height);
            y += height + D(12);
        }
        var checkWidth = Math.Min(width, D(200));
        _check.SetBounds(0, y, checkWidth, D(38));
        var narrow = width < D(450);
        _install.SetBounds(narrow ? 0 : checkWidth + D(12), narrow ? y + D(48) : y,
            Math.Min(width, D(220)), D(38));
        y += D(_hasUpdate && narrow ? 108 : 60);
        _notesTitle.SetBounds(0, y, width, D(32));
        _notes.SetBounds(0, y + D(42), width, D(200));
        return _notes.Bottom + D(8);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_version is not null && Width > 0) HeightForWidth(Width);
    }

    public void ApplyTheme()
    {
        BackColor = Theme.Surface;
        foreach (var label in new[] { _version, _status, _notesTitle }) label.ForeColor = Theme.Ink;
        _status.ForeColor = _current ? Theme.Shield : Theme.Ink;
        _lastCheck.ForeColor = Theme.InkMuted;
        _notes.BackColor = Theme.Surface; _notes.ForeColor = Theme.Ink;
        foreach (var button in new[] { _check, _install })
        {
            button.BackColor = Theme.SurfaceHot; button.ForeColor = Theme.Ink;
            button.FlatAppearance.BorderColor = Theme.Divider;
            button.FlatAppearance.MouseOverBackColor = Theme.Chrome;
        }
    }

    private int D(int value) => (int)Math.Round(value * DeviceDpi / 96f);
    private static string FormatVersion(Version version) => version.ToString(version.Revision > 0 ? 4 : 3);
    private static string PlainNotes(string notes) => Regex.Replace(notes.Replace("\r\n", "\n"),
        @"(?m)^#{1,6}\s+", "").Replace("\n", Environment.NewLine);
    private static Label Label(string text, float size, bool bold = false) => new()
    {
        Text = text, Font = new Font(Theme.UiFont, size, bold ? FontStyle.Bold : FontStyle.Regular),
        AutoSize = false, BackColor = Color.Transparent
    };
    private static Button Button(string text) => new()
    {
        Text = text, AccessibleName = text, FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand, Font = new Font(Theme.UiFont, 9.5f), UseVisualStyleBackColor = false
    };
}
