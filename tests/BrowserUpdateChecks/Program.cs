using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LeanBrowser;

var failures = new List<string>();
var checkCount = 0;
void Check(bool condition, string name)
{
    checkCount++;
    if (!condition) failures.Add(name);
}

const string assetUrl = "https://github.com/tiaprendizabapa-collab/CottonBrowser/releases/download/v1.0.1/CottonBrowser-win-x64.zip";
const int updaterSize = 68_000_000;
var package = BuildPackage(updaterSize);
var digest = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant();
var releaseJson = JsonSerializer.Serialize(new
{
    tag_name = "v1.0.1",
    body = "## Novidades\n\n- Tela Sobre e atualizações.",
    assets = new[]
    {
        new { name = "CottonBrowser-win-x64.zip", browser_download_url = assetUrl,
            digest = "sha256:" + digest, size = package.LongLength }
    }
});
var stagingRoot = Path.Combine(Path.GetTempPath(), "cotton-update-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(stagingRoot);
try
{
    var preferencePath = Path.Combine(stagingRoot, "update-notice.txt");
    var preferences = new UpdateNoticePreferenceStore(preferencePath);
    Check(!preferences.Suppressed, "update notice starts enabled");
    preferences.SetSuppressed(true);
    Check(new UpdateNoticePreferenceStore(preferencePath).Suppressed,
        "do not show again persists across restarts");
    preferences.SetSuppressed(false);
    Check(!new UpdateNoticePreferenceStore(preferencePath).Suppressed,
        "update notice can be enabled again");

    using var client = new HttpClient(new FakeHandler(request =>
        request.RequestUri?.Host == "api.github.com"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(releaseJson, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) }));
    using var service = new BrowserUpdateService(client, stagingRoot);
    var update = await service.CheckAsync(new Version(1, 0, 0, 0), CancellationToken.None);
    Check(update is not null && update.Version == new Version(1, 0, 1, 0), "new release found");
    Check(await service.CheckAsync(new Version(1, 0, 1, 0), CancellationToken.None) is null,
        "installed version is current");

    Check(service.LatestRelease?.Notes.Contains("Sobre e atualizações") == true,
        "release notes remain available when the installed version is current");
    var historyPath = Path.Combine(stagingRoot, "update-check.json");
    var historyStore = new UpdateCheckHistoryStore(historyPath);
    Check(historyStore.Load() is null, "first launch has no previous check");
    var checkedAt = DateTimeOffset.UtcNow;
    Check(historyStore.Save(new UpdateCheckHistory(checkedAt, true, service.LatestRelease)),
        "successful check history can be saved");
    var restored = new UpdateCheckHistoryStore(historyPath).Load();
    Check(restored?.CheckedAt == checkedAt && restored.Succeeded && restored.Release?.Version == new Version(1, 0, 1, 0),
        "check time and release survive restarting the browser");
    Check(restored?.Release?.Notes == service.LatestRelease?.Notes, "cached notes survive restarting the browser");
    historyStore.Save(new UpdateCheckHistory(checkedAt.AddMinutes(1), false, restored?.Release));
    Check(historyStore.Load() is { Succeeded: false, Release: not null }, "failed check retains previous notes");
    File.WriteAllText(historyPath, "{invalid");
    Check(historyStore.Load() is null, "malformed history does not prevent opening settings");
    File.WriteAllText(historyPath, "{\"CheckedAt\":\"2026-10-05T12:00:00Z\",\"Succeeded\":true,\"Release\":{\"Version\":null,\"Tag\":\"v1.0.1\",\"Notes\":null}}");
    Check(historyStore.Load() is null, "invalid cached release is ignored");
    if (update is not null)
    {
        var staged = await service.DownloadAsync(update, null, CancellationToken.None);
        Check(File.Exists(staged.ArchivePath) &&
              new FileInfo(staged.UpdaterPath).Length == updaterSize,
            "self-contained updater the size of the published executable is accepted");
        try
        {
            await service.DownloadAsync(update with { Sha256 = new string('0', 64) }, null,
                CancellationToken.None);
            Check(false, "tampered package rejected");
        }
        catch (InvalidDataException) { Check(true, "tampered package rejected"); }
    }

    await RejectPackage(BuildPackage(4096, includeBrowser: false), "package without browser rejected before restart");
    await RejectPackage(BuildPackage(4096, includeUpdater: false), "package without updater rejected before restart");
    await RejectPackage(BuildPackage(0), "empty updater rejected");
    await RejectPackage(BuildPackage(4096, validExecutable: false), "invalid executable header rejected before restart");
    await RejectPackage(BuildPackage(4096, includeBridge: false), "package without internal pages rejected");
    await RejectPackage(BuildPackage(4096, extraFiles: ["cottonupdater.exe"]), "case-insensitive duplicate rejected");
    await RejectPackage(BuildPackage(4096, extraFiles: ["Assets/Bridge/../outside.html"]), "package traversal rejected");
    await RejectPackage(BuildPackage(4096, extraFiles: ["Assets/Bridge/NUL.txt"]), "Windows device path rejected");
    await RejectPackage(BuildPackage(4096, extraFiles: ["unexpected.dll"]), "unexpected package file rejected");
    await RejectPackage(BuildPackage(4096, extraFiles: ["Assets/Bridge/page", "Assets/Bridge/page/index.html"]),
        "file-directory conflict rejected");
    await RejectPackage(BuildPackage(4096, extraFiles: ["../outside/"]), "unsafe directory entry rejected");
    var understatedPackage = BuildPackage(4096);
    for (var index = 0; index <= understatedPackage.Length - 46; index++)
    {
        if (BitConverter.ToUInt32(understatedPackage, index) != 0x02014b50) continue;
        if (Encoding.UTF8.GetString(understatedPackage, index + 46,
                BitConverter.ToUInt16(understatedPackage, index + 28)) != "CottonUpdater.exe") continue;
        BitConverter.GetBytes(1u).CopyTo(understatedPackage, index + 24);
        break;
    }
    await RejectPackage(understatedPackage, "updater with truncated declared executable size rejected");

    async Task RejectPackage(byte[] bytes, string description)
    {
        using var invalidClient = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        }));
        using var invalidService = new BrowserUpdateService(invalidClient, stagingRoot);
        var invalidUpdate = new BrowserUpdate(new Version(1, 0, 1, 0), "v1.0.1", new Uri(assetUrl),
            Convert.ToHexString(SHA256.HashData(bytes)), bytes.LongLength);
        var stagedBefore = Directory.EnumerateDirectories(stagingRoot).Count();
        try
        {
            await invalidService.DownloadAsync(invalidUpdate, null, CancellationToken.None);
            Check(false, description);
        }
        catch (InvalidDataException) { Check(true, description); }
        Check(Directory.EnumerateDirectories(stagingRoot).Count() == stagedBefore,
            description + " clears failed staging");
    }

    using var unavailableClient = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
    using var unavailableService = new BrowserUpdateService(unavailableClient, stagingRoot);
    try
    {
        await unavailableService.CheckAsync(new Version(1, 0, 0, 0), CancellationToken.None);
        Check(false, "missing public release is explained");
    }
    catch (InvalidDataException ex)
    {
        Check(ex.Message.Contains("Release pública", StringComparison.Ordinal),
            "missing public release is explained");
    }

    using var unsafeClient = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(releaseJson.Replace("https://github.com/", "https://example.com/"),
            Encoding.UTF8, "application/json")
    }));
    using var unsafeService = new BrowserUpdateService(unsafeClient, stagingRoot);
    try
    {
        await unsafeService.CheckAsync(new Version(1, 0, 0, 0), CancellationToken.None);
        Check(false, "untrusted asset URL rejected");
    }
    catch (InvalidDataException) { Check(true, "untrusted asset URL rejected"); }

    if (args.Length > 0)
    {
        if (args.Length != 2 || args[0] != "--package" || !File.Exists(args[1]))
            throw new ArgumentException("Uso: BrowserUpdateChecks [--package caminho-do-zip]");

        var publishedPackage = Path.GetFullPath(args[1]);
        long updaterLength;
        using (var archive = ZipFile.OpenRead(publishedPackage))
            updaterLength = archive.GetEntry("CottonUpdater.exe")?.Length ?? 0;
        string publishedHash;
        await using (var input = File.OpenRead(publishedPackage))
            publishedHash = Convert.ToHexString(await SHA256.HashDataAsync(input)).ToLowerInvariant();

        using var packageClient = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(File.OpenRead(publishedPackage))
        }));
        using var packageService = new BrowserUpdateService(packageClient, stagingRoot);
        var publishedUpdate = new BrowserUpdate(new Version(1, 0, 1, 0), "v1.0.1",
            new Uri(assetUrl), publishedHash, new FileInfo(publishedPackage).Length);
        var publishedStaging = await packageService.DownloadAsync(publishedUpdate, null, CancellationToken.None);
        Check(updaterLength > 0 && new FileInfo(publishedStaging.UpdaterPath).Length == updaterLength,
            "published ZIP can be staged by the browser updater");
    }
}
finally
{
    var fullRoot = Path.GetFullPath(stagingRoot);
    var fullTemp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) +
        Path.DirectorySeparatorChar;
    if (fullRoot.StartsWith(fullTemp, StringComparison.OrdinalIgnoreCase) &&
        Directory.Exists(fullRoot))
        Directory.Delete(fullRoot, recursive: true);
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Falhas: " + string.Join(", ", failures));
    return 1;
}
Console.WriteLine($"{checkCount} verificações do atualizador passaram.");
return 0;

