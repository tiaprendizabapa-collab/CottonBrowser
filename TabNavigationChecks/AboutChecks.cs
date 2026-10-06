using System.Reflection;
using LeanBrowser;

internal static class AboutChecks
{
    internal static void Run()
    {
        using var host = new PreviewHost { ClientSize = new Size(1150, 850), Opacity = 0, ShowInTaskbar = false };
        using var tabs = new TabControl { Dock = DockStyle.Fill };
        using var settings = new SettingsTab(_ => { }, () => { }, () => Array.Empty<Bookmark>(),
            () => { }, _ => { }, false, _ => { }, "Preview");
        host.Controls.Add(tabs); tabs.TabPages.Add(settings);
        _ = host.Handle; _ = tabs.Handle; _ = settings.Handle;
        host.Show(); Application.DoEvents();
        settings.ShowAboutUpdates();
        var view = Field<Control>(settings, "_aboutUpdates");
        var installed = new Version(1, 0, 33, 0);
        var latest = new Version(1, 0, 34, 0);
        var release = New("BrowserReleaseInfo", latest, "v1.0.34",
            "## Novidades\n\n- Nova tela Sobre e atualizações.\n- Versão instalada e última verificação.\n- Notas da publicação disponíveis após reiniciar.");
        var history = New("UpdateCheckHistory", DateTimeOffset.Now, true, release);
        var update = New("BrowserUpdate", latest, "v1.0.34",
            new Uri("https://github.com/tiaprendizabapa-collab/CottonBrowser/releases/download/v1.0.34/CottonBrowser-win-x64.zip"),
            new string('0', 64), 100L);
        var configure = typeof(SettingsTab).GetMethod("ConfigureUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void State(object? check, object? available, bool busy = false, string? activity = null) =>
            configure.Invoke(settings, [installed, check, available, busy, activity]);
        State(null, null);
        Check(Field<Label>(view, "_lastCheck").Text.Contains("ainda não"), "Primeira abertura informa ausência de verificação.");
        Check(!Field<Button>(view, "_install").Visible, "Instalar fica oculto sem uma versão confirmada.");
        State(history, update);
        Check(Field<Label>(view, "_status").Text.Contains("1.0.34"), "Versão disponível aparece na tela.");
        Check(Field<TextBox>(view, "_notes").Text.Contains("Nova tela") && !Field<TextBox>(view, "_notes").Text.StartsWith("##"), "Notas são exibidas como texto legível.");
        Check(Field<Button>(view, "_install").Visible, "Instalar fica visível para uma versão confirmada.");
        var checks = 0; var installs = 0;
        settings.UpdateCheckRequested += () => checks++;
        settings.UpdateInstallRequested += () => installs++;
        Click(Field<Button>(view, "_check")); Click(Field<Button>(view, "_install"));
        Check(checks == 1 && installs == 1, "Botões encaminham os pedidos ao navegador.");
        State(history, update, true, "Verificando atualizações...");
        Check(!Field<Button>(view, "_check").Enabled && !Field<Button>(view, "_install").Enabled, "Operação em andamento impede pedidos repetidos.");
        State(New("UpdateCheckHistory", DateTimeOffset.Now, false, release), null);
        Check(Field<Label>(view, "_status").Text.Contains("Não foi possível"), "Falha de consulta não é apresentada como atualizado.");
        Check(Field<TextBox>(view, "_notes").Text.Contains("Nova tela"), "Falha de consulta mantém as notas anteriores.");
        State(New("UpdateCheckHistory", DateTimeOffset.Now, true,
            New("BrowserReleaseInfo", installed, "v1.0.33", "")), null);
        Check(Field<Label>(view, "_status").Text.Contains("está atualizado"), "Versão atual é identificada corretamente.");
        Check(Field<TextBox>(view, "_notes").Text.Contains("não contém notas"), "Publicação sem notas recebe mensagem explícita.");
        State(history, update);
        foreach (var dark in new[] { false, true })
        {
            Theme.SetDark(dark); settings.ApplyTheme();
            host.ClientSize = new Size(1150, 850); host.PerformLayout(); settings.PerformLayout();
            Capture(settings, dark ? "about-updates-dark" : "about-updates-light");
        }
        host.ClientSize = new Size(560, 750); host.PerformLayout(); settings.PerformLayout();
        Capture(settings, "about-updates-narrow");
        Check(Field<Button>(view, "_install").Top >= Field<Button>(view, "_check").Bottom,
            "Em janela estreita, os botões ficam em linhas separadas.");
        Check(Field<Label>(view, "_notesTitle").Top >= Field<Button>(view, "_install").Bottom, "Notas não sobrepõem os botões em janela estreita.");
        foreach (Control child in view.Controls)
            Check(child.Left >= 0 && child.Right <= view.Width && child.Top >= 0 && child.Bottom <= view.Height,
                "Controles da tela devem caber no painel: " + child.Text);
        Console.WriteLine("PASS: Sobre e atualizações, estados, botões, notas e layout claro/escuro/estreito.");
    }

    private sealed class PreviewHost : Form { protected override bool ShowWithoutActivation => true; }
    private static object New(string name, params object?[] arguments) => Activator.CreateInstance(
        typeof(SettingsTab).Assembly.GetType("LeanBrowser." + name)!, arguments)!;
    private static T Field<T>(object instance, string name) => (T)instance.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static void Click(Button button) => typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(button, [EventArgs.Empty]);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Capture(SettingsTab page, string name)
    {
        var path = Path.Combine(Path.GetTempPath(), name + ".png");
        using var bitmap = new Bitmap(page.Width, page.Height);
        page.DrawToBitmap(bitmap, page.ClientRectangle);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        var colors = new HashSet<int>();
        for (var y = 0; y < bitmap.Height; y += 3)
        for (var x = 0; x < bitmap.Width; x += 3) colors.Add(bitmap.GetPixel(x, y).ToArgb());
        Check(colors.Count > 20, "Preview deve conter os controles da tela.");
        Console.WriteLine("PREVIEW: " + path);
    }
}
