using System.Reflection;
using System.Text.Json;
using LeanBrowser;
using CottonBrowser.Shared;
using Microsoft.Web.WebView2.Core;

internal static class Program
{
    internal static readonly string Work = Path.GetFullPath(Path.Combine(".verification", "youtube-" + Guid.NewGuid().ToString("N")));
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize(); Directory.CreateDirectory(Work);
        using var form = new Form { Width = 1024, Height = 760, Opacity = 0, ShowInTaskbar = false };
        form.Shown += async (_, _) =>
        {
            try { if (args.Contains("--probe")) await ProbeAsync(form, args.Contains("--fault")); else await RegressionAsync(form); }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { form.Close(); }
        };
        Application.Run(form);
    }

    private static async Task ProbeAsync(Form form, bool injectFault)
    {
        var arguments = (string)typeof(BrowserForm).GetMethod("BrowserArguments", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        var options = WebContentIsolation.CreateEnvironmentOptions(arguments + " --autoplay-policy=no-user-gesture-required");
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Work, "profile"), options);
        using var web = new TabWebView { Dock = DockStyle.Fill }; form.Controls.Add(web);
        await web.EnsureCoreWebView2Async(env); var core = web.CoreWebView2;
        WebContentIsolation.ConfigureUntrustedTab(web);
        var allowlist = new SiteAllowlist(new SiteExceptionStore(Path.Combine(Work, "sites.json")));
        var blocker = new AdBlocker(allowlist); blocker.Attach(core);
        var extension = new AdProtection(); await extension.InitializeAsync(core, Work);
        Console.WriteLine("Runtime=" + env.BrowserVersionString + "; blocker=" + extension.Available + "; failure=" + extension.Failure);
        var document = new DocumentProtection(); await document.SetEnabledAsync(core, true);
        await AttachRecoveryAsync(core);
        var faultUntil = DateTimeOffset.UtcNow.AddSeconds(25); var injected = 0;
        if (injectFault)
        {
            core.AddWebResourceRequestedFilter("https://*.googlevideo.com/videoplayback*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) =>
            {
                if (DateTimeOffset.UtcNow >= faultUntil) return;
                injected++;
                args.Response = env.CreateWebResourceResponse(null, 503, "Temporary fixture failure", "Access-Control-Allow-Origin: *\r\nCache-Control: no-store");
            };
        }
        var requests = new Dictionary<string, string>();
        await core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
        core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent").DevToolsProtocolEventReceived += (_, args) =>
        {
            using var data = JsonDocument.Parse(args.ParameterObjectAsJson);
            var root = data.RootElement; var url = root.GetProperty("request").GetProperty("url").GetString();
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) requests[root.GetProperty("requestId").GetString()!] = uri.Host + uri.AbsolutePath;
        };
        core.GetDevToolsProtocolEventReceiver("Network.loadingFailed").DevToolsProtocolEventReceived += (_, args) =>
        {
            using var data = JsonDocument.Parse(args.ParameterObjectAsJson); var root = data.RootElement;
            var id = root.GetProperty("requestId").GetString()!;
            if (requests.TryGetValue(id, out var route) && (route.Contains("googlevideo") || route.Contains("youtube")))
                Console.WriteLine("FAIL " + route + " " + root.GetProperty("errorText").GetString());
        };
        foreach (var enabled in injectFault ? new[] { true } : new[] { true, false })
        {
            await extension.SetEnabledAsync(enabled); blocker.Enabled = enabled; await document.SetEnabledAsync(core, enabled);
            core.Navigate("https://www.youtube.com/watch?v=ZvXjl20nB6Q");
            for (var count = 0; count < (injectFault ? 10 : 4); count++)
            {
                await Task.Delay(5000);
                if (count == 0) await core.ExecuteScriptAsync("(() => {const p=document.querySelector('#movie_player');if(typeof p?.loadVideoById==='function'){window.probeRetries=0;const original=p.loadVideoById;p.loadVideoById=function(...args){window.probeRetries++;return original.apply(this,args)}}})()");
                Console.WriteLine("PROBE protection=" + enabled + " " + await core.ExecuteScriptAsync("JSON.stringify({video:Array.from(document.querySelectorAll('video')).map(v=>({time:v.currentTime,duration:v.duration,ready:v.readyState,network:v.networkState,paused:v.paused,error:v.error?.code})),state:document.querySelector('#movie_player')?.getPlayerState?.(),reloadAPI:typeof document.querySelector('#movie_player')?.loadVideoById,playability:document.querySelector('#movie_player')?.getPlayerResponse?.()?.playabilityStatus?.status,recovery:!!window.__COTTON_YOUTUBE_PLAYBACK__,retries:window.probeRetries,notice:!!document.querySelector('[data-cotton-video-recovery]'),error:document.querySelector('.ytp-error-content-wrap-reason')?.textContent})"));
            }
        }
        if (injectFault)
        {
            Check(injected > 0, "O teste deve provocar uma falha de mídia real.");
            Check(await core.ExecuteScriptAsync("window.probeRetries===1 && document.querySelector('video').readyState>=2 && document.querySelector('video').currentTime>0") == "true", "O player real deve recuperar a reprodução após uma tentativa automática.");
            Console.WriteLine("PASS: falha temporária em " + injected + " requisições de mídia; player real recuperou a reprodução após uma tentativa, com proteção ativa.");
        }
    }
    private static async Task AttachRecoveryAsync(CoreWebView2 core)
    {
        var type = typeof(BrowserForm).Assembly.GetType("LeanBrowser.YouTubePlaybackRecovery")!;
        await (Task)type.GetMethod("AttachAsync", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [core])!;
    }
    private static async Task RegressionAsync(Form form)
    {
        using var web = new TabWebView { Dock = DockStyle.Fill }; form.Controls.Add(web);
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Work, "regression-profile"));
        await web.EnsureCoreWebView2Async(env); var core = web.CoreWebView2;
        core.Settings.IsWebMessageEnabled = false;
        var fixture = """
            <meta charset='utf-8'><div id='movie_player' style='width:600px;height:350px'><video></video></div>
            <script>
            window.now=0; window.tick=null; window.loads=[]; window.state=3; window.status='OK'; window.hidden=false;
            window.ready=0; window.duration=NaN; window.time=0; window.paused=false; window.live=false;
            Date.now=()=>now; window.setInterval=f=>(tick=f,1); window.clearInterval=()=>{tick=null};
            Object.defineProperty(document,'visibilityState',{get:()=>hidden?'hidden':'visible'});
            const video=document.querySelector('video');
            for(const [name,key] of [['readyState','ready'],['duration','duration'],['currentTime','time'],['paused','paused']])
              Object.defineProperty(video,name,{get:()=>window[key]});
            video.muted=true; video.volume=0.35; video.playbackRate=1.5;
            const player=document.querySelector('#movie_player');
            player.getVideoData=()=>({video_id:new URLSearchParams(location.search).get('v'),isLive:live});
            player.getPlayerState=()=>state; player.getPlayerResponse=()=>({playabilityStatus:{status}});
            player.loadVideoById=(id,start)=>{loads.push({id,start});video.muted=false;video.volume=1;video.playbackRate=1};
            window.advance=ms=>{now+=ms;tick?.()};
            </script>
            """;
        File.WriteAllText(Path.Combine(Work, "watch"), fixture);
        foreach (var fixtureHost in new[] { "www.youtube.com", "fixture.example.test" })
            core.AddWebResourceRequestedFilter("https://" + fixtureHost + "/watch*", CoreWebView2WebResourceContext.Document);
        core.WebResourceRequested += (_, args) => args.Response = env.CreateWebResourceResponse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fixture)), 200, "OK", "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
        // Register after the fixture has replaced its clock/timers, matching an
        // injected production script but avoiding twenty-second waits per case.
        string script;
        using (var stream = typeof(BrowserForm).Assembly.GetManifestResourceStream("LeanBrowser.Assets.youtube-playback.js")!)
        using (var reader = new StreamReader(stream)) script = await reader.ReadToEndAsync();
        async Task LoadAsync(string extra = "", string host = "www.youtube.com")
        {
            var complete = new TaskCompletionSource<bool>(); ulong? id = null;
            void Starting(object? sender, CoreWebView2NavigationStartingEventArgs e) => id = e.NavigationId;
            void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e) { if (e.NavigationId == id) complete.TrySetResult(e.IsSuccess); }
            core.NavigationStarting += Starting; core.NavigationCompleted += Completed;
            try { core.Navigate("https://" + host + "/watch?v=ZvXjl20nB6Q&case=" + Guid.NewGuid().ToString("N")); Check(await complete.Task.WaitAsync(TimeSpan.FromSeconds(10)), "Fixture deve carregar."); }
            finally { core.NavigationStarting -= Starting; core.NavigationCompleted -= Completed; }
            if (extra.Length > 0) await core.ExecuteScriptAsync(extra);
            await core.ExecuteScriptAsync(script); await core.ExecuteScriptAsync("tick?.()");
        }
        async Task AssertAsync(string expression, string failure)
        {
            if (await core.ExecuteScriptAsync(expression) == "true") return;
            Console.Error.WriteLine(await core.ExecuteScriptAsync("JSON.stringify({url:location.href,flag:window.__COTTON_YOUTUBE_PLAYBACK__,now:window.now,loads:window.loads,hasTick:!!window.tick,visible:document.visibilityState,online:navigator.onLine,state:window.state,ready:window.ready,player:!!document.querySelector('#movie_player'),muted:document.querySelector('video')?.muted,volume:document.querySelector('video')?.volume,rate:document.querySelector('video')?.playbackRate})"));
            Check(false, failure);
        }
        await LoadAsync(); await core.ExecuteScriptAsync("advance(21000)");
        await AssertAsync("loads.length===1 && loads[0].id==='ZvXjl20nB6Q' && loads[0].start===0 && document.querySelector('video').muted && Math.abs(document.querySelector('video').volume-0.35)<0.001 && document.querySelector('video').playbackRate===1.5", "Player sem metadados deve tentar novamente, preservando áudio e velocidade.");
        await core.ExecuteScriptAsync("advance(22000);advance(30000)");
        await AssertAsync("loads.length===1 && document.querySelectorAll('[data-cotton-video-recovery]').length===1", "A falha persistente deve oferecer recuperação sem repetir automaticamente.");
        await core.ExecuteScriptAsync("document.querySelector('[data-cotton-video-recovery] button').click()");
        await AssertAsync("loads.length===2 && !document.querySelector('[data-cotton-video-recovery]')", "O botão deve repetir apenas o carregamento do vídeo.");
        await core.ExecuteScriptAsync("ready=4;duration=238;time=3;advance(30000)");
        await AssertAsync("loads.length===2 && !document.querySelector('[data-cotton-video-recovery]')", "A recuperação não pode interromper reprodução saudável.");
        await LoadAsync("duration=238"); await core.ExecuteScriptAsync("advance(21000)");
        await AssertAsync("loads.length===1", "A duração anunciada pelo YouTube não pode esconder uma falha sem metadados.");
        foreach (var setup in new[] { "ready=4;duration=238;time=12", "ready=1;duration=Infinity", "live=true;duration=Infinity", "state=2;paused=true", "state=5;paused=true", "status='UNPLAYABLE'", "hidden=true" })
        {
            await LoadAsync(setup); await core.ExecuteScriptAsync("advance(30000);advance(30000)");
            await AssertAsync("loads.length===0 && !document.querySelector('[data-cotton-video-recovery]')", "Não recuperar automaticamente vídeo saudável, live, pausado, indisponível ou aba oculta: " + setup);
        }
        await LoadAsync("hidden=true"); await core.ExecuteScriptAsync("advance(30000);hidden=false;advance(1000);advance(19000)");
        await AssertAsync("loads.length===0", "Uma aba recém-exibida deve ter tempo para carregar.");
        await core.ExecuteScriptAsync("advance(2000)"); await AssertAsync("loads.length===1", "Falha persistente na aba visível deve recuperar após o prazo.");
        await LoadAsync(); await core.ExecuteScriptAsync("advance(21000);history.replaceState(null,'','/watch?v=8sgycukafqQ');advance(1000);advance(21000)");
        await AssertAsync("loads.length===2 && loads[1].id==='8sgycukafqQ'", "Trocar de vídeo na SPA deve permitir a recuperação do vídeo novo.");
        await LoadAsync("", "fixture.example.test"); await AssertAsync("!window.__COTTON_YOUTUBE_PLAYBACK__", "Sites diferentes não devem receber o monitor do YouTube.");
        Console.WriteLine("PASS: recuperação limitada de 0:00/0:00, controles manuais, preferências de mídia, SPA e visibilidade; reprodução normal, lives, pausa e erros permanentes preservados.");
    }
    private static void Check(bool condition, string failure) { if (!condition) throw new InvalidOperationException(failure); }
}
