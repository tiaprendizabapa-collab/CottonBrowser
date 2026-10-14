using System.Text.Json;
using LeanBrowser;
using CottonBrowser.Shared;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CottonSessionChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "session.json");
            var store = new BrowserSessionStore(path);
            Check(store.Load().Windows.Length == 0, "Primeiro início deve ter sessão vazia.");
            var state = new SessionTabState("https://example.com/report", "Relatório", true, "Trabalho", Color.Blue.ToArgb(), true);
            var session = new BrowserSessionDocument([new([state, new("https://example.org", "Pesquisa")], 1)], false);
            Check(store.Save(session), "Gravação deve funcionar.");
            var loaded = new BrowserSessionStore(path).Load();
            Check(!loaded.CleanShutdown && loaded.Windows[0].SelectedIndex == 1 && loaded.Windows[0].Tabs[0] == state,
                "Abas, grupo, cor, fixação, silêncio, seleção e marcador de recuperação devem sobreviver.");
            Check(store.Save(session with { CleanShutdown = true }), "Encerramento normal deve ser gravado.");
            Check(new BrowserSessionStore(path).Load().CleanShutdown, "Encerramento normal não pode parecer travamento.");

            var allowlist = new SiteAllowlist(new SiteExceptionStore(Path.Combine(directory, "sites.json")));
            using var view = new BrowserTabControl();
            using var normal = new BrowserTab(allowlist) { IsPinned = true, GroupName = "Trabalho" };
            normal.NavigateOrQueue(state.Url);
            using var privateTab = new BrowserTab(allowlist, true);
            privateTab.NavigateOrQueue("https://example.com/private-secret");
            Check(SessionTabState.Capture(normal)?.Url == state.Url, "URL pendente deve ser recuperável antes de inicializar o motor.");
            Check(SessionTabState.Capture(privateTab) is null, "Abas anônimas não podem ser salvas nem reabertas.");
            view.TabPages.AddRange([normal, privateTab]);
            var manager = new BrowserTabManager(view, null!, allowlist);
            var closed = new List<SessionTabState>();
            manager.TabClosed += closed.Add;
            manager.Close(privateTab);
            manager.Close(normal);
            Check(closed.Count == 1 && closed[0].IsPinned && closed[0].GroupName == "Trabalho", "Fechamento só deve lembrar a aba normal e seus metadados.");

            using var galaxy = new BrowserTab(allowlist) { Text = "Cotton Galaxy" };
            using var privateGalaxy = new BrowserTab(allowlist, true);
            galaxy.NavigateOrQueue(TrustedBrowserBridge.GalaxyUrl);
            privateGalaxy.NavigateOrQueue(TrustedBrowserBridge.GalaxyUrl);
            var galaxyState = SessionTabState.Capture(galaxy);
            Check(galaxyState?.Url == TrustedBrowserBridge.GalaxyUrl,
                "O jogo em uma aba normal deve ser recuperável antes de inicializar o motor.");
            Check(SessionTabState.Capture(privateGalaxy) is null,
                "O jogo em uma aba anônima não pode ser salvo nem reaberto.");
            view.TabPages.AddRange([galaxy, privateGalaxy]);
            manager.Close(privateGalaxy);
            manager.Close(galaxy);
            Check(closed.Count == 2 && closed[1] == galaxyState,
                "Fechar o jogo deve lembrar somente a aba normal para reabertura.");
            Check(store.Save(new([new([galaxyState!], 0)], true)), "Salvar a aba normal do jogo.");
            loaded = new BrowserSessionStore(path).Load();
            Check(loaded.Windows.Length == 1 && loaded.Windows[0].Tabs.Length == 1
                && loaded.Windows[0].Tabs[0] == galaxyState,
                "A aba normal do jogo deve sobreviver à gravação e restauração da sessão.");

            File.WriteAllText(path, JsonSerializer.Serialize(new BrowserSessionDocument([
                new([new("javascript:alert(1)", "Inválida"), new("https://user:password@example.com", "Credenciais"),
                     new(TrustedBrowserBridge.PrivateTabUrl, "Anônima interna"),
                     new(TrustedBrowserBridge.UiUrl, "Central interna"),
                     new("https://app.cottonbrowser.test/other.html", "Página interna desconhecida"), state], 99)], false)));
            loaded = new BrowserSessionStore(path).Load();
            Check(loaded.Windows[0].Tabs.Length == 1 && loaded.Windows[0].SelectedIndex == 0,
                "Restaurar deve filtrar URLs inseguras e limitar a seleção.");
            File.WriteAllText(path, JsonSerializer.Serialize(new BrowserSessionDocument([
                new([new("javascript:alert(1)", "Inválida"), state, new("https://example.org", "Vizinha")], 1)], true)));
            loaded = new BrowserSessionStore(path).Load();
            Check(loaded.Windows[0].Tabs[loaded.Windows[0].SelectedIndex] == state,
                "Filtrar uma aba anterior deve preservar a seleção da aba válida.");
            File.WriteAllText(path, JsonSerializer.Serialize(new BrowserSessionDocument([
                new([state, new("javascript:alert(1)", "Inválida"), new("https://example.org", "Vizinha")], 1)], true)));
            loaded = new BrowserSessionStore(path).Load();
            Check(loaded.Windows[0].SelectedIndex == 1,
                "Uma seleção inválida deve escolher a próxima aba válida quando disponível.");
            File.WriteAllText(path, "{ arquivo incompleto");
            var damaged = new BrowserSessionStore(path);
            damaged.Load();
            Check(!damaged.Save(session) && File.ReadAllText(path) == "{ arquivo incompleto", "Arquivo danificado deve ser preservado.");
            Console.WriteLine("PASS: sessão atômica, recuperação, metadados, seleção, isolamento anônimo e reabertura segura.");
        }
        finally
        {
            // This path was generated inside the temporary directory by this test.
            var resolved = Path.GetFullPath(directory);
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
            if (resolved.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)) Directory.Delete(resolved, true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
