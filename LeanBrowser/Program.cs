using Microsoft.Web.WebView2.Core;
using System.Drawing;
using System.Linq;
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
                "O CottonBrowser ja esta aberto. Para abrir outra janela, use Ctrl+N ou Nova janela no menu de tres pontos.",
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
            Application.Run(new BrowserApplicationContext());
        }
        finally
        {
            instanceMutex.ReleaseMutex();
        }
    }

    private sealed class BrowserApplicationContext : ApplicationContext
    {
        private readonly HashSet<BrowserForm> _windows = new();
        public BrowserApplicationContext() => OpenNewWindow();
        private void OpenNewWindow() => CreateBrowserWindow();

        private BrowserForm CreateBrowserWindow(string? initialUrl = null, bool isPrivate = false)
        {
            var previous = _windows.LastOrDefault();
            var form = new BrowserForm(initialUrl, isPrivate);
            if (previous is not null)
            {
                var workArea = Screen.FromControl(previous).WorkingArea;
                var offset = ((_windows.Count - 1) % 5 + 1) * 28;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(
                    Math.Clamp(previous.Left + offset, workArea.Left, Math.Max(workArea.Left, workArea.Right - form.Width)),
                    Math.Clamp(previous.Top + offset, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - form.Height)));
            }
            form.NewWindowRequested += OpenNewWindow;
            form.DetachedTabWindowRequested += OpenDetachedTabWindowAsync;
            form.FormClosed += OnBrowserWindowClosed;
            _windows.Add(form);
            form.Show();
            return form;
        }

        private async Task<bool> OpenDetachedTabWindowAsync(string url, bool isPrivate)
        {
            var form = CreateBrowserWindow(url, isPrivate);
            return await form.InitialTabReady;
        }
        private void OnBrowserWindowClosed(object? sender, FormClosedEventArgs e)
        {
            if (sender is not BrowserForm form || !_windows.Remove(form)) return;
            form.NewWindowRequested -= OpenNewWindow;
            form.DetachedTabWindowRequested -= OpenDetachedTabWindowAsync;
            if (_windows.Count == 0) ExitThread();
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