static byte[] BuildPackage(int updaterSize, bool includeBrowser = true, bool includeUpdater = true,
    bool includeBridge = true, bool validExecutable = true, params string[] extraFiles)
{
    using var buffer = new MemoryStream();
    using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
        if (includeBrowser)
        {
            using var browser = archive.CreateEntry("CottonBrowser.exe").Open();
            var executable = new byte[4096];
            if (validExecutable) WriteExecutableHeader(executable);
            browser.Write(executable);
        }
        if (includeUpdater)
        {
            var entry = archive.CreateEntry("CottonUpdater.exe", CompressionLevel.Fastest);
            using var output = entry.Open();
            var block = new byte[64 * 1024];
            if (validExecutable) WriteExecutableHeader(block);
            for (var remaining = updaterSize; remaining > 0;)
            {
                var count = Math.Min(remaining, block.Length);
                output.Write(block, 0, count);
                remaining -= count;
                Array.Clear(block);
            }
        }
        if (includeBridge)
        {
            foreach (var page in new[] { "index.html", "newtab.html", "private.html" })
            {
                using var output = archive.CreateEntry("Assets/Bridge/" + page).Open();
                output.WriteByte(1);
            }
        }
        foreach (var name in extraFiles)
        {
            var entry = archive.CreateEntry(name);
            if (name.EndsWith('/')) continue;
            using var output = entry.Open();
            output.WriteByte(1);
        }
    }
    return buffer.ToArray();
}

static void WriteExecutableHeader(byte[] bytes)
{
    bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
    BitConverter.GetBytes(128).CopyTo(bytes, 60);
    bytes[128] = (byte)'P'; bytes[129] = (byte)'E';
}

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken) => Task.FromResult(send(request));
}
