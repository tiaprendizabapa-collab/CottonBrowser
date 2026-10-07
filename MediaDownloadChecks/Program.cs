using LeanBrowser;
using System.Reflection;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        CheckPageUrlValidation();
        CheckArgumentIsolation();
        CheckBrowserMenu();
        CheckHistoryAcrossRestart();
        if (args is ["--conversion-tools", var toolsRoot])
            Task.Run(() => CheckMp3ConversionAsync(toolsRoot)).GetAwaiter().GetResult();
        Console.WriteLine("PASS: validação de URL, argumentos isolados e histórico de mídia sem acesso à rede.");
    }

    private static void CheckBrowserMenu()
    {
        using var browser = new BrowserForm();
        var field = typeof(BrowserForm).GetField("_overflowMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        var menu = field?.GetValue(browser) as BrowserOverflowMenu;
        Check(menu is not null, "The browser menu should exist.");
        var media = menu!.Items.OfType<BrowserMenuItem>()
            .SingleOrDefault(item => item.Text == "Baixar mídia desta página");
        Check(media is not null && media.DropDownItems.OfType<BrowserMenuItem>()
            .Select(item => item.Text).SequenceEqual(["Baixar vídeo", "Baixar áudio (MP3)"]),
            "The three-dot menu should expose video and audio downloads.");
    }

    private static void CheckArgumentIsolation()
    {
        const string pageUrl = "https://example.com/watch?v=one&next=two;echo";
        var root = Path.Combine(Path.GetTempPath(), "Cotton media checks");
        var outputDirectory = Path.Combine(root, "output folder");
        var denoPath = Path.Combine(root, "deno.exe");
        var ffmpegDirectory = Path.Combine(root, "ffmpeg folder");

        var video = MediaDownloadService.BuildDownloadArguments(pageUrl, MediaDownloadKind.Video,
            outputDirectory, denoPath, ffmpegDirectory);
        var audio = MediaDownloadService.BuildDownloadArguments(pageUrl, MediaDownloadKind.Audio,
            outputDirectory, denoPath, ffmpegDirectory);

        foreach (var args in new[] { video, audio })
        {
            Check(args.Contains("--ignore-config") && args.Contains("--no-plugin-dirs")
                  && args.Contains("--no-js-runtimes"),
                "Media tools should not load user configuration, plugins, or arbitrary JavaScript runtimes.");
            Check(args.Contains("--no-playlist")
                  && OptionValue(args, "--playlist-items") == "1"
                  && !args.Contains("--max-downloads"),
                "A page download should be limited to one item.");
            Check(OptionValue(args, "--js-runtimes") == "deno:" + denoPath,
                "The configured Deno executable should be passed as one argument.");
            Check(OptionValue(args, "--encoding") == "utf-8",
                "Downloader output should use UTF-8 for progress and file paths.");
            Check(OptionValue(args, "-P") == outputDirectory,
                "The output directory should be passed as one argument even with spaces.");
            var outputTemplate = OptionValue(args, "-o");
            Check(Path.GetFileName(outputTemplate) == outputTemplate
                  && !outputTemplate.Contains("..", StringComparison.Ordinal),
                "The output template should remain inside the selected directory.");
            Check(args[^2] == "--" && args[^1] == pageUrl,
                "The page URL should stay one positional argument after the option terminator.");
        }

        Check(OptionValue(video, "-f") == "bestvideo+bestaudio/best"
              && OptionValue(video, "--ffmpeg-location") == ffmpegDirectory,
            "Video should request combined streams with the configured FFmpeg directory.");
        Check(OptionValue(audio, "-f") == "bestaudio/best"
              && OptionValue(audio, "--ffmpeg-location") == ffmpegDirectory
              && audio.Contains("--extract-audio")
              && OptionValue(audio, "--audio-format") == "mp3"
              && OptionValue(audio, "--audio-quality") == "0"
              && !audio.Contains("--fixup"),
            "Audio should extract and convert the best available audio to MP3 using FFmpeg.");
        Check(!video.Contains("--extract-audio") && !video.Contains("--audio-format"),
            "Video downloads should retain their existing stream selection and output format.");

        ExpectArgumentException(() => MediaDownloadService.BuildDownloadArguments("file:///video.mp4",
            MediaDownloadKind.Video, outputDirectory, denoPath, ffmpegDirectory));
        ExpectArgumentException(() => MediaDownloadService.BuildDownloadArguments(pageUrl,
            MediaDownloadKind.Video, "relative/output", denoPath, ffmpegDirectory));
        ExpectArgumentException(() => MediaDownloadService.BuildDownloadArguments(pageUrl,
            MediaDownloadKind.Video, outputDirectory, "relative/deno.exe", ffmpegDirectory));
        ExpectArgumentException(() => MediaDownloadService.BuildDownloadArguments(pageUrl,
            MediaDownloadKind.Video, outputDirectory, denoPath, null));
        ExpectArgumentException(() => MediaDownloadService.BuildDownloadArguments(pageUrl,
            MediaDownloadKind.Audio, outputDirectory, denoPath, null));
    }

    // Optional integration check using the installed tools and a synthetic local
    // video. No public site or existing user download is accessed.
    private static async Task CheckMp3ConversionAsync(string toolsRoot)
    {
        var directory = Path.GetFullPath(Path.Combine(".verification", "mp3-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var ffmpegDirectory = Path.Combine(toolsRoot, "ffmpeg-2026-09-30");
        var input = Path.Combine(directory, "fixture.webm");
        await RunToolAsync(Path.Combine(ffmpegDirectory, "ffmpeg.exe"),
            ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=blue:s=160x90:r=10",
             "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "2",
             "-c:v", "libvpx", "-c:a", "libopus", input]);
        var bytes = await File.ReadAllBytesAsync(input);
        var portSelector = new TcpListener(IPAddress.Loopback, 0);
        portSelector.Start();
        var port = ((IPEndPoint)portSelector.LocalEndpoint).Port;
        portSelector.Stop();
        using var server = new HttpListener();
        var baseUrl = $"http://127.0.0.1:{port}/";
        server.Prefixes.Add(baseUrl);
        server.Start();
        var serving = ServeAsync();
        try
        {
            using var service = new MediaDownloadService(toolsRoot: toolsRoot, downloadsDirectory: Path.Combine(directory, "downloads"));
            Check(service.ToolsReady(MediaDownloadKind.Audio), "Installed tools must be available for the integration check.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var messages = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var progress = new RecordingProgress(messages);
            var result = await service.DownloadAsync(baseUrl + "fixture.webm", MediaDownloadKind.Audio, progress, timeout.Token);
            Check(Path.GetExtension(result.FilePath) == ".mp3" && result.FileSize > 0, "The completed download must be an MP3 file.");
            var probe = await RunToolAsync(Path.Combine(ffmpegDirectory, "ffprobe.exe"),
                ["-v", "error", "-show_streams", "-show_format", "-of", "json", result.FilePath]);
            using var data = JsonDocument.Parse(probe);
            var streams = data.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            Check(streams.Length == 1 && streams[0].GetProperty("codec_name").GetString() == "mp3"
                && streams[0].GetProperty("codec_type").GetString() == "audio", "The output must contain real MP3 audio and no video stream.");
            Check(!Directory.Exists(Path.Combine(directory, "downloads", ".cotton-media-tmp")), "Converted source and temporary files must be removed.");
            Check(messages.Any(m => m.Contains("Convertendo áudio para MP3")), "The conversion phase must be reported.");
            Console.WriteLine("PASS: download local de vídeo WebM/Opus convertido em MP3 real, sem vídeo, com progresso e limpeza dos temporários.");
            Console.WriteLine("MP3 de teste: " + result.FilePath);
        }
        finally
        {
            server.Stop();
            await serving;
        }

        async Task ServeAsync()
        {
            try
            {
                while (server.IsListening)
                {
                    var context = await server.GetContextAsync();
                    context.Response.ContentType = "video/webm";
                    context.Response.ContentLength64 = bytes.Length;
                    if (context.Request.HttpMethod != "HEAD") await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException && !server.IsListening) { }
        }
    }

    private sealed class RecordingProgress(System.Collections.Concurrent.ConcurrentQueue<string> messages) : IProgress<MediaDownloadProgress>
    {
        public void Report(MediaDownloadProgress progress) => messages.Enqueue(progress.Message);
    }

    private static async Task<string> RunToolAsync(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Media tool did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Check(process.ExitCode == 0, "Media tool failed: " + await error);
        return await output;
    }

    private static string OptionValue(IReadOnlyList<string> args, string option)
    {
        var index = -1;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] != option) continue;
            Check(index < 0, $"Option {option} should appear once.");
            index = i;
        }
        Check(index >= 0 && index < args.Count - 1, $"Option {option} needs a value.");
        return args[index + 1];
    }

    private static void ExpectArgumentException(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Invalid media arguments should be rejected before starting a tool.");
    }

    private static void CheckPageUrlValidation()
    {
        var cases = new (string? Url, bool Supported)[]
        {
            ("https://example.com/watch?v=123", true),
            ("http://localhost:8080/video", true),
            ("HTTPS://example.com/live", true),
            ("https://app.cottonbrowser.test/newtab.html", false),
            ("https://cottonbrowser.test/", false),
            ("https://user:pass@example.com/watch", false),
            ("https://example.com/\nwatch", false),
            (null, false),
            ("", false),
            ("  ", false),
            ("example.com/watch", false),
            ("//example.com/watch", false),
            ("file:///C:/video.mp4", false),
            ("javascript:alert(1)", false),
            ("data:text/html,hello", false),
            ("ftp://example.com/video", false)
        };

        foreach (var (url, supported) in cases)
            Check(MediaDownloadService.IsSupportedPageUrl(url) == supported,
                $"URL '{url ?? "(null)"}' should be {(supported ? "accepted" : "rejected")}.");
    }

    private static void CheckHistoryAcrossRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CottonMediaChecks", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "downloads.json");
        try
        {
            var sourceUrl = "https://example.com/watch?v=123&list=456";
            var entry = new DownloadEntry(Guid.NewGuid(), "clip.mp4", sourceUrl,
                Path.Combine(directory, "clip.mp4"), 1024, -1, DownloadStatus.InProgress,
                null, DateTimeOffset.UtcNow, null);
            new DownloadHistoryStore(path).Upsert(entry);

            var reopenedStore = new DownloadHistoryStore(path);
            var interrupted = reopenedStore.Snapshot().Single();
            Check(interrupted.Id == entry.Id && interrupted.SourceUrl == sourceUrl,
                "Restart should retain the media source URL and entry identity.");
            Check(interrupted.Status == DownloadStatus.Interrupted,
                "A media download left in progress should be interrupted after restart.");

            reopenedStore.Upsert(interrupted with
            {
                Status = DownloadStatus.Completed,
                FinishedAt = DateTimeOffset.UtcNow
            });
            var completed = new DownloadHistoryStore(path).Snapshot().Single();
            Check(completed.Status == DownloadStatus.Completed && completed.FinishedAt is not null,
                "A finished media download should remain completed after restart.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
