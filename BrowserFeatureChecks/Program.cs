using System.Reflection;
using System.Text.Json;
using LeanBrowser;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using CottonBrowser.Shared;

internal static class Program
{
    private static readonly Assembly Browser = typeof(BrowserForm).Assembly;
    private static readonly string Work = Path.GetFullPath(Path.Combine(".verification", "features-" + Guid.NewGuid().ToString("N")));
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware); Application.EnableVisualStyles(); Directory.CreateDirectory(Work);
        DataChecks(); UiChecks();
        if (args.Contains("--webview"))
        {
            var task = WebChecks(); var deadline = Environment.TickCount64 + 60000;
            while (!task.IsCompleted && Environment.TickCount64 < deadline) { Application.DoEvents(); Thread.Sleep(10); }
            Check(task.IsCompleted, "Timeout nos testes WebView2."); task.GetAwaiter().GetResult();
        }
        Console.WriteLine("PASS: favoritos com pastas, importação/exportação e ordenação; perfis e preferências isolados; configurações, abas verticais, tela dividida, tradução e modo de leitura.");
    }
    private static void DataChecks()
    {
        var store = new BookmarkStore(Path.Combine(Work, "bookmarks.json"));
        File.WriteAllText(Path.Combine(Work, "bookmarks.json"), "[{\"Url\":\"https://old.example.com\",\"Title\":\"Antigo\"}]");
        Check(store.Load()[0].Folder == "", "Favoritos antigos devem ser preservados.");
        var html = "<DL><p><DT><H3>Trabalho &amp; estudos</H3><DL><DT><A HREF='https://example.com/?a=1&amp;b=2'>Relatório &lt;2026&gt;</A><DT><H3>Documentos</H3><DL><DT><A HREF=\"https://docs.example.com\">Documentos</A></DL></DL><DT><A HREF='javascript:alert(1)'>Inválido</A></DL>";
        Check(store.ImportHtml(html) == 2, "Importação deve preservar pastas e rejeitar esquemas perigosos.");
        Check(store.Load().Any(b => b.Folder == "Trabalho & estudos/Documentos"), "Subpastas importadas.");
        Check(store.Load().Any(b => b.Title == "Relatório <2026>" && b.Url.Contains("&b=2")), "Entidades HTML decodificadas.");
        store.CreateFolder("Vazia"); var roundtrip = new BookmarkStore(Path.Combine(Work, "roundtrip.json"));
        Check(roundtrip.ImportHtml(store.ExportHtml()) == 3 && roundtrip.Folders().Contains("Vazia"), "Exportar e importar deve preservar todos os favoritos e pastas vazias.");
        store.RenameFolder("Trabalho & estudos", "Equipe"); Check(store.Load().Any(b => b.Folder == "Equipe/Documentos"), "Renomear pasta deve atualizar descendentes.");
        store.Update("https://old.example.com", new("https://old.example.com", "Zeta", "Equipe"));
        var entries = store.Load().Where(b => b.Folder == "Equipe").ToArray(); store.Move(entries[1].Url, -1);
        Check(store.Load().Where(b => b.Folder == "Equipe").First().Url == entries[1].Url, "Reordenação persistida.");
        store.Sort(); Check(store.Load().Where(b => b.Folder == "Equipe").First().Title == "Relatório <2026>", "Ordenação alfabética.");
        store.CreateFolder("Projetos/2026/Planejamento");
        Check(store.Folders().Contains("Projetos") && store.Folders().Contains("Projetos/2026"), "Subpastas criadas devem expor seus pais.");
        var beforeConflict = File.ReadAllText(Path.Combine(Work, "bookmarks.json"));
        try { store.Update("https://old.example.com", new("https://docs.example.com", "Duplicado")); throw new Exception("Edição duplicada aceita."); } catch (ArgumentException) { }
        Check(File.ReadAllText(Path.Combine(Work, "bookmarks.json")) == beforeConflict, "Uma edição conflitante não pode excluir um favorito existente.");
        var profiles = new BrowserProfileStore(Path.Combine(Work, "profile-registry")); var work = profiles.Create("Trabalho"); profiles.Rename(work.Id, "Empresa");
        var legacyRegistry = Path.Combine(Work, "profile-registry", "profiles.json");
        File.WriteAllText(legacyRegistry, "{\"Profiles\":[{\"User\":\"legacy-user\",\"Role\":\"Admin\"}]}");
        Check(profiles.Load().Any(p => p.Name == "Empresa") && profiles.Load().Any(p => p.Id == "default"), "Perfis persistidos e padrão preservado.");
        profiles.Create("Estudos"); Check(File.ReadAllText(legacyRegistry).Contains("legacy-user"), "Perfis de navegação não podem substituir o antigo cadastro do modo de interface.");
        var root = Path.Combine(Work, "profile-data"); var first = BrowserPaths.ForProfile(root, "default"); var second = BrowserPaths.ForProfile(root, work.Id);
        Check(first == Path.GetFullPath(root) && first != second, "O perfil padrão mantém o local antigo, e os novos perfis ficam isolados.");
        try { BrowserPaths.ForProfile(root, "../default"); throw new Exception("Perfil aceitou caminho arbitrário."); } catch (ArgumentException) { }
        var prefs = BrowserPreferences.Load(Path.Combine(first, "preferences.json")); prefs.VerticalTabs = true; prefs.NewTabBackground = "Azul"; prefs.NewTabShowClock = false; prefs.NewTabShortcuts = [new("https://example.com", "Atalho")]; Check(prefs.Save(), "Salvar preferências.");
        var restored = BrowserPreferences.Load(Path.Combine(first, "preferences.json"));
        Check(restored.VerticalTabs && restored.NewTabBackground == "Azul" && !restored.NewTabShowClock && restored.NewTabShortcuts.Length == 1, "Preferências novas persistem.");
        Check(!BrowserPreferences.Load(Path.Combine(second, "preferences.json")).VerticalTabs, "Preferências de outro perfil não devem se misturar.");
        var method = typeof(BrowserForm).GetMethod("TryTranslationUrl", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var url in new[] { "http://localhost/", "http://10.0.0.1/", "https://portal.local/", "https://app.cottonbrowser.test/newtab.html", "https://user:secret@example.com/" })
            Check(!(bool)method.Invoke(null, [url, "pt", ""])!, "Tradução deve rejeitar endereços locais, internos e credenciais.");
        object[] values = ["https://example.com/artigo?q=a&b=c", "pt", ""];
        Check((bool)method.Invoke(null, values)! && ((string)values[2]).Contains("u=https%3A%2F%2F"), "Tradução deve codificar o endereço original.");
    }
    private static void UiChecks()
    {
        var store = new BookmarkStore(Path.Combine(Work, "bookmarks.json"));
        foreach (var dark in new[] { false, true })
        {
            Theme.SetDark(dark);
            foreach (var width in new[] { 560, 1200 })
            {
                using var settings = new SettingsTab(_ => {}, () => {}, store.Load, () => {}, store.Remove, true, _ => {});
                typeof(SettingsTab).GetMethod("ConfigureBookmarks", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, [store, (Action)(() => {})]);
                using var form = new Form { ClientSize = new Size(width, 760), Opacity = 0, ShowInTaskbar = false, Location = new Point(-3000,-3000), StartPosition = FormStartPosition.Manual };
                var host = new TabControl { Dock = DockStyle.Fill }; host.TabPages.Add(settings); form.Controls.Add(host); form.Show(); Application.DoEvents();
                foreach (var section in new[] { "Favoritos", "Nova guia" })
                {
                    typeof(SettingsTab).GetMethod("SelectSection", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(settings, [section]); Application.DoEvents();
                    using var bitmap = new Bitmap(settings.Width, settings.Height); settings.DrawToBitmap(bitmap, settings.ClientRectangle); bitmap.Save(Path.Combine(Work, $"{section}-{width}-{dark}.png"));
                    var view = section == "Favoritos" ? Get<Control>(settings, "_bookmarkManager") : Get<Control>(settings, "_newTabCustomization");
                    Check(view.Visible && view.Right <= view.Parent!.ClientSize.Width, "As novas seções devem caber nas configurações.");
                    foreach (FlowLayoutPanel tools in view.Controls.OfType<FlowLayoutPanel>()) Check(tools.Height < view.Height - 80, "Ferramentas devem deixar espaço para a lista.");
                }
            }
        }
        using var tabs = new BrowserTabControl(); var allowlist = new SiteAllowlist(new SiteExceptionStore(Path.Combine(Work, "exceptions.json")));
        var alpha = new BrowserTab(allowlist) { Text = "Alpha", GroupName = "Trabalho", IsPinned = true }; var beta = new BrowserTab(allowlist) { Text = "Beta" };
        tabs.TabPages.AddRange([alpha, beta]); tabs.SetVerticalTabs(true);
        Check(tabs.HeaderStrip.FlowDirection == FlowDirection.TopDown, "Abas verticais devem usar a lateral.");
        var expanded = tabs.HeaderStrip.Controls.Cast<Control>().Single(c => c.AccessibleName == "Alpha").Width; tabs.SetVerticalTabs(true, true);
        Check(tabs.HeaderStrip.Controls.Cast<Control>().Single(c => c.AccessibleName == "Alpha").Width < expanded, "Recolher abas deve usar ícones compactos."); tabs.SetVerticalTabs(false);
        using var browser = new BrowserForm(); var browserTabs = Get<BrowserTabControl>(browser, "_tabView");
        var contentArea = Get<Panel>(browser, "_contentArea"); var bookmarksBar = Get<Control>(browser, "_bookmarksBar");
        browser.PerformLayout(); contentArea.PerformLayout();
        Check(contentArea.Top >= bookmarksBar.Bottom, $"O conteúdo das páginas deve começar abaixo das barras do navegador: conteúdo {contentArea.Bounds}; favoritos {bookmarksBar.Bounds}.");
        var menu = Get<BrowserOverflowMenu>(browser, "_overflowMenu");
        foreach (var label in new[] { "Modo de leitura", "Traduzir página", "Capturar página", "Tela dividida", "Abas verticais", "Gerenciar perfis", "Personalizar nova guia" })
            Check(menu.Items.Cast<ToolStripItem>().Any(item => item.Text == label), "O menu deve disponibilizar " + label + ".");
        menu.ConstrainTo(new Size(700, 400), menu.DeviceDpi); menu.PerformLayout();
        Check(menu.Height <= 400 && menu.Items.Cast<ToolStripItem>().Sum(item => item.Height) > menu.Height, "O menu ampliado deve caber em janelas curtas com rolagem.");
        var left = new BrowserTab(allowlist) { Text = "Esquerda" }; var right = new BrowserTab(allowlist) { Text = "Direita" }; browserTabs.TabPages.AddRange([left,right]);
        typeof(BrowserForm).GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(browser, new BrowserTabManager(browserTabs, null!, allowlist));
        Invoke(browser, "StartSplit", left, right);
        var split = Get<SplitContainer>(browser, "_split"); Check(left.Web.Parent == split.Panel1 && right.Web.Parent == split.Panel2, "Divisão deve manter os dois WebViews existentes.");
        Invoke(browser, "EndSplit"); Check(left.Web.Parent == left && right.Web.Parent == right && !right.Web.IsDisposed, "Sair da divisão deve preservar as páginas.");
        Invoke(browser, "StartSplit", left, right); browserTabs.TabPages.Remove(left);
        Check(right.Web.Parent == right && !right.Web.IsDisposed, "Fechar uma aba dividida não pode destruir a outra página."); left.Dispose();
        var translations = Get<Dictionary<BrowserTab, string>>(browser, "_translationOriginals"); translations[right] = "https://example.com/article";
        Invoke(browser, "TrackTranslationNavigation", right, "https://example-com.translate.goog/article");
        Check(translations.ContainsKey(right), "A tradução deve manter o endereço original durante redirecionamentos do tradutor.");
        Invoke(browser, "TrackTranslationNavigation", right, "https://another.example.com/");
        Check(!translations.ContainsKey(right), "Navegar para outro site deve retirar o original da tradução anterior.");
    }
    private static async Task WebChecks()
    {
        using var form = new Form { ClientSize = new Size(900,600), Opacity = 0, ShowInTaskbar = false };
        using var web = new WebView2 { Dock = DockStyle.Fill }; form.Controls.Add(web); form.Show();
        var firstRoot = BrowserPaths.ForProfile(Path.Combine(Work,"native-profiles"), "default"); var secondRoot = BrowserPaths.ForProfile(Path.Combine(Work,"native-profiles"), Guid.NewGuid().ToString("N"));
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(firstRoot,"WebView2")); await web.EnsureCoreWebView2Async(env);
        var core = web.CoreWebView2; core.Settings.IsWebMessageEnabled = false;
        var completed = new TaskCompletionSource<bool>(); core.NavigationCompleted += (_, _) => completed.TrySetResult(true);
        core.NavigateToString("<title>Artigo de teste</title><nav>MENU A REMOVER</nav><article><h1>Artigo</h1><p>" + new string('a',200) + "</p><p>Parágrafo final</p><form><input value='draft'></form><div style='height:3000px'>Fim</div></article>"); await completed.Task;
        var script = (string)Browser.GetType("LeanBrowser.ReadingView")!.GetField("ExtractionScript", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        using var text = JsonDocument.Parse(await core.ExecuteScriptAsync(script)); var article = text.RootElement.GetProperty("text").GetString()!;
        Check(article.Contains("Parágrafo final") && !article.Contains("MENU A REMOVER"), "Leitura deve extrair o artigo sem navegação.");
        var captures = Browser.GetType("LeanBrowser.PageCapture")!;
        var visible = await (Task<byte[]>)captures.GetMethod("VisibleAsync")!.Invoke(null, [core])!;
        var full = await (Task<byte[]>)captures.GetMethod("FullAsync")!.Invoke(null, [core])!;
        using var vs = new MemoryStream(visible); using var fs = new MemoryStream(full); using var vi = Image.FromStream(vs); using var fi = Image.FromStream(fs);
        Check(fi.Height > vi.Height && fi.Height >= 3000, "Captura inteira deve incluir conteúdo fora da área visível."); File.WriteAllBytes(Path.Combine(Work,"captura-inteira.png"),full);
        var cookie = core.CookieManager.CreateCookie("profile-test","isolated","fixture.example.test","/"); core.CookieManager.AddOrUpdateCookie(cookie);
        using var other = new WebView2(); form.Controls.Add(other); var otherEnv = await CoreWebView2Environment.CreateAsync(null, Path.Combine(secondRoot,"WebView2")); await other.EnsureCoreWebView2Async(otherEnv);
        Check((await other.CoreWebView2.CookieManager.GetCookiesAsync("https://fixture.example.test/")).Count == 0 && (await core.CookieManager.GetCookiesAsync("https://fixture.example.test/")).Any(c => c.Name == "profile-test"), "Cookies e logins devem ficar isolados entre perfis.");
        core.SetVirtualHostNameToFolderMapping("fixture.example.test", Path.GetFullPath("LeanBrowser/Assets/Bridge"), CoreWebView2HostResourceAccessKind.DenyCors);
        completed = new TaskCompletionSource<bool>(); core.Navigate("https://fixture.example.test/newtab.html"); await completed.Task;
        await core.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('cottonbrowser-home-settings', {detail:{background:'Verde',showClock:false,showGreeting:false,showShortcuts:true,shortcuts:[{title:'<img src=x onerror=alert(1)>',url:'https://example.com/'},{title:'Inválido',url:'javascript:alert(1)'}]}}))");
        using var home = JsonDocument.Parse(await core.ExecuteScriptAsync("({background:document.body.dataset.homeBackground,clock:document.querySelector('.date-line').hidden,greeting:document.querySelector('#welcome-title').hidden,count:document.querySelectorAll('.quick-link').length,title:document.querySelector('.quick-label').textContent,images:document.querySelector('.quick-links').querySelectorAll('img').length})"));
        var settings = home.RootElement;
        Check(settings.GetProperty("background").GetString() == "Verde" && settings.GetProperty("clock").GetBoolean() && settings.GetProperty("greeting").GetBoolean(), "A nova guia deve aplicar fundo e visibilidade dos elementos.");
        Check(settings.GetProperty("count").GetInt32() == 1 && settings.GetProperty("title").GetString()!.StartsWith("<img") && settings.GetProperty("images").GetInt32() == 0, "Atalhos devem rejeitar JavaScript e tratar o título como texto.");
        await core.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('cottonbrowser-home-settings', {detail:{background:'Padrão',showClock:true,showGreeting:true,showShortcuts:false,shortcuts:[]}}))");
        Check(await core.ExecuteScriptAsync("!document.querySelector('.date-line').hidden && !document.querySelector('#welcome-title').hidden && document.querySelector('.quick-section').hidden") == "true", "A personalização deve permitir restaurar elementos e ocultar atalhos.");
        using var browser = new BrowserForm(); var tabs = Get<BrowserTabControl>(browser, "_tabView");
        var allowlist = new SiteAllowlist(new SiteExceptionStore(Path.Combine(Work, "split-exceptions.json")));
        var left = new BrowserTab(allowlist, isPrivate: true) { Text = "Esquerda" }; var right = new BrowserTab(allowlist, isPrivate: true) { Text = "Direita" };
        tabs.TabPages.AddRange([left, right]);
        typeof(BrowserForm).GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(browser, new BrowserTabManager(tabs, env, allowlist));
        _ = browser.Handle;
        await left.Web.EnsureCoreWebView2Async(env); await right.Web.EnsureCoreWebView2Async(env);
        File.WriteAllText(Path.Combine(Work, "article.html"), "<meta charset='utf-8'><title>Artigo da direita</title><article><h1>Teste</h1><p>" + new string('b', 200) + "</p><form><input value='rascunho preservado'></form></article>");
        right.Web.CoreWebView2.SetVirtualHostNameToFolderMapping("fixture.example.test", Work, CoreWebView2HostResourceAccessKind.DenyCors);
        var loaded = new TaskCompletionSource<bool>(); right.Web.CoreWebView2.NavigationCompleted += (_, _) => loaded.TrySetResult(true);
        right.Web.CoreWebView2.Navigate("https://fixture.example.test/article.html"); await loaded.Task;
        Invoke(browser, "StartSplit", left, right);
        typeof(BrowserForm).GetField("_splitFocusedTab", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(browser, right);
        await (Task)typeof(BrowserForm).GetMethod("ToggleReaderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(browser, null)!;
        Check(tabs.SelectedTab == right && Get<System.Collections.IDictionary>(browser, "_readers").Contains(right), "Leitura deve exibir a página que tinha foco na tela dividida.");
        Invoke(browser, "ExitReader", right);
        Check(await right.Web.CoreWebView2.ExecuteScriptAsync("document.querySelector('input').value") == "\"rascunho preservado\"", "Dividir e ler a página deve preservar o formulário original.");
        await ReloadChecks(browser, tabs, right, left, form);
        Console.WriteLine("PASS: leitura e captura completa no WebView2 real; cookies isolados entre diretórios de perfis.");
    }
    private static async Task ReloadChecks(BrowserForm browser, BrowserTabControl tabs, BrowserTab tab, BrowserTab other, Form host)
    {
        host.Controls.Add(tabs); tabs.Dock = DockStyle.Fill; tabs.BringToFront();
        tabs.SelectedTab = tab; Application.DoEvents();
        Invoke(browser, "ConfigureTab", tab);
        var core = tab.Web.CoreWebView2;
        core.IsMuted = true; tab.IsPinned = true; tab.GroupName = "Teste de recarga";
        var controller = (CoreWebView2Controller)typeof(WebView2).GetField("_coreWebView2Controller", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tab.Web)!;
        tab.Web.SetPageZoom(1.5); other.Web.SetPageZoom(0.8);
        foreach (var shortcut in new[] { Keys.F5, Keys.R })
        {
            await NavigationAsync(core, () => Invoke(browser, "HandleShortcut", shortcut, shortcut == Keys.R, false, false));
            Check(Math.Abs(controller.ZoomFactor - 1.5) < 0.001, "F5 e Ctrl+R devem preservar o zoom configurado no menu no motor real.");
        }
        // Exercise the WebView2 zoom event independently of the menu, then
        // force the same automatic reset that Chromium performs on navigation.
        tab.Web.ZoomFactor = 1.75;
        // Host assignments do not raise the user zoom notification. Deliver
        // that notification explicitly instead of sending physical input to
        // the user's desktop; the zoom and reload still use the real engine.
        ((EventHandler<EventArgs>?)typeof(WebView2).GetField("ZoomFactorChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tab.Web))?.Invoke(tab.Web, EventArgs.Empty);
        void ResetNativeZoom(object? sender, CoreWebView2NavigationStartingEventArgs args) => tab.Web.ZoomFactor = 1;
        core.NavigationStarting += ResetNativeZoom;
        try { await NavigationAsync(core, core.Reload); }
        finally { core.NavigationStarting -= ResetNativeZoom; }
        Check(Math.Abs(controller.ZoomFactor - 1.75) < 0.001, "Recarregar deve restaurar o zoom recebido pelo evento nativo, mesmo após um reset automático do motor.");
        Check(Math.Abs(other.Web.ZoomFactor - 0.8) < 0.001 && core.IsMuted && tab.IsPinned && tab.GroupName == "Teste de recarga",
            "Recarregar deve preservar áudio, organização e zoom independente das outras abas.");
        await (Task)typeof(BrowserForm).GetMethod("ToggleReaderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(browser, null)!;
        var readers = Get<System.Collections.IDictionary>(browser, "_readers"); var reader = (Control)readers[tab]!;
        var tools = reader.Controls.OfType<FlowLayoutPanel>().Single();
        foreach (var label in new[] { "A+", "Espaçamento", "Fundo claro / escuro" }) tools.Controls.OfType<Button>().Single(b => b.Text == label).PerformClick();
        var text = reader.Controls.OfType<RichTextBox>().Single(); var fontSize = text.Font.Size; var color = text.BackColor;
        await core.CallDevToolsProtocolMethodAsync("Network.setCacheDisabled", "{\"cacheDisabled\":true}");
        File.WriteAllText(Path.Combine(Work, "article.html"), "<meta charset='utf-8'><title>Artigo atualizado</title><article><h1>Conteúdo novo após F5</h1><p>" + new string('c',200) + "</p></article>");
        await NavigationAsync(core, () => Invoke(browser, "HandleShortcut", Keys.F5, false, false, false));
        await WaitAsync(() => text.Text.Contains("Conteúdo novo após F5"), "O modo de leitura deve mostrar o conteúdo recarregado.");
        Check(ReferenceEquals(readers[tab], reader) && text.Font.Size == fontSize && text.BackColor == color && text.Text.Contains("\n\n\n\n"),
            "Recarregar deve manter o modo de leitura, a fonte, o fundo e o espaçamento escolhidos.");
        Invoke(browser, "ExitReader", tab);
        Invoke(browser, "ChangeZoom", 0); await NavigationAsync(core, core.Reload);
        Check(Math.Abs(tab.Web.ZoomFactor - 1) < 0.001, "Redefinir zoom para 100% deve continuar valendo após recarregar.");
        await (Task)typeof(BrowserForm).GetMethod("ToggleReaderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(browser, null)!;
        await NavigationAsync(core, () => core.Navigate("https://fixture.example.test/article.html?next=1"));
        Check(!readers.Contains(tab), "Navegar para outro endereço deve encerrar a leitura da página anterior.");
        Check(Math.Abs(tab.Web.ZoomFactor - 1) < 0.001, "Uma nova navegação deve respeitar o zoom atual.");
        Invoke(browser, "StartSplit", tab, other);
        await NavigationAsync(core, core.Reload);
        Check(tab.Web.Parent != tab && other.Web.Parent != other, "Recarregar deve preservar a tela dividida.");
        Console.WriteLine("PASS: F5, Ctrl+R e recarga nativa preservam zoom, inclusive após reset automático; áudio, grupos, tela dividida e ajustes de leitura preservados.");
    }
    private static async Task NavigationAsync(CoreWebView2 core, Action start)
    {
        var completed = new TaskCompletionSource<bool>();
        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args) => completed.TrySetResult(args.IsSuccess);
        core.NavigationCompleted += OnCompleted;
        try { start(); Check(await completed.Task.WaitAsync(TimeSpan.FromSeconds(10)), "A página de teste deve recarregar com sucesso."); }
        finally { core.NavigationCompleted -= OnCompleted; }
    }
    private static async Task WaitAsync(Func<bool> condition, string failure)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!condition() && Environment.TickCount64 < deadline) await Task.Delay(20);
        Check(condition(), failure);
    }
    private static T Get<T>(object instance, string field) => (T)instance.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static void Invoke(object instance, string method, params object[] args) => instance.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance,args);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
