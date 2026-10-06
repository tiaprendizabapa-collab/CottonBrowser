using System.Reflection;
using Microsoft.Web.WebView2.Core;

namespace LeanBrowser;

/// <summary>Recovers a stalled YouTube player without touching the profile or disabling protection.</summary>
internal static class YouTubePlaybackRecovery
{
    private static readonly Lazy<string> Script = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LeanBrowser.Assets.youtube-playback.js")
            ?? throw new FileNotFoundException("Recuperação do player ausente.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    internal static Task AttachAsync(CoreWebView2 core) => core.AddScriptToExecuteOnDocumentCreatedAsync(Script.Value);
}
