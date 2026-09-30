using System.Diagnostics;

namespace CottonUpdater;

/// <summary>Keeps the browser closed while its installed files are replaced.</summary>
internal sealed class UpdateInstallGate : IDisposable
{
    private readonly Mutex _mutex;

    private UpdateInstallGate(Mutex mutex) => _mutex = mutex;

    internal static UpdateInstallGate Acquire(string browserPath, string mutexName, TimeSpan timeout)
    {
        var mutex = new Mutex(initiallyOwned: false, mutexName);
        var held = false;
        var elapsed = Stopwatch.StartNew();
        try
        {
            try { held = mutex.WaitOne(timeout); }
            catch (AbandonedMutexException) { held = true; }
            if (!held)
                throw new IOException("Feche todas as janelas do CottonBrowser antes de concluir a atualização.");

            while (true)
            {
                try
                {
                    using var file = new FileStream(browserPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    return new UpdateInstallGate(mutex);
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
            if (held) mutex.ReleaseMutex();
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
