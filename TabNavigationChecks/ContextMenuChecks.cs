using System.Reflection;
using System.Text.Json;
using CottonBrowser.Shared;
using LeanBrowser;
using Microsoft.Web.WebView2.Core;

internal static class ContextMenuChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    internal static async Task RunAsync()
    {
        var root = Path.GetFullPath(Path.Combine(".verification", "context-menu-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "page.html"), "<meta charset='utf-8'><a id='link' style='display:block;width:260px;height:90px' href='target.html'>Abrir destino</a><p id='text'>Texto sem link</p>");
        File.WriteAllText(Path.Combine(root, "target.html"), "<title>Destino de teste</title><p>Nova guia</p>");
        using var browser = new BrowserForm();
        _ = browser.Handle; // Do not show BrowserForm or load the user's profile.
        var tabs = (BrowserTabControl)typeof(BrowserForm).GetField("_tabView", PrivateInstance)!.GetValue(browser)!;
        using var host = new Form { ClientSize = new Size(900, 650), ShowInTaskbar = false, Opacity = 0 };
        host.Controls.Add(tabs);
        host.Show();
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(root, "profile"));
        var allowlist = new SiteAllowlist(new SiteExceptionStore(Path.Combine(root, "sites.json")));
        var manager = new BrowserTabManager(tabs, environment, allowlist);
        typeof(BrowserForm).GetField("_tabs", PrivateInstance)!.SetValue(browser, manager);
        manager.InitializeTabAsync = tab =>
        {
            tab.Web.CoreWebView2.SetVirtualHostNameToFolderMapping("context.example.test", root, CoreWebView2HostResourceAccessKind.DenyCors);
            return Task.CompletedTask;
        };

        // Native command and separator objects triggered get_Children access
        // violations before the fix. Only the submenu may expose Children.
        var command = environment.CreateContextMenuItem("Comando", null, CoreWebView2ContextMenuItemKind.Command);
        var separator = environment.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator);
        var submenu = environment.CreateContextMenuItem("Submenu", null, CoreWebView2ContextMenuItemKind.Submenu);
        submenu.Children.Add(command);
        var mixed = new List<CoreWebView2ContextMenuItem> { command, separator, submenu };
        Check(!(bool)InvokeStatic("ContainsInspectContextMenuItem", mixed)!, "Comandos e separadores devem ser percorridos sem consultar filhos.");
        Check(!(bool)InvokeStatic("ReplaceContextMenuItem", mixed, command)!, "A busca de abrir link deve atravessar somente submenus.");

        foreach (var isPrivate in new[] { false, true })
        {
            var source = await manager.CreateAsync("https://context.example.test/page.html", isPrivate)
                ?? throw new InvalidOperationException("A guia de teste não abriu.");
            var core = source.Web.CoreWebView2;
            TaskCompletionSource<bool>? requested = null;
            var selectLink = false;
            var menuCount = 0;
            core.ContextMenuRequested += (_, args) =>
            {
                try
                {
                    // Keep the native inspect entry once, then remove it so the
                    // browser must add its cached custom command on repeat menus.
                    if (menuCount++ > 0)
                        for (var i = args.MenuItems.Count - 1; i >= 0; i--)
                            if (args.MenuItems[i].Name.StartsWith("inspect", StringComparison.OrdinalIgnoreCase)) args.MenuItems.RemoveAt(i);
                    Invoke(browser, "ReplaceOpenInNewWindowCommand", source, args);
                    Invoke(browser, "EnsureInspectContextMenuItem", source, args);
                    args.Handled = true;
                    if (selectLink)
                    {
                        var item = args.MenuItems.Single(m => m.Label == "Abrir link em uma nova aba");
                        args.SelectedCommandId = item.CommandId;
                    }
                    requested?.TrySetResult(true);
                }
                catch (Exception ex) { args.Handled = true; requested?.TrySetException(ex); }
            };

            for (var repeat = 0; repeat < 4; repeat++)
            {
                tabs.SelectedTab = source;
                await WaitAsync(async () => await core.ExecuteScriptAsync("!!document.getElementById('link')") == "true");
                selectLink = true;
                requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
                await RightClickAsync(core, "link");
                await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await WaitAsync(() => Task.FromResult(tabs.TabCount == 2 && manager.Active?.Web.CoreWebView2?.Source == "https://context.example.test/target.html"));
                var opened = manager.Active!;
                Check(opened != source && opened.IsPrivate == isPrivate
                    && opened.Web.CoreWebView2.Profile.IsInPrivateModeEnabled == isPrivate, "O link deve abrir em uma única nova guia e preservar o modo anônimo.");
                Check(core.Source == "https://context.example.test/page.html", "A página original deve permanecer aberta.");
                manager.Close(opened, remember: false);
                selectLink = false;
                requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
                await RightClickAsync(core, "text");
                await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Check(tabs.TabCount == 1, "Abrir o menu em texto não deve criar uma guia.");
            }
            manager.Close(source, remember: false);
        }
        host.Controls.Remove(tabs);
        Console.WriteLine("PASS: menu nativo em links e texto, inspeção, comandos/separadores/submenus e abertura repetida em nova guia normal e anônima, sem encerrar o navegador.");
    }

    private static async Task RightClickAsync(CoreWebView2 core, string id)
    {
        using var position = JsonDocument.Parse(await core.ExecuteScriptAsync($"(()=>{{const r=document.getElementById('{id}').getBoundingClientRect();return {{x:r.left+10,y:r.top+10}};}})()"));
        var x = position.RootElement.GetProperty("x").GetDouble();
        var y = position.RootElement.GetProperty("y").GetDouble();
        foreach (var type in new[] { "mousePressed", "mouseReleased" })
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type, x, y, button = "right", clickCount = 1 }));
    }

    private static async Task WaitAsync(Func<Task<bool>> ready)
    {
        var deadline = Environment.TickCount64 + 15000;
        while (Environment.TickCount64 < deadline)
        {
            if (await ready()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("A nova guia não concluiu a abertura.");
    }

    private static object? Invoke(BrowserForm browser, string method, params object[] args) =>
        typeof(BrowserForm).GetMethod(method, PrivateInstance)!.Invoke(browser, args);
    private static object? InvokeStatic(string method, params object[] args) =>
        typeof(BrowserForm).GetMethod(method, PrivateStatic)!.Invoke(null, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
