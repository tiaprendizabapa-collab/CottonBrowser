using LeanBrowser;
using System.Reflection;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        CheckPageUrlValidation();
        CheckArgumentIsolation();
        CheckBrowserMenu();
        CheckHistoryAcrossRestart();
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
            .Select(item => item.Text).SequenceEqual(["Baixar vídeo", "Baixar áudio (formato original)"]),
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
        Check(OptionValue(audio, "-f") == "bestaudio"
              && OptionValue(audio, "--ffmpeg-location") == ffmpegDirectory
              && !audio.Contains("--fixup"),
            "Audio should select an audio-only stream and provide FFmpeg for streamed formats.");

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
