using System.Reflection;
using Microsoft.Web.WebView2.Core;

namespace LeanBrowser;

/// <summary>Adapta a proteção visual do pacote enviado ao ciclo de documentos do WebView2.</summary>
public sealed class DocumentProtection
{
    private string? _scriptId;
    private static readonly Lazy<string> Script = new(() =>
        "(() => { if (!/^https?:$/.test(location.protocol)) return;\n" +
        Read("Recovery.dialog-guard.js") + "\n" +
        Read("Recovery.overlay-cleaner.js") + "\n" +
        "window.__BC_DIALOGS_113__?.configure('known');\n" +
        "if (!(location.hostname === 'youtube.com' || location.hostname.endsWith('.youtube.com'))) window.__BC_CLEANER_113__?.configure(true);\n" +
        Read("youtube-protection.js") + "\n})();");

    private static string Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LeanBrowser.Assets." + name)
            ?? throw new FileNotFoundException("Proteção de páginas ausente: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public async Task SetEnabledAsync(CoreWebView2 core, bool enabled)
    {
        if (enabled && _scriptId is null)
            _scriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(Script.Value);
        else if (!enabled && _scriptId is not null)
        {
            core.RemoveScriptToExecuteOnDocumentCreated(_scriptId);
            _scriptId = null;
        }
        // O chamador recarrega a página ao alternar, removendo também as alterações visuais.
    }
}
