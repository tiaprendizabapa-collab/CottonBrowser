using System.Text.Json;
using CottonBrowser.Shared;
using LeanBrowser;
using Microsoft.Web.WebView2.Core;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form
        {
            Text = "CottonBrowser isolated memory checks", Width = 800, Height = 600,
            ShowInTaskbar = false, Opacity = 0
        };
        using var tabs = new BrowserTabControl { Dock = DockStyle.Fill };
        form.Controls.Add(tabs);
        form.Shown += async (_, _) =>
        {
            try { await RunAsync(tabs); }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { form.Close(); }
        };
        Application.Run(form);
    }

    private static async Task RunAsync(BrowserTabControl tabs)
    {
        var directory = Path.Combine(Environment.CurrentDirectory, ".verification", "memory-webview",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var preferences = BrowserPreferences.Load(Path.Combine(directory, "preferences.json"));
        preferences.MemorySaverEnabled = true;
        preferences.SuspendAfterMinutes = 1;
        var allowlist = new SiteAllowlist(new SiteExceptionStore(Path.Combine(directory, "sites.json")));
        using var browser = new BrowserTab(allowlist);
        using var other = new TabPage("Other tab");
        tabs.TabPages.AddRange([browser, other]);
        tabs.SelectedTab = browser;
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(directory, "profile"),
            new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"))
            .WaitAsync(TimeSpan.FromSeconds(20));
        await browser.Web.EnsureCoreWebView2Async(environment).WaitAsync(TimeSpan.FromSeconds(20));
        var core = browser.Web.CoreWebView2;
        core.Settings.IsWebMessageEnabled = false;
        core.NavigationStarting += (_, _) => browser.Loading = true;
        core.NavigationCompleted += (_, _) => browser.Loading = false;
        var downloads = false;
        using var saver = new TabMemorySaver(tabs, () => downloads, preferences);
        await saver.RegisterTabAsync(browser);
        saver.Start();
        await NavigateAsync(core, "<title>Isolated fixture</title><p id='marker'>Document stays loaded</p>");
        await core.ExecuteScriptAsync("window.fixtureToken = 42;");
        MakeIdle();
        await Task.Delay(100);
        await saver.CheckNowAsync();
        Check(core.IsSuspended && browser.IsSuspended, "A safe inactive page must be suspended.");
        tabs.SelectedTab = browser;
        Check(!core.IsSuspended && !browser.IsSuspended, "Selecting the page must resume it immediately.");
        Check(await core.ExecuteScriptAsync("window.fixtureToken") == "42",
            "Resuming must preserve the document rather than reload it.");

        await NavigateAsync(core, "<title>Draft fixture</title><input id='draft'>");
        await core.ExecuteScriptAsync("document.getElementById('draft').value = 'unsaved draft';");
        MakeIdle();
        await saver.CheckNowAsync();
        Check(!core.IsSuspended, "A modified form must stay awake.");
        tabs.SelectedTab = browser;
        Check(await core.ExecuteScriptAsync("document.getElementById('draft').value") == "\"unsaved draft\"",
            "Draft checks must preserve the field content.");

        await NavigateAsync(core, "<title>Embedded draft</title><iframe srcdoc=\"<input id='draft' value='original'>\"></iframe>");
        await WaitForAsync(async () => await core.ExecuteScriptAsync(
            "document.querySelector('iframe').contentDocument.readyState") == "\"complete\"");
        await core.ExecuteScriptAsync("document.querySelector('iframe').contentDocument.getElementById('draft').value = 'changed';");
        MakeIdle();
        await saver.CheckNowAsync();
        Check(!core.IsSuspended, "A draft inside an iframe must keep the tab awake.");

        tabs.SelectedTab = browser;
        await NavigateAsync(core, "<title>Audio fixture</title><audio id='sound' loop></audio>");
        var wave = JsonSerializer.Serialize("data:audio/wav;base64," + MakeWave());
        await core.ExecuteScriptAsync($"document.getElementById('sound').src = {wave}; document.getElementById('sound').play();");
        await WaitForAsync(async () => await core.ExecuteScriptAsync("document.getElementById('sound').paused") == "false");
        core.IsMuted = true;
        MakeIdle();
        await saver.CheckNowAsync();
        Check(!core.IsSuspended, "Playing media must stay awake even when the tab is muted.");
        tabs.SelectedTab = browser;
        await core.ExecuteScriptAsync("document.getElementById('sound').pause();");
        core.IsMuted = false;

        await NavigateAsync(core, "<title>Safe fixture</title><p>Idle page</p>");
        downloads = true;
        MakeIdle();
        await saver.CheckNowAsync();
        Check(!core.IsSuspended, "Ongoing downloads must keep the page awake.");
        downloads = false;
        await saver.CheckNowAsync();
        Check(core.IsSuspended, "An idle page can sleep after the protected activity ends.");
        preferences.MemorySaverEnabled = false;
        Check(preferences.Save(), "Isolated preferences should be writable.");
        Check(!core.IsSuspended && !browser.IsSuspended, "Disabling memory saver must wake sleeping pages.");
        browser.LastActivatedAt = DateTimeOffset.UtcNow.AddHours(-1);
        preferences.MemorySaverEnabled = true;
        preferences.Save();
        await saver.CheckNowAsync();
        Check(!core.IsSuspended && DateTimeOffset.UtcNow - browser.LastActivatedAt < TimeSpan.FromSeconds(5),
            "Re-enabling must start a fresh idle interval.");

        tabs.SelectedTab = browser;
        await core.ExecuteScriptAsync("document.querySelectorAll = (() => { const original = document.querySelectorAll.bind(document); return selector => { const start = performance.now(); while (performance.now() - start < 200) {} return original(selector); }; })();");
        MakeIdle();
        var check = saver.CheckNowAsync();
        await Task.Delay(50);
        tabs.SelectedTab = browser;
        await check;
        Check(!core.IsSuspended && !browser.IsSuspended,
            "Selecting a tab while an asynchronous safety probe runs must prevent suspension.");
        Check(!core.Settings.IsWebMessageEnabled, "Memory saving must leave web messaging disabled.");
        Console.WriteLine("PASS: real WebView2 suspension/resume, document preservation, drafts in frames, muted media, downloads, disable/re-enable and selection race.");

        void MakeIdle()
        {
            tabs.SelectedTab = other;
            browser.LastActivatedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        }
    }

    private static async Task NavigateAsync(CoreWebView2 core, string html)
    {
        var completed = new TaskCompletionSource<bool>();
        EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
        handler = (_, args) => { core.NavigationCompleted -= handler; completed.TrySetResult(args.IsSuccess); };
        core.NavigationCompleted += handler;
        core.NavigateToString("<!doctype html><html><body>" + html + "</body></html>");
        Check(await completed.Task.WaitAsync(TimeSpan.FromSeconds(15)), "The isolated fixture must load.");
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("The isolated WebView2 fixture did not become ready.");
    }

    private static string MakeWave()
    {
        const int sampleRate = 8000;
        const int sampleCount = 8000;
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        writer.Write("RIFF"u8); writer.Write(36 + sampleCount * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate);
        writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(sampleCount * 2);
        for (var index = 0; index < sampleCount; index++)
            writer.Write((short)(Math.Sin(index * 440 * Math.PI * 2 / sampleRate) * 1000));
        return Convert.ToBase64String(memory.ToArray());
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
