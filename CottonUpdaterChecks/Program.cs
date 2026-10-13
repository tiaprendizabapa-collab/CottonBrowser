using System.Diagnostics;
using CottonUpdater;
using CottonBrowser.Shared;

var folder = Path.Combine(Path.GetTempPath(), "cotton-updater-gate-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
var browserPath = Path.Combine(folder, "CottonBrowser.exe");
var lockDirectory = Path.Combine(folder, "locks");
var checkCount = 0;
File.WriteAllText(browserPath, "test");

try
{
    using (var occupiedFile = new FileStream(browserPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        var releaseFile = Task.Run(async () =>
        {
            await Task.Delay(350);
            occupiedFile.Dispose();
        });
        var timer = Stopwatch.StartNew();
        using (UpdateInstallGate.Acquire(browserPath, TimeSpan.FromSeconds(3), lockDirectory))
            Check(timer.Elapsed >= TimeSpan.FromMilliseconds(250), "waits for browser file lock");
        await releaseFile;
    }

    using (var firstProfile = InstallationLock.AcquireBrowser(folder, lockDirectory))
    using (var secondProfile = InstallationLock.AcquireBrowser(folder, lockDirectory))
    {
        Check(true, "multiple browser profiles share the installation");
        firstProfile.Dispose();
        try
        {
            using var gate = UpdateInstallGate.Acquire(browserPath, TimeSpan.Zero, lockDirectory);
            throw new Exception("Update accepted while another profile remained open.");
        }
        catch (IOException ex)
        {
            Check(ex.Message.Contains("perfis", StringComparison.Ordinal), "remaining profile blocks the update");
        }
    }

    var profile = InstallationLock.AcquireBrowser(folder, lockDirectory);
    var releaseTask = Task.Run(async () =>
    {
        await Task.Delay(350);
        profile.Dispose();
    });
    var profileTimer = Stopwatch.StartNew();
    using (UpdateInstallGate.Acquire(browserPath, TimeSpan.FromSeconds(3), lockDirectory))
        Check(profileTimer.Elapsed >= TimeSpan.FromMilliseconds(250), "waits until the last profile closes");
    await releaseTask;

    using (var gate = UpdateInstallGate.Acquire(browserPath, TimeSpan.Zero, lockDirectory))
    {
        try
        {
            using var browser = InstallationLock.AcquireBrowser(folder + Path.DirectorySeparatorChar, lockDirectory);
            throw new Exception("Browser started during update.");
        }
        catch (IOException ex)
        {
            Check(ex.Message.Contains("Aguarde", StringComparison.Ordinal), "browser startup is blocked while updating");
        }
        try
        {
            using var secondUpdate = UpdateInstallGate.Acquire(browserPath, TimeSpan.Zero, lockDirectory);
            throw new Exception("Two updates acquired the same installation.");
        }
        catch (IOException) { Check(true, "concurrent updates are blocked"); }
        var otherFolder = Path.Combine(folder, "portable");
        using var otherBrowser = InstallationLock.AcquireBrowser(otherFolder, lockDirectory);
        Check(true, "another installation can run during this update");
    }
    using (var browser = InstallationLock.AcquireBrowser(folder, lockDirectory))
        Check(true, "update disposal releases browser startup");

    using (var occupiedFile = new FileStream(browserPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        try
        {
            using var gate = UpdateInstallGate.Acquire(browserPath,
                TimeSpan.FromMilliseconds(350), lockDirectory);
            throw new Exception("An occupied file was accepted.");
        }
        catch (IOException ex)
        {
            Check(ex.Message.Contains("Feche todas as janelas", StringComparison.Ordinal),
                "timeout explains that all windows must close");
        }
    }

    using (var browser = InstallationLock.AcquireBrowser(folder, lockDirectory))
        Check(true, "file-lock failure releases the installation lock");

    try
    {
        using var gate = UpdateInstallGate.Acquire(Path.Combine(folder, "missing.exe"), TimeSpan.Zero, lockDirectory);
        throw new Exception("Missing browser file accepted.");
    }
    catch (FileNotFoundException) { Check(true, "missing browser file is rejected"); }
    using (var browser = InstallationLock.AcquireBrowser(folder, lockDirectory))
        Check(true, "unexpected failure releases the installation lock");

    var installation = Path.Combine(folder, "installation");
    var extracted = Path.Combine(folder, "extracted");
    Directory.CreateDirectory(installation);
    Directory.CreateDirectory(Path.Combine(extracted, "Assets", "Bridge"));
    File.WriteAllText(Path.Combine(installation, "CottonBrowser.exe"), "previous browser");
    File.WriteAllText(Path.Combine(installation, "CottonBrowserUninstall.exe"), "uninstaller");
    var manifest = Path.Combine(installation, "CottonBrowserInstallFiles.txt");
    File.WriteAllLines(manifest, ["CottonBrowser.exe", "Assets/Bridge/previous.html"]);
    File.WriteAllText(Path.Combine(extracted, "CottonBrowser.exe"), "updated browser");
    var newPage = Path.Combine("Assets", "Bridge", "new.html");
    File.WriteAllText(Path.Combine(extracted, newPage), "new page");
    UpdateFileTransaction.Apply(installation, extracted, Path.Combine(folder, "backup"), ["CottonBrowser.exe", newPage]);
    Check(File.ReadAllText(Path.Combine(installation, "CottonBrowser.exe")) == "updated browser",
        "successful update replaces browser files");
    Check(File.ReadAllLines(manifest).Contains("Assets/Bridge/new.html") &&
        File.ReadAllLines(manifest).Contains("Assets/Bridge/previous.html"),
        "uninstaller tracks new assets and keeps previous files for removal");

    var blockedPage = Path.Combine("Assets", "Bridge", "blocked.html");
    Directory.CreateDirectory(Path.Combine(installation, blockedPage));
    File.WriteAllText(Path.Combine(extracted, blockedPage), "blocked page");
    var temporaryPage = Path.Combine("Assets", "Bridge", "temporary.html");
    File.WriteAllText(Path.Combine(extracted, temporaryPage), "temporary page");
    File.WriteAllText(Path.Combine(extracted, "CottonBrowser.exe"), "failed update browser");
    var previousManifest = File.ReadAllText(manifest);
    try
    {
        UpdateFileTransaction.Apply(installation, extracted, Path.Combine(folder, "failed-backup"),
            ["CottonBrowser.exe", temporaryPage, blockedPage]);
        throw new Exception("Blocked destination accepted.");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Check(File.ReadAllText(Path.Combine(installation, "CottonBrowser.exe")) == "updated browser",
            "failed update restores files already replaced");
        Check(!File.Exists(Path.Combine(installation, temporaryPage)), "failed update removes newly created files");
        Check(File.ReadAllText(manifest) == previousManifest, "failed update preserves the uninstall list");
    }

    var portable = Path.Combine(folder, "portable");
    Directory.CreateDirectory(portable);
    UpdateFileTransaction.Apply(portable, extracted, Path.Combine(folder, "portable-backup"), ["CottonBrowser.exe"]);
    Check(!File.Exists(Path.Combine(portable, "CottonBrowserInstallFiles.txt")),
        "portable update does not create an uninstall list");

    Console.WriteLine($"{checkCount} verificações da instalação de atualização passaram.");
}
finally
{
    var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) +
        Path.DirectorySeparatorChar;
    if (Path.GetFullPath(folder).StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
        Directory.Delete(folder, recursive: true);
}

void Check(bool condition, string description)
{
    checkCount++;
    if (!condition) throw new Exception("Falhou: " + description);
}
