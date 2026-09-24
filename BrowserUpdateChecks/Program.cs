using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LeanBrowser;

var failures = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) failures.Add(name);
}

const string assetUrl = "https://github.com/tiaprendizabapa-collab/CottonBrowser/releases/download/v1.0.1/CottonBrowser-win-x64.zip";
var package = BuildPackage(21_000_001);
var digest = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant();
var releaseJson = JsonSerializer.Serialize(new
{
    tag_name = "v1.0.1",
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
    using var client = new HttpClient(new FakeHandler(request =>
        request.RequestUri?.Host == "api.github.com"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(releaseJson, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) }));
    using var service = new BrowserUpdateService(client, stagingRoot);
    var update = await service.CheckAsync(new Version(1, 0, 0, 0), CancellationToken.None);
    Check(update is not null && update.Version == new Version(1, 0, 1, 0), "new release found");
    Check(await service.CheckAsync(new Version(1, 0, 1, 0), CancellationToken.None) is null,
        "installed version is current");

    if (update is not null)
    {
        var staged = await service.DownloadAsync(update, null, CancellationToken.None);
        Check(File.Exists(staged.ArchivePath) &&
              new FileInfo(staged.UpdaterPath).Length == 21_000_001,
            "self-contained updater larger than 20 MB is accepted");
        try
        {
            await service.DownloadAsync(update with { Sha256 = new string('0', 64) }, null,
                CancellationToken.None);
            Check(false, "tampered package rejected");
        }
        catch (InvalidDataException) { Check(true, "tampered package rejected"); }
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
Console.WriteLine("6 verificações do atualizador passaram.");
return 0;

static byte[] BuildPackage(int updaterSize)
{
    using var buffer = new MemoryStream();
    using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
        var entry = archive.CreateEntry("CottonUpdater.exe", CompressionLevel.Fastest);
        using var output = entry.Open();
        var block = new byte[64 * 1024];
        for (var remaining = updaterSize; remaining > 0;)
        {
            var count = Math.Min(remaining, block.Length);
            output.Write(block, 0, count);
            remaining -= count;
        }
    }
    return buffer.ToArray();
}

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken) => Task.FromResult(send(request));
}
