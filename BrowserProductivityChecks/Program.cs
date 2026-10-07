using System.Reflection;
using System.Text;
using System.Text.Json;
using LeanBrowser;
using CottonBrowser.Shared;
using Microsoft.Web.WebView2.Core;

internal static class Program
{
    private static readonly string Work = Path.GetFullPath(Path.Combine(".verification", "productivity-" + Guid.NewGuid().ToString("N")));
    private static readonly Assembly Browser = typeof(BrowserForm).Assembly;
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize(); Directory.CreateDirectory(Work);
        try
        {
            DataChecks(); UiChecks();
            if (args.Contains("--webview"))
            {
                using var form = new Form { Width = 1000, Height = 700, ShowInTaskbar = false, Opacity = 0 };
                form.Shown += async (_, _) =>
                {
                    try { await WebChecks(form); }
                    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
                    finally { form.Close(); }
                };
                Application.Run(form);
            }
            Console.WriteLine("Artefatos: " + Work);
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    }

    private static void DataChecks()
    {
        var directory = Path.Combine(Work, "data"); Directory.CreateDirectory(directory);
        var prefs = BrowserPreferences.Load(Path.Combine(directory, "preferences.json"));
        prefs.RememberZoom("https://alpha.example/page", 1.25);
        Check(BrowserPreferences.Load(Path.Combine(directory, "preferences.json")).ZoomFor("https://alpha.example/other") == 1.25, "Zoom persists across restart and paths.");
        Check(prefs.ZoomFor("https://beta.example/") == 1, "Different sites keep separate zoom.");
        prefs.DownloadFolder = Path.Combine(Work, "downloads"); prefs.AskDownloadLocation = true;
        prefs.Shortcuts["history"] = (int)(Keys.Control | Keys.Alt | Keys.H); Check(prefs.Save(), "Preferences save.");
        var loaded = BrowserPreferences.Load(Path.Combine(directory, "preferences.json"));
        Check(loaded.DownloadFolder == prefs.DownloadFolder && loaded.AskDownloadLocation && ShortcutCatalog.Get("history", loaded) == (Keys.Control | Keys.Alt | Keys.H), "Download choices and shortcuts persist.");
        Check(!ShortcutCatalog.IsAvailable(Keys.Control | Keys.T) && !ShortcutCatalog.IsAvailable(Keys.A), "Reserved and plain typing shortcuts rejected.");
        var normalized = ShortcutCatalog.Normalize(new() { ["history"] = (int)(Keys.Control | Keys.J), ["downloads"] = (int)(Keys.Control | Keys.T) });
        Check(normalized.Count == 0, "Duplicate and reserved shortcut assignments return to defaults.");
        prefs.RememberZoom("https://alpha.example", 1); Check(!prefs.SiteZoom.ContainsKey("alpha.example"), "Reset zoom forgets site override.");
        prefs.RememberZoom("https://alpha.example", 1.5);
        var store = new ProductivityStore(Path.Combine(directory, "productivity.json"));
        var tabs = new[] { new SessionTabState("https://alpha.example/a", "A", true, "Estudos", Color.Blue.ToArgb(), true), new SessionTabState("https://beta.example/", "B") };
        store.SaveWorkspace("Estudos", tabs, 1); var workspace = store.Load().Workspaces.Single();
        Check(workspace.Tabs[0].IsPinned && workspace.Tabs[0].IsMuted && workspace.SelectedIndex == 1, "Workspace preserves tab organization and selection.");
        store.RenameWorkspace(workspace.Id, "Trabalho"); store.SaveWorkspace("Trabalho", tabs[..1], 0, workspace.Id);
        Check(store.Load().Workspaces is [{ Name: "Trabalho", Tabs.Length: 1 }], "Updating and renaming workspace do not duplicate it.");
        store.SaveReading("https://alpha.example/article", "Artigo"); store.SaveReading("https://alpha.example/article", "Duplicado");
        var reading = store.Load().ReadingList.Single(); store.UpdateReading(reading with { Notes = "Minha anotação", Read = true });
        Check(store.Load().ReadingList is [{ Notes: "Minha anotação", Read: true }], "Reading list deduplicates URLs and retains notes/state.");
        var before = File.ReadAllText(Path.Combine(directory, "productivity.json"));
        Reject(() => store.SaveWorkspace("Inválido", [new("javascript:alert(1)", "X")], 0));
        Check(File.ReadAllText(Path.Combine(directory, "productivity.json")) == before, "Invalid workspace leaves saved data unchanged.");
        var bookmarks = new BookmarkStore(Path.Combine(directory, "bookmarks.json")); bookmarks.Add("https://alpha.example", "Favorito"); bookmarks.CreateFolder("Vazia");
        File.WriteAllText(Path.Combine(directory, "theme.txt"), "dark"); File.WriteAllText(Path.Combine(directory, "accent.txt"), "#8839EF");
        File.WriteAllText(Path.Combine(directory, "passwords.json"), "DO NOT EXPORT"); File.WriteAllText(Path.Combine(directory, "history.json"), "DO NOT EXPORT");
        var backup = Path.Combine(Work, "fixture.cottonbackup"); BrowserBackup.Export(backup, directory, directory);
        Check(!File.ReadAllText(backup).Contains("DO NOT EXPORT"), "Backup excludes passwords and history.");
        var target = Path.Combine(Work, "restored"); BrowserBackup.Restore(backup, target, target);
        Check(new BookmarkStore(Path.Combine(target, "bookmarks.json")).Folders().Contains("Vazia")
            && BrowserPreferences.Load(Path.Combine(target, "preferences.json")).ZoomFor("https://alpha.example/") == 1.5
            && new ProductivityStore(Path.Combine(target, "productivity.json")).Load().ReadingList.Single().Read, "Complete portable restore.");
        var bad = Path.Combine(Work, "bad.cottonbackup");
        File.WriteAllText(bad, JsonSerializer.Serialize(new { Format = 1, CreatedAt = DateTimeOffset.UtcNow, Files = new Dictionary<string, string> { ["../outside.txt"] = "bad" } }));
        Reject(() => BrowserBackup.Restore(bad, target, target)); Check(!File.Exists(Path.Combine(Work, "outside.txt")), "Path traversal rejected.");
        File.WriteAllText(bad, JsonSerializer.Serialize(new { Format = 1, CreatedAt = DateTimeOffset.UtcNow, Files = new Dictionary<string, string> { ["theme.txt"] = "light", ["productivity.json"] = "{}" } }));
        Reject(() => BrowserBackup.Restore(bad, target, target)); Check(File.ReadAllText(Path.Combine(target, "theme.txt")) == "dark", "Every backup entry validates before any data changes.");
        var rollback = Path.Combine(Work, "rollback"); Directory.CreateDirectory(Path.Combine(rollback, "bookmarks.json"));
        File.WriteAllText(Path.Combine(rollback, "preferences.json"), "{\"RestoreSession\":false}");
        var previousPreferences = File.ReadAllText(Path.Combine(rollback, "preferences.json"));
        Reject(() => BrowserBackup.Restore(backup, rollback, rollback));
        Check(File.ReadAllText(Path.Combine(rollback, "preferences.json")) == previousPreferences, "An I/O failure rolls back entries already replaced.");
        using var policy = new PermissionPolicy(); policy.SetBlocked("https://alpha.example", "Camera", false, true);
        Check(policy.IsBlocked("https://alpha.example", "Camera", false) && !policy.IsBlocked("https://alpha.example", "Camera", true), "Permission rules isolate private browsing.");
        Check(!(bool)Invoke(policy, "RecordDecision", "regular|https://alpha.example|Camera", true)!, "A pending allow cannot defeat a revocation.");
        policy.SetBlocked("https://alpha.example", "Camera", false, false); Check(!policy.IsBlocked("https://alpha.example", "Camera", false), "Permissions can return to asking.");
        Console.WriteLine("PASS: site zoom, downloads, shortcuts, workspaces, reading notes, validated backup/restore and permission revocation.");
    }

