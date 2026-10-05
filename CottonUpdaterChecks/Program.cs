using System.Diagnostics;
using CottonUpdater;

var folder = Path.Combine(Path.GetTempPath(), "cotton-updater-gate-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
var browserPath = Path.Combine(folder, "CottonBrowser.exe");
File.WriteAllText(browserPath, "test");

try
{
    var fileMutexName = @"Local\CottonBrowser.UpdateCheck." + Guid.NewGuid().ToString("N");
    using (var occupiedFile = new FileStream(browserPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        var releaseFile = Task.Run(async () =>
        {
            await Task.Delay(350);
            occupiedFile.Dispose();
        });
        var timer = Stopwatch.StartNew();
        using (UpdateInstallGate.Acquire(browserPath, fileMutexName, TimeSpan.FromSeconds(3)))
            Check(timer.Elapsed >= TimeSpan.FromMilliseconds(250), "waits for browser file lock");
        await releaseFile;
    }

    var mutexName = @"Local\CottonBrowser.UpdateCheck." + Guid.NewGuid().ToString("N");
    using var ownerReady = new ManualResetEventSlim();
    using var releaseOwner = new ManualResetEventSlim();
    var owner = Task.Run(() =>
    {
        using var mutex = new Mutex(false, mutexName);
        mutex.WaitOne();
        ownerReady.Set();
        releaseOwner.Wait();
        mutex.ReleaseMutex();
    });
    Check(ownerReady.Wait(TimeSpan.FromSeconds(3)), "test owner acquired mutex");
    var releaseTask = Task.Run(async () =>
    {
        await Task.Delay(350);
        releaseOwner.Set();
    });
    var mutexTimer = Stopwatch.StartNew();
    using (UpdateInstallGate.Acquire(browserPath, mutexName, TimeSpan.FromSeconds(3)))
        Check(mutexTimer.Elapsed >= TimeSpan.FromMilliseconds(250), "waits for browser process mutex");
    await Task.WhenAll(owner, releaseTask);

    using (var occupiedFile = new FileStream(browserPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        try
        {
            using var gate = UpdateInstallGate.Acquire(browserPath,
                @"Local\CottonBrowser.UpdateCheck." + Guid.NewGuid().ToString("N"),
                TimeSpan.FromMilliseconds(350));
            throw new Exception("An occupied file was accepted.");
        }
        catch (IOException ex)
        {
            Check(ex.Message.Contains("Feche todas as janelas", StringComparison.Ordinal),
                "timeout explains that all windows must close");
        }
    }

    Console.WriteLine("4 verificações da trava de atualização passaram.");
}
finally
{
    var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) +
        Path.DirectorySeparatorChar;
    if (Path.GetFullPath(folder).StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
        Directory.Delete(folder, recursive: true);
}

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception("Falhou: " + description);
}
