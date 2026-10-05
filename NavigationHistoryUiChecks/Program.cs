using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;
using LeanBrowser;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        Application.EnableVisualStyles();
        var output = Path.GetFullPath(".verification");
        Directory.CreateDirectory(output);
        var path = Path.Combine(output, "preview-history.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new[]
        {
            new NavigationHistoryEntry("https://example.com/relatorio", "Relatório de produção", DateTimeOffset.Now),
            new NavigationHistoryEntry("https://www.bing.com/search?q=algodao", "Pesquisa por algodão", DateTimeOffset.Now.AddHours(-1)),
            new NavigationHistoryEntry("https://duckduckgo.com/?q=colheita", "Calendário de colheita", DateTimeOffset.Now.AddDays(-1))
        }));
        foreach (var dark in new[] { false, true })
        {
            Theme.SetDark(dark);
            foreach (var width in new[] { 560, 1200 })
            {
                using var settings = new SettingsTab(_ => { }, () => { }, () => Array.Empty<Bookmark>(), () => { }, _ => { }, true, _ => { }, "Exemplo");
                settings.ConfigureHistory(new NavigationHistoryStore(path));
                using var form = Host(settings, width);
                foreach (var section in new[] { "Inicialização", "Desempenho", "Pesquisa" })
                {
                    typeof(SettingsTab).GetMethod("SelectSection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, [section]);
                    Pump();
                    var body = Get<Panel>(settings, "_body");
                    foreach (Control card in body.Controls)
                    {
                        if (!card.Visible || card is Label) continue;
                        foreach (Control control in card.Controls)
                            Check(control.Left >= 0 && control.Top >= 0 && control.Right <= card.ClientSize.Width && control.Bottom <= card.ClientSize.Height,
                                $"{section}, {width}px: {control.GetType().Name} saiu do cartão.");
                    }
                    Save(settings, Path.Combine(output, $"settings-{section}-{width}-{(dark ? "dark" : "light")}.png"));
                }
                var tabs = (TabControl)settings.Parent!;
                var count = tabs.TabPages.Count;
                var navigation = Get<FlowLayoutPanel>(settings, "_nav");
                var historyButton = navigation.Controls.OfType<Button>().Single(button => button.Text == "Histórico");
                historyButton.PerformClick();
                Pump();
                var view = Get<HistoryView>(settings, "_historyView");
                Check(tabs.TabPages.Count == count && tabs.SelectedTab == settings, "O histórico da barra lateral deve permanecer na mesma aba de configurações.");
                Check(view.Visible && Get<Label>(settings, "_title").Text == "Histórico", "O histórico deve ocupar o painel das configurações.");
                Check((bool)historyButton.GetType().GetProperty("Selected")!.GetValue(historyButton)!, "Histórico deve ficar destacado na barra lateral.");
                Get<ComboBox>(view, "_period").SelectedIndex = 5;
                Pump();
                CheckHistory(view, width);
                Check(view.Left >= 0 && view.Top >= 0 && view.Right <= view.Parent!.ClientSize.Width && view.Bottom <= view.Parent.ClientSize.Height,
                    "O histórico deve caber no cartão das configurações.");
                Save(settings, Path.Combine(output, $"settings-Histórico-{width}-{(dark ? "dark" : "light")}.png"));
                Get<TextBox>(view, "_search").Text = "relatório";
                Pump(); Pump();
                Check(Get<ListView>(view, "_list").VirtualListSize == 1, "A pesquisa interna deve filtrar as visitas.");
                string? opened = null;
                settings.HistoryEntrySelected += url => opened = url;
                Get<ListView>(view, "_list").SelectedIndices.Add(0);
                Get<Button>(view, "_open").PerformClick();
                Check(opened == "https://example.com/relatorio", "Abrir uma visita deve encaminhar o endereço ao navegador.");
                navigation.Controls.OfType<Button>().Single(button => button.Text == "Inicialização").PerformClick();
                Pump();
                Check(!view.Visible && Get<Label>(settings, "_title").Text == "Inicialização", "Mudar de seção deve esconder o histórico.");
                historyButton.PerformClick(); Pump();
                Check(view.Visible && tabs.TabPages.Count == count, "Voltar ao histórico deve reutilizar o painel sem criar abas.");
                Get<TextBox>(settings, "_searchInput").Text = "histórico";
                Pump();
                Check(view.Visible, "A pesquisa das configurações deve encontrar o cartão de histórico.");
                Get<TextBox>(settings, "_searchInput").Text = "palavra-sem-resultado";
                Pump();
                Check(!view.Visible, "O cartão de histórico deve respeitar a pesquisa das configurações.");
            }
            foreach (var width in new[] { 560, 1200 })
            {
                var store = new NavigationHistoryStore(path);
                using var history = new HistoryTab(store);
                using var form = Host(history, width);
                var view = history.Controls.OfType<HistoryView>().Single();
                Get<ComboBox>(view, "_period").SelectedIndex = 5;
                Pump();
                CheckHistory(view, width);
                Save(history, Path.Combine(output, $"history-{width}-{(dark ? "dark" : "light")}.png"));
            }
        }
        Console.WriteLine("PASS: histórico dentro das configurações sem criar abas, busca e abertura de visitas, troca de seções; aba de histórico preservada; layouts de 560 e 1200 px, temas claro e escuro; previews em .verification.");
    }

    private static void CheckHistory(HistoryView view, int width)
    {
        foreach (var field in new[] { "_tools", "_rangeTools" })
        {
            var flow = Get<FlowLayoutPanel>(view, field);
            foreach (Control child in flow.Controls)
                if (child.Visible) Check(child.Right <= flow.ClientSize.Width && child.Bottom <= flow.ClientSize.Height,
                    $"Histórico, {width}px: {child.GetType().Name} saiu da linha de ferramentas.");
        }
        var list = Get<ListView>(view, "_list");
        Check(list.VirtualListSize == 3, "As visitas devem aparecer na lista virtual.");
        Check(list.Height >= 100, "A lista de visitas deve ter espaço abaixo das ferramentas.");
    }

    private static Form Host(TabPage page, int width)
    {
        var form = new Form { ClientSize = new Size(width, 760), Opacity = 0, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) };
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(page); form.Controls.Add(tabs);
        form.Show(); Pump();
        return form;
    }

    private static void Pump()
    {
        var until = Environment.TickCount64 + 250;
        do { Application.DoEvents(); Thread.Sleep(10); } while (Environment.TickCount64 < until);
    }

    private static T Get<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static void Save(Control control, string path)
    {
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path, ImageFormat.Png);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