    private static void UiChecks()
    {
        using var browser = new BrowserForm();
        Invoke(browser, "OpenSettings"); var settings = Get<SettingsTab>(browser, "_settingsTab");
        foreach (var section in new[] { "Espaços de trabalho", "Lista de leitura", "Downloads", "Atalhos", "Backup e restauração" })
            Check(((System.Collections.IDictionary)Field(settings, "_navigation")).Contains(section), "Settings navigation includes " + section);
        foreach (var dark in new[] { true, false })
        {
            Theme.SetDark(dark); settings.ApplyTheme();
            using var form = new Form { ClientSize = new Size(1200, 780), Opacity = 0, ShowInTaskbar = false };
            var host = new TabControl { Dock = DockStyle.Fill }; Get<BrowserTabControl>(browser, "_tabView").TabPages.Remove(settings);
            host.TabPages.Add(settings); form.Controls.Add(host); form.Show();
            foreach (var width in new[] { 1200, 600 })
            {
                form.ClientSize = new Size(width, 780);
                foreach (var section in new[] { "Espaços de trabalho", "Lista de leitura", "Downloads", "Atalhos", "Backup e restauração", "Privacidade e proteção", "Desempenho" })
                {
                    Invoke(settings, "SelectSection", section); Application.DoEvents();
                    var body = Get<Panel>(settings, "_body");
                    foreach (var card in body.Controls.OfType<Panel>().Where(c => c.Visible)) Check(card.Right <= body.ClientSize.Width && card.Width > 0, "Card fits settings at " + width);
                    using var bitmap = new Bitmap(settings.Width, settings.Height); settings.DrawToBitmap(bitmap, settings.ClientRectangle);
                    bitmap.Save(Path.Combine(Work, section + "-" + width + "-" + dark + ".png"));
                }
            }
            host.TabPages.Remove(settings); form.Close();
        }
        Get<BrowserTabControl>(browser, "_tabView").TabPages.Add(settings);
        var menu = Get<BrowserOverflowMenu>(browser, "_overflowMenu");
        foreach (var name in new[] { "Vídeo flutuante", "Espaços de trabalho", "Lista de leitura", "Painel de desempenho", "Permissões e dados do site" })
            Check(menu.Items.Cast<ToolStripItem>().Any(i => i.Text == name), "Menu contains " + name);
        Check(typeof(Omnibox).GetEvent("SiteControlsRequested") is not null, "Site controls accessible from address bar.");
        using (var capture = (TextBox)Activator.CreateInstance(Browser.GetType("LeanBrowser.ShortcutCaptureBox")!, true)!)
        {
            Keys captured = Keys.None;
            capture.GetType().GetProperty("Captured")!.SetValue(capture, (Action<Keys>)(key => captured = key));
            object[] args = [new Message(), Keys.Control | Keys.H];
            Check((bool)Invoke(capture, "ProcessCmdKey", args)! && captured == (Keys.Control | Keys.H), "Shortcut field captures browser commands before the parent can activate them.");
        }
        var readingStore = new ProductivityStore(Path.Combine(Work, "ui-reading.json")); readingStore.SaveReading("https://notes.example/", "Notes");
        using (var view = (Control)Activator.CreateInstance(Browser.GetType("LeanBrowser.ProductivityView")!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [readingStore, true, (Action)(() => {}), (Action<WorkspaceEntry>)(_ => {}), (Action<string>)(_ => {})], null)!)
        {
            var list = (ListView)Field(view, "_list"); _ = view.Handle; _ = list.Handle; list.Items[0].Selected = true;
            ((TextBox)Field(view, "_notes")).Text = "Draft note survives marking read";
            Descendants(view).OfType<Button>().Single(b => b.Text == "Marcar lida / não lida").PerformClick();
            Check(readingStore.Load().ReadingList is [{ Read: true, Notes: "Draft note survives marking read" }], "Marking read saves an edited note without overwriting it.");
        }
        Console.WriteLine("PASS: integrated settings, light/dark layouts at 600/1200 pixels and menu navigation.");
    }

