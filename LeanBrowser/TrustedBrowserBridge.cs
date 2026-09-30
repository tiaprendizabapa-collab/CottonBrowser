using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LeanBrowser;

/// <summary>Maps the bundled new-tab pages to a private virtual host.</summary>
public static class TrustedBrowserBridge
{
    public const string HostName = "app.cottonbrowser.test";
    public const string NewTabUrl = "https://app.cottonbrowser.test/newtab.html";
    public const string PrivateTabUrl = "https://app.cottonbrowser.test/private.html";

    public static void Attach(WebView2 control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var core = control.CoreWebView2
            ?? throw new InvalidOperationException("CoreWebView2 must be initialized before mapping local pages.");
        var assetDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Bridge");
        if (!Directory.Exists(assetDirectory))
            throw new DirectoryNotFoundException("The browser pages were not deployed with the application.");

        core.SetVirtualHostNameToFolderMapping(
            HostName,
            assetDirectory,
            CoreWebView2HostResourceAccessKind.DenyCors);
    }
}
