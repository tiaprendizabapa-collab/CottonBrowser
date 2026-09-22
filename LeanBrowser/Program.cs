using Microsoft.Web.WebView2.Core;
using System.Threading;

namespace LeanBrowser;

internal static class Program
{
    private const string SingleInstanceMutexName = "Local\\CottonBrowser.SingleInstance.v1";

    [STAThread]
    private static void Main()
    {
        // The WebView2 profile is intentionally exclusive. Detect a second
        // launch before the runtime tries to attach to that profile, which
        // otherwise surfaces as an unhelpful COM "Catastrophic failure".
        using var instanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out var ownsInstance);

        if (!ownsInstance)
        {
            MessageBox.Show(
                "O CottonBrowser ja esta aberto. Feche a instancia atual antes de iniciar outra.",
                "CottonBrowser",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        // Sem visual styles do GDI+ tematizado onde nao precisamos: a UI e
        // desenhada a mao. Mantemos ApplicationConfiguration para DPI correto.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (!RuntimeAvailable(out var version))
        {
            MessageBox.Show(
                "O Microsoft Edge WebView2 Runtime nao foi encontrado.\n\n" +
                "Baixe o instalador 'Evergreen Bootstrapper' em:\n" +
                "https://developer.microsoft.com/microsoft-edge/webview2/\n\n" +
                "Ele ja vem pre-instalado no Windows 11 e na maioria dos " +
                "Windows 10 atualizados.",
                "CottonBrowser", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        System.Diagnostics.Debug.WriteLine($"WebView2 Runtime: {version}");

        try
        {
            Application.Run(new BrowserForm());
        }
        finally
        {
            instanceMutex.ReleaseMutex();
        }
    }

    private static bool RuntimeAvailable(out string version)
    {
        version = string.Empty;

        try
        {
            version = CoreWebView2Environment.GetAvailableBrowserVersionString() ?? string.Empty;
            return !string.IsNullOrEmpty(version);
        }
        catch
        {
            return false;
        }
    }
}
