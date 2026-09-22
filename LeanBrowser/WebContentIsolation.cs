using System.Security.Principal;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LeanBrowser;

/// <summary>
/// Security boundary for untrusted Internet content.
///
/// WebView2 uses Chromium's renderer sandbox and the normal same-origin
/// policy by default.  Unlike Electron, it has no Node.js runtime, preload
/// script, or contextBridge to turn on.  This policy keeps those guarantees
/// intact by refusing switches that disable them and by never exposing a host
/// object or a web-message channel to Internet pages.
/// </summary>
public static class WebContentIsolation
{
    private const string RequiredIsolationSwitch = "--site-per-process";

    private static readonly string[] UnsafeSwitchFragments =
    {
        "--no-sandbox",
        "--disable-gpu-sandbox",
        "--disable-setuid-sandbox",
        "--disable-web-security",
        "--disable-site-isolation-trials",
        "--single-process",
        "--in-process-gpu",
        "--allow-running-insecure-content",
        "--disable-renderer-accessibility",
        "isolateorigins",
        "siteisolation",
        "renderercodeintegrity"
    };

    /// <summary>
    /// Builds the one environment shared by every Internet tab.  Sharing a
    /// single environment does not merge origins: Chromium still assigns
    /// documents to renderer processes according to site isolation.
    /// </summary>
    public static CoreWebView2EnvironmentOptions CreateEnvironmentOptions(string performanceArguments)
    {
        RequireStandardUserHost();
        RejectUnsafeBrowserArguments(performanceArguments, "configuração do aplicativo");
        RejectUnsafeBrowserArguments(
            Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS") ?? string.Empty,
            "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS");

        return new CoreWebView2EnvironmentOptions
        {
            // Do not enable SSO based on the Windows account for arbitrary sites.
            AllowSingleSignOnUsingOSPrimaryAccount = false,

            // The only extension installed by this application is the bundled,
            // hash-verified uBlock Origin Lite package (AdProtection).  WebView2
            // must enable extensions when the environment is created; otherwise
            // AddBrowserExtensionAsync fails with a COMException.
            AreBrowserExtensionsEnabled = true,
            EnableTrackingPrevention = true,

            // Do not permit a second host process to attach to this profile and
            // share a browser process or its in-memory state.
            ExclusiveUserDataFolderAccess = true,

            // --site-per-process reinforces cross-site renderer isolation.
            // No --no-sandbox, --disable-web-security, or equivalent switch is
            // ever supplied by this application.
            AdditionalBrowserArguments = JoinArguments(RequiredIsolationSwitch, performanceArguments)
        };
    }

    /// <summary>
    /// Applies the no-bridge policy immediately after a tab's CoreWebView2 is
    /// created and before it navigates to an untrusted URL.
    /// </summary>
    public static void ConfigureUntrustedTab(WebView2 control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var core = control.CoreWebView2
            ?? throw new InvalidOperationException("CoreWebView2 must be initialized before applying the isolation policy.");

        var settings = core.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsReputationCheckingRequired = true;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsWebMessageEnabled = false;

        // Do not call AddHostObjectToScript, AddScriptToExecuteOnDocumentCreatedAsync
        // with native capabilities, WebMessageReceived, or PostWebMessage* for
        // untrusted content.  With web messaging disabled, a page has no IPC
        // route to this WinForms host.
        core.NavigationStarting += (_, args) =>
        {
            if (!IsAllowedTopLevelUri(args.Uri)) args.Cancel = true;
        };
        core.FrameCreated += (_, args) =>
        {
            args.Frame.NavigationStarting += (_, frameArgs) =>
            {
                if (!IsAllowedFrameUri(frameArgs.Uri)) frameArgs.Cancel = true;
            };
        };
    }

    private static bool IsAllowedTopLevelUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme is "http" or "https"
            || string.Equals(value, "about:blank", StringComparison.OrdinalIgnoreCase));

    private static bool IsAllowedFrameUri(string value) =>
        IsAllowedTopLevelUri(value)
        || string.Equals(value, "about:srcdoc", StringComparison.OrdinalIgnoreCase);

    private static void RequireStandardUserHost()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (principal.IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException(
                "O host do WebView2 não pode ser executado elevado. Inicie o CottonBrowser como usuário padrão.");
    }

    private static void RejectUnsafeBrowserArguments(string arguments, string source)
    {
        var normalized = arguments.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        if (UnsafeSwitchFragments.Any(normalized.Contains))
            throw new InvalidOperationException(
                $"A opção de browser insegura em {source} desabilita o sandbox, a segurança da Web ou o isolamento de sites.");
    }

    private static string JoinArguments(params string[] values) =>
        string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value)));
}
