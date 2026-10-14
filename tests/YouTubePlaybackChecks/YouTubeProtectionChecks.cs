using System.IO.Compression;
using System.Text;
using CottonBrowser.Shared;
using LeanBrowser;
using Microsoft.Web.WebView2.Core;

internal static class YouTubeProtectionChecks
{
    internal static async Task RunAsync(Form form, string work)
    {
        var extensionRoot = Path.Combine(work, "extension-migration");
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(extensionRoot, "profile"), WebContentIsolation.CreateEnvironmentOptions(""));
        using (var web = new TabWebView { Dock = DockStyle.Fill })
        {
            form.Controls.Add(web);
            await web.EnsureCoreWebView2Async(env);
            var core = web.CoreWebView2;
            var otherExtensions = (await core.Profile.GetBrowserExtensionsAsync()).ToDictionary(e => e.Id, e => e.IsEnabled);
            var oldDirectory = Path.Combine(extensionRoot, "Extensions", "uBOL-2026.914.1325-4a8d6c13f772");
            ZipFile.ExtractToDirectory(Path.GetFullPath("src/LeanBrowser/Assets/uBOLite_2026.914.1325.edge.zip"), oldDirectory);
            var old = await core.Profile.AddBrowserExtensionAsync(oldDirectory);
            await File.WriteAllTextAsync(oldDirectory + ".id", old.Id);
            Check(old.IsEnabled, "A versão antiga deve começar ativa para reproduzir uma atualização real.");
            var protection = new AdProtection();
            await protection.InitializeAsync(core, extensionRoot);
            Check(protection.Available && protection.Failure is null, "O novo pacote deve instalar corretamente.");
            var installed = await core.Profile.GetBrowserExtensionsAsync();
            var currentId = new Uri(protection.DashboardUrl!).Host;
            Check(currentId != old.Id && installed.Count == otherExtensions.Count + 2 && installed.Single(e => e.Id == old.Id).IsEnabled == false
                && installed.Single(e => e.Id == currentId).IsEnabled, "Somente a versão atual do bloqueador deve permanecer ativa após a atualização.");
            var restarted = new AdProtection();
            await restarted.InitializeAsync(core, extensionRoot);
            Check((await core.Profile.GetBrowserExtensionsAsync()).Count == otherExtensions.Count + 2, "Reinicializar não deve duplicar a extensão.");
            await restarted.SetEnabledAsync(false);
            Check((await core.Profile.GetBrowserExtensionsAsync()).Where(e => e.Id == old.Id || e.Id == currentId).All(e => !e.IsEnabled), "Pausar deve desligar todas as versões do bloqueador.");
            await restarted.SetEnabledAsync(true);
            var resumed = await core.Profile.GetBrowserExtensionsAsync();
            Check(resumed.Single(e => e.Id == currentId).IsEnabled && !resumed.Single(e => e.Id == old.Id).IsEnabled, "Retomar deve ativar apenas os filtros atuais.");
            Check(otherExtensions.All(e => resumed.Single(x => x.Id == e.Key).IsEnabled == e.Value), "A migração deve preservar as outras extensões do perfil.");
        }

        // The separate environment has no extension: measure the native layer
        // alone, while the migration check above verifies uBOL stays enabled.
        var nativeEnv = await CoreWebView2Environment.CreateAsync(null, Path.Combine(work, "native-filter-profile"));
        using var native = new TabWebView { Dock = DockStyle.Fill }; form.Controls.Add(native);
        await native.EnsureCoreWebView2Async(nativeEnv);
        var nativeCore = native.CoreWebView2;
        var blocker = new AdBlocker(new SiteAllowlist(new SiteExceptionStore(Path.Combine(work, "compatibility-sites.json"))));
        blocker.Attach(nativeCore);
        var document = new DocumentProtection();
        await document.SetEnabledAsync(nativeCore, true);
        foreach (var host in new[] { "www.youtube.com", "m.youtube.com", "fixture.example.test", "youtube.com.evil.test" })
            nativeCore.AddWebResourceRequestedFilter("https://" + host + "/*", CoreWebView2WebResourceContext.Document);
        nativeCore.WebResourceRequested += (_, args) =>
        {
            if (args.Response is not null) return;
            var isProbe = new Uri(args.Request.Uri).Host == "doubleclick.net";
            args.Response = nativeEnv.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(isProbe ? "allowed" : "<!doctype html><p>Fixture</p>")),
                200, "OK", "Access-Control-Allow-Origin: *\r\nContent-Type: text/html");
        };
        foreach (var (host, isYouTube) in new[] { ("www.youtube.com", true), ("m.youtube.com", true), ("fixture.example.test", false), ("youtube.com.evil.test", false) })
        {
            var done = new TaskCompletionSource<bool>();
            void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args) => done.TrySetResult(args.IsSuccess);
            nativeCore.NavigationCompleted += Completed;
            try
            {
                nativeCore.Navigate("https://" + host + "/fixture");
                Check(await done.Task.WaitAsync(TimeSpan.FromSeconds(10)), "A página isolada deve carregar.");
            }
            finally { nativeCore.NavigationCompleted -= Completed; }
            await nativeCore.ExecuteScriptAsync("window.__probeStatus = null; fetch('https://doubleclick.net/probe').then(r=>window.__probeStatus=r.status).catch(()=>window.__probeStatus=-1)");
            var status = "null";
            for (var attempt = 0; attempt < 100 && status == "null"; attempt++)
            {
                await Task.Delay(50);
                status = await nativeCore.ExecuteScriptAsync("window.__probeStatus");
            }
            Check(status == (isYouTube ? "200" : "403"), $"O filtro básico deve delegar somente no YouTube e manter o bloqueio nos demais sites ({host}: {status}).");
            var extras = await nativeCore.ExecuteScriptAsync("!!window.__BC_DIALOGS_113__ || !!window.__BC_CLEANER_113__ || !!window.__LEAN_YOUTUBE_PROTECTION__");
            Check(extras == (isYouTube ? "false" : "true"), "Scripts de limpeza não devem sobrepor os filtros do YouTube.");
        }
        Console.WriteLine("PASS: pacote atualizado, migração sem bloqueadores duplicados, pausa/retomada e regras adicionais restritas aos outros sites.");
    }

    private static void Check(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException(failure);
    }
}
