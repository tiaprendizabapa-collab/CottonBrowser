using Microsoft.Web.WebView2.Core;

namespace LeanBrowser;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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

        Application.Run(new BrowserForm());
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