    private static async Task WebChecks(Form form)
    {
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Work, "web-profile"), new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required --disable-backgrounding-occluded-windows --disable-renderer-backgrounding --disable-background-timer-throttling"));
        var allowlist = new SiteAllowlist(new SiteExceptionStore(Path.Combine(Work, "sites.json")));
        using var tab = new BrowserTab(allowlist); using var other = new TabPage("Other");
        using var tabs = new BrowserTabControl { Dock = DockStyle.Fill }; form.Controls.Add(tabs); tabs.TabPages.AddRange([tab, other]); tabs.SelectedTab = tab;
        await tab.Web.EnsureCoreWebView2Async(env); var core = tab.Web.CoreWebView2;
        var prefs = BrowserPreferences.Load(Path.Combine(Work, "web-preferences.json")); prefs.RememberZoom("https://alpha.example", 1.5);
        tab.Web.SiteZoomLookup = prefs.ZoomFor; tab.Web.SiteZoomChanged = prefs.RememberZoom;
        core.AddWebResourceRequestedFilter("https://*.example/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) =>
        {
            var html = "<!doctype html><title>Fixture</title><input id='draft'><video style='width:640px;height:360px' loop muted></video><canvas width='640' height='360'></canvas>";
            args.Response = env.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(html)), 200, "OK", "Content-Type: text/html");
        };
        await Navigate(core, "https://alpha.example/one"); Check(Math.Abs(tab.Web.ZoomFactor - 1.5) < .001, "Saved host zoom applied on navigation.");
        tab.Web.SetPageZoom(1.25); await Reload(core); Check(Math.Abs(tab.Web.ZoomFactor - 1.25) < .001, "Reload preserves and persists chosen zoom.");
        await Navigate(core, "https://beta.example/two"); Check(Math.Abs(tab.Web.ZoomFactor - 1) < .001, "Different host resets to its own zoom.");
        await Navigate(core, "https://alpha.example/back"); Check(Math.Abs(tab.Web.ZoomFactor - 1.25) < .001, "Returning restores host zoom.");
        var manager = core.CookieManager;
        manager.AddOrUpdateCookie(manager.CreateCookie("old", "1", "alpha.example", "/"));
        manager.AddOrUpdateCookie(manager.CreateCookie("other", "2", "beta.example", "/"));
        await Wait(async () => (await manager.GetCookiesAsync(null)).Count >= 2);
        foreach (var cookie in (await manager.GetCookiesAsync("https://alpha.example")).ToArray()) manager.DeleteCookie(cookie);
        await Wait(async () => (await manager.GetCookiesAsync("https://alpha.example/")).Count == 0);
        Check((await manager.GetCookiesAsync("https://alpha.example")).Count == 0 && (await manager.GetCookiesAsync("https://beta.example")).Count == 1, "Site cookie deletion keeps unrelated sites.");
        await core.ExecuteScriptAsync("localStorage.setItem('fixture','data');");
        await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.Cookies | CoreWebView2BrowsingDataKinds.AllDomStorage);
        Check((await manager.GetCookiesAsync(null)).Count == 0 && await core.ExecuteScriptAsync("localStorage.getItem('fixture')") == "null", "Clear cookies and site storage.");
        await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);
        await core.ExecuteScriptAsync("window.token='preserved';const c=document.querySelector('canvas');const ctx=c.getContext('2d');ctx.fillStyle='red';ctx.fillRect(0,0,640,360);const stream=c.captureStream(0);const track=stream.getVideoTracks()[0];const chunks=[];const recorder=new MediaRecorder(stream,{mimeType:'video/webm'});recorder.ondataavailable=e=>chunks.push(e.data);recorder.onstop=()=>{stream.getTracks().forEach(t=>t.stop());const v=document.querySelector('video');v.src=URL.createObjectURL(new Blob(chunks,{type:'video/webm'}));v.play();};recorder.start();track.requestFrame();setTimeout(()=>{ctx.fillStyle='blue';ctx.fillRect(0,0,640,360);track.requestFrame();},100);setTimeout(()=>recorder.stop(),900);");
        await Wait(async () => await core.ExecuteScriptAsync("document.querySelector('video').readyState >= 2 && !document.querySelector('video').paused") == "true");
        var floatingType = Browser.GetType("LeanBrowser.FloatingVideoWindow")!;
        var enter = (string)floatingType.GetField("EnterScript", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        var exit = (string)floatingType.GetField("ExitScript", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        Check(await core.ExecuteScriptAsync(enter) == "true", "Floating video selects real loaded media.");
        using (var floating = (Form)Activator.CreateInstance(floatingType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [tab], null)!)
        {
            floating.Show(); tabs.SelectedTab = other; await Task.Delay(400);
            Check(tab.Web.Parent == floating && tab.IsFloatingVideo && !tab.Web.IsDisposed, "Float hosts same WebView and remains available on other tabs.");
            Check(await core.ExecuteScriptAsync("window.token==='preserved' && !document.querySelector('video').paused") == "true", "Floating preserves document and playback.");
            Check(await core.ExecuteScriptAsync("Math.abs(document.querySelector('video').getBoundingClientRect().width-innerWidth)<2") == "true", "Video fills floating viewport.");
            floating.Close(); await Task.Delay(100);
        }
        Check(tab.Web.Parent == tab && !tab.IsFloatingVideo && !tab.Web.IsDisposed, "Closing float returns same renderer to tab.");
        Check(await core.ExecuteScriptAsync("!window.__cottonFloatingVideo && !document.querySelector('video').controls && window.token==='preserved'") == "true", "Floating exit restores page and original controls.");
        tabs.SelectedTab = tab; await core.ExecuteScriptAsync(exit);
        var preferences = BrowserPreferences.Load(Path.Combine(Work, "memory.json")); preferences.MemorySaverEnabled = false;
        using var saver = new TabMemorySaver(tabs, () => false, preferences); await saver.RegisterTabAsync(tab);
        await core.ExecuteScriptAsync("document.querySelector('video').pause();document.getElementById('draft').value='unsaved';");
        tabs.SelectedTab = other; await Task.Delay(100);
        Check(!await saver.SuspendNowAsync(tab), "Manual suspend preserves dirty form.");
        await core.ExecuteScriptAsync("document.getElementById('draft').value='';document.querySelector('video').remove();document.querySelector('canvas').remove();");
        Check(await saver.SuspendNowAsync(tab) && core.IsSuspended, "Manual safe suspension works independently of automatic timer.");
        saver.Resume(tab); Check(await core.ExecuteScriptAsync("window.token") == "\"preserved\"", "Resume retains document.");
        Console.WriteLine("PASS: real WebView2 host zoom/reload, selective cookies, cache/storage clearing, floating playback and safe manual suspension.");
    }
    private static async Task Navigate(CoreWebView2 core, string url) { await Completion(core, () => core.Navigate(url)); }
    private static async Task Reload(CoreWebView2 core) { await Completion(core, core.Reload); }
    private static async Task Completion(CoreWebView2 core, Action action)
    {
        var done = new TaskCompletionSource<bool>(); EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
        handler = (_, args) => { core.NavigationCompleted -= handler; done.TrySetResult(args.IsSuccess); };
        core.NavigationCompleted += handler; action(); Check(await done.Task.WaitAsync(TimeSpan.FromSeconds(20)), "Fixture navigation succeeds.");
    }
    private static async Task Wait(Func<Task<bool>> predicate)
    {
        var until = Environment.TickCount64 + 15000; while (Environment.TickCount64 < until) { if (await predicate()) return; await Task.Delay(100); }
        throw new Exception("Timeout waiting for native media.");
    }
    private static object? Invoke(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, args);
    private static object Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static IEnumerable<Control> Descendants(Control root) { foreach (Control child in root.Controls) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
    private static T Get<T>(object owner, string name, bool required = true) => Field(owner, name) is T value ? value : required ? throw new Exception("Field " + name) : default!;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or JsonException) { return; } throw new Exception("Invalid input accepted."); }
}
