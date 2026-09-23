using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace LeanBrowser;

internal sealed record BrowserUpdate(Version Version, string Tag, Uri DownloadUrl, string Sha256, long Size);
internal sealed record StagedBrowserUpdate(string ArchivePath, string UpdaterPath);

internal sealed class BrowserUpdateService : IDisposable
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/tiaprendizabapa-collab/CottonBrowser/releases/latest";
    private const string AssetName = "CottonBrowser-win-x64.zip";
    private const long MaximumPackageBytes = 250L * 1024 * 1024;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public BrowserUpdateService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CottonBrowser-Updater/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<BrowserUpdate?> CheckAsync(Version installedVersion, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        var release = json.RootElement;
        var tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var parsedVersion))
            throw new InvalidDataException("A versão publicada no GitHub é inválida.");

        var version = new Version(parsedVersion.Major, parsedVersion.Minor,
            Math.Max(0, parsedVersion.Build), Math.Max(0, parsedVersion.Revision));
        if (version <= installedVersion) return null;

        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != AssetName) continue;
            var url = new Uri(asset.GetProperty("browser_download_url").GetString()!);
            if (url.Scheme != Uri.UriSchemeHttps ||
                !url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                !url.AbsolutePath.StartsWith(
                    "/tiaprendizabapa-collab/CottonBrowser/releases/download/",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O endereço do pacote de atualização é inválido.");

            var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() : null;
            if (digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
                digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit))
                throw new InvalidDataException("O pacote publicado não contém um resumo SHA-256 verificável.");

            var size = asset.GetProperty("size").GetInt64();
            if (size <= 0 || size > MaximumPackageBytes)
                throw new InvalidDataException("O tamanho do pacote de atualização é inválido.");

            return new BrowserUpdate(version, tag, url, digest[7..].ToLowerInvariant(), size);
        }

        throw new InvalidDataException($"A versão {tag} não contém o pacote {AssetName}.");
    }

    public async Task<StagedBrowserUpdate> DownloadAsync(
        BrowserUpdate update, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var staging = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LeanBrowser", "Updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var archivePath = Path.Combine(staging, AssetName);
        var updaterPath = Path.Combine(staging, "CottonUpdater.exe");

        try
        {
            using var response = await _http.GetAsync(update.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumPackageBytes)
                throw new InvalidDataException("O download excede o tamanho permitido.");

            await using (var destination = new FileStream(archivePath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536, useAsync: true))
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                var buffer = new byte[65536];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    total += count;
                    if (total > MaximumPackageBytes)
                        throw new InvalidDataException("O download excede o tamanho permitido.");
                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    progress?.Report((int)Math.Min(100, total * 100 / update.Size));
                }
                if (total != update.Size)
                    throw new InvalidDataException("O download da atualização está incompleto.");
            }

            await using (var file = File.OpenRead(archivePath))
            {
                var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
                if (!actualHash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("A verificação SHA-256 do pacote falhou.");
            }

            using var archive = ZipFile.OpenRead(archivePath);
            var updater = archive.GetEntry("CottonUpdater.exe");
            if (updater is null || updater.Length is <= 0 or > 20_000_000)
                throw new InvalidDataException("O pacote não contém um atualizador válido.");
            await using (var source = updater.Open())
            await using (var destination = new FileStream(updaterPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536, useAsync: true))
                await source.CopyToAsync(destination, cancellationToken);

            return new StagedBrowserUpdate(archivePath, updaterPath);
        }
        catch
        {
            try { Directory.Delete(staging, recursive: true); } catch { /* arquivo em uso */ }
            throw;
        }
    }

    public void Dispose() => _http.Dispose();
}
