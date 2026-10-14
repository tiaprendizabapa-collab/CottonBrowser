using System.Diagnostics;
using CottonBrowser.Shared;

namespace CottonUpdater;

/// <summary>Keeps the browser closed while its installed files are replaced.</summary>
internal sealed class UpdateInstallGate : IDisposable
{
    private readonly InstallationLock _installation;

    private UpdateInstallGate(InstallationLock installation) => _installation = installation;

    internal static UpdateInstallGate Acquire(string browserPath, TimeSpan timeout, string? lockDirectory = null)
    {
        var elapsed = Stopwatch.StartNew();
        var installation = InstallationLock.AcquireUpdate(Path.GetDirectoryName(browserPath)!, timeout, lockDirectory);
        try
        {
            while (true)
            {
                try
                {
                    using var file = new FileStream(browserPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    return new UpdateInstallGate(installation);
                }
                catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
                {
                    var remaining = timeout - elapsed.Elapsed;
                    if (remaining <= TimeSpan.Zero)
                        throw new IOException("Feche todas as janelas do CottonBrowser antes de concluir a atualização.", ex);
                    Thread.Sleep(remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250));
                }
            }
        }
        catch
        {
            installation.Dispose();
            throw;
        }
    }

    public void Dispose() => _installation.Dispose();
}
