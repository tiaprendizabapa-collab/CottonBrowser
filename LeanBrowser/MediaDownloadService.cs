using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

[assembly: InternalsVisibleTo("MediaDownloadChecks")]

namespace LeanBrowser;

internal enum MediaDownloadKind
{
    Video,
    Audio
}

/// <param name="Percent">Progress from 0 to 100, or null while preparing tools or when the size is unknown.</param>
/// <param name="BytesReceived">Bytes downloaded, or -1 when unknown.</param>
/// <param name="TotalBytes">Expected bytes, or -1 when unknown.</param>
internal sealed record MediaDownloadProgress(string Message, double? Percent, long BytesReceived, long TotalBytes);

internal sealed record MediaDownloadResult(string FilePath, long FileSize);

/// <summary>
/// Downloads one user-selected page with fixed, verified copies of yt-dlp and its helpers.
/// Tools are installed only when first needed; each media job uses a separate temporary directory.
/// </summary>
internal sealed class MediaDownloadService : IDisposable
{
    private const string YtDlpUrl =
        "https://github.com/yt-dlp/yt-dlp/releases/download/2026.08.19/yt-dlp.exe";
    private const string YtDlpSha256 =
        "66674953fe251b89f4d08c5f0e35e0728679bd67ab3d7d05c0562af101dd3e7a";
    private const long YtDlpLength = 17_840_399;

    private const string DenoUrl =
        "https://github.com/denoland/deno/releases/download/v2.9.7/deno-x86_64-pc-windows-msvc.zip";
    private const string DenoSha256 =
        "a0c3101b4158d1dfb7d6a78a7bf0f3de80c96bb423c152beec8beb22786f2238";
    private const long DenoArchiveLength = 42_630_221;

    private const string FfmpegUrl =
        "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/autobuild-2026-09-30-00-16/ffmpeg-N-127014-g18ee27e67b-win64-gpl.zip";
    private const string FfmpegSha256 =
        "3d0e8cbf23de7d5702fd1b2125d944bf46f786943810b62e682f6f798ba2f0ed";
    private const long FfmpegArchiveLength = 198_585_847;

    private const string ProgressPrefix = "__COTTON_PROGRESS__:";
    private const string FilePrefix = "__COTTON_FILE__";
    private const string InstallMarkerName = "verified-sha256.txt";
    private static readonly SemaphoreSlim InstallGate = new(1, 1);

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _toolsRoot;
    private readonly string _downloadsDirectory;

    private string YtDlpDirectory => Path.Combine(_toolsRoot, "yt-dlp-2026.08.19");
    private string DenoDirectory => Path.Combine(_toolsRoot, "deno-v2.9.7");
    private string FfmpegDirectory => Path.Combine(_toolsRoot, "ffmpeg-2026-09-30");
    private string YtDlpPath => Path.Combine(YtDlpDirectory, "yt-dlp.exe");
    private string DenoPath => Path.Combine(DenoDirectory, "deno.exe");

    internal MediaDownloadService(
        HttpClient? httpClient = null,
        string? toolsRoot = null,
        string? downloadsDirectory = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _ownsHttp = httpClient is null;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CottonBrowser-Media/1.0");
        _toolsRoot = toolsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LeanBrowser", "MediaTools");
        _downloadsDirectory = downloadsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    /// <summary>Fast check for deciding whether the first-use setup notice is needed.</summary>
    internal bool ToolsReady(MediaDownloadKind kind)
    {
        if (kind is not (MediaDownloadKind.Video or MediaDownloadKind.Audio))
            throw new ArgumentOutOfRangeException(nameof(kind));

        return HasInstalledFile(YtDlpDirectory, "yt-dlp.exe", YtDlpSha256)
            && HasInstalledFile(DenoDirectory, "deno.exe", DenoSha256)
            && HasInstalledFile(FfmpegDirectory, "ffmpeg.exe", FfmpegSha256)
            && HasInstalledFile(FfmpegDirectory, "ffprobe.exe", FfmpegSha256);
    }

    internal static bool IsSupportedPageUrl(string? pageUrl)
    {
        if (string.IsNullOrWhiteSpace(pageUrl)
            || pageUrl.Any(char.IsControl)
            || !Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host)
            || uri.UserInfo.Length != 0
            || uri.Host.Equals("cottonbrowser.test", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".cottonbrowser.test", StringComparison.OrdinalIgnoreCase))
            return false;

        if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.Trim('/').Length > 0;

        if (uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            var path = uri.AbsolutePath.Trim('/');
            if (path.Equals("watch", StringComparison.OrdinalIgnoreCase))
                return uri.Query.TrimStart('?').Split('&').Any(parameter =>
                    parameter.StartsWith("v=", StringComparison.OrdinalIgnoreCase)
                    && parameter.Length > 2);
            return path.StartsWith("shorts/", StringComparison.OrdinalIgnoreCase)
                && path.Length > "shorts/".Length
                || path.StartsWith("live/", StringComparison.OrdinalIgnoreCase)
                && path.Length > "live/".Length;
        }

        return true;
    }

    internal async Task<MediaDownloadResult> DownloadAsync(
        string pageUrl,
        MediaDownloadKind kind,
        IProgress<MediaDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!IsSupportedPageUrl(pageUrl))
            throw new ArgumentException("A página precisa ter um endereço HTTP ou HTTPS válido.", nameof(pageUrl));
        if (kind is not (MediaDownloadKind.Video or MediaDownloadKind.Audio))
            throw new ArgumentOutOfRangeException(nameof(kind));

        cancellationToken.ThrowIfCancellationRequested();
        await EnsureToolsAsync(kind, progress, cancellationToken);

        Directory.CreateDirectory(_downloadsDirectory);
        var temporaryRoot = Path.Combine(_downloadsDirectory, ".cotton-media-tmp");
        var jobDirectory = Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(jobDirectory);

        try
        {
            progress?.Report(new MediaDownloadProgress(
                kind == MediaDownloadKind.Audio ? "Baixando áudio…" : "Baixando vídeo…",
                null, -1, -1));

            var args = BuildDownloadArguments(pageUrl, kind, jobDirectory,
                DenoPath, FfmpegDirectory);
            var sourcePath = await RunYtDlpAsync(args, pageUrl, kind, jobDirectory,
                progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var finalPath = MoveToDownloads(sourcePath);
            var size = new FileInfo(finalPath).Length;
            progress?.Report(new MediaDownloadProgress("Download concluído.", 100, size, size));
            return new MediaDownloadResult(finalPath, size);
        }
        finally
        {
            TryDeleteDirectory(jobDirectory);
            TryDeleteEmptyDirectory(temporaryRoot);
        }
    }

    /// <summary>Pure command builder, also used by checks to guard process isolation.</summary>
    internal static IReadOnlyList<string> BuildDownloadArguments(
        string pageUrl,
        MediaDownloadKind kind,
        string outputDirectory,
        string denoPath,
        string? ffmpegDirectory)
    {
        if (!IsSupportedPageUrl(pageUrl))
            throw new ArgumentException("A página precisa ter um endereço HTTP ou HTTPS válido.", nameof(pageUrl));
        if (kind is not (MediaDownloadKind.Video or MediaDownloadKind.Audio))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Path.IsPathFullyQualified(outputDirectory))
            throw new ArgumentException("A pasta de saída precisa ser absoluta.", nameof(outputDirectory));
        if (!Path.IsPathFullyQualified(denoPath))
            throw new ArgumentException("O caminho do Deno precisa ser absoluto.", nameof(denoPath));
        if (string.IsNullOrWhiteSpace(ffmpegDirectory)
            || !Path.IsPathFullyQualified(ffmpegDirectory))
            throw new ArgumentException("A mídia precisa de um caminho absoluto para FFmpeg.", nameof(ffmpegDirectory));

        var args = new List<string>
        {
            "--ignore-config",
            "--no-plugin-dirs",
            "--no-js-runtimes",
            "--js-runtimes", "deno:" + denoPath,
            "--no-playlist",
            "--playlist-items", "1",
            "--no-overwrites",
            "--no-continue",
            "--windows-filenames",
            "--trim-filenames", "160",
            "--no-mtime",
            "--no-cache-dir",
            "--encoding", "utf-8",
            "--no-simulate",
            "--newline",
            "--color", "no_color",
            "--progress",
            "--progress-template",
            "download:" + ProgressPrefix
                + "%(progress.downloaded_bytes)s:%(progress.total_bytes)s:%(progress.total_bytes_estimate)s",
            "--print", "after_move:" + FilePrefix + "%(filepath)s",
            "-P", outputDirectory,
            "-o", "%(title)s [%(id)s].%(ext)s",
            "-f", kind == MediaDownloadKind.Audio ? "bestaudio" : "bestvideo+bestaudio/best"
        };

        args.AddRange(["--ffmpeg-location", ffmpegDirectory!]);

        args.Add("--");
        args.Add(pageUrl);
        return args;
    }

    private async Task EnsureToolsAsync(
        MediaDownloadKind kind,
        IProgress<MediaDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        await InstallGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_toolsRoot);
            await using var crossProcessLock = await AcquireInstallLockAsync(cancellationToken);

            if (!await IsInstallationVerifiedAsync(
                    YtDlpDirectory, YtDlpSha256, ["yt-dlp.exe"], cancellationToken))
            {
                const string setupMessage = "Preparando yt-dlp (18 MB)…";
                ReportSetup(progress, setupMessage);
                await InstallSingleExecutableAsync(
                    YtDlpUrl, YtDlpSha256, YtDlpLength,
                    YtDlpDirectory, "yt-dlp.exe", zipped: false,
                    progress, setupMessage, cancellationToken);
            }

            if (!await IsInstallationVerifiedAsync(
                    DenoDirectory, DenoSha256, ["deno.exe"], cancellationToken))
            {
                const string setupMessage = "Preparando Deno para YouTube (43 MB)…";
                ReportSetup(progress, setupMessage);
                await InstallSingleExecutableAsync(
                    DenoUrl, DenoSha256, DenoArchiveLength,
                    DenoDirectory, "deno.exe", zipped: true,
                    progress, setupMessage, cancellationToken);
            }

            if (!await IsInstallationVerifiedAsync(FfmpegDirectory, FfmpegSha256,
                    ["ffmpeg.exe", "ffprobe.exe"], cancellationToken))
            {
                const string setupMessage = "Preparando FFmpeg para mídia (199 MB)…";
                ReportSetup(progress, setupMessage);
                await InstallFfmpegAsync(progress, setupMessage, cancellationToken);
            }

            if (!ToolsReady(kind))
                throw new InvalidDataException("A preparação das ferramentas de mídia ficou incompleta.");
        }
        finally
        {
            InstallGate.Release();
        }
    }

    private async Task InstallSingleExecutableAsync(
        string url,
        string expectedSha256,
        long expectedLength,
        string destinationDirectory,
        string executableName,
        bool zipped,
        IProgress<MediaDownloadProgress>? progress,
        string setupMessage,
        CancellationToken cancellationToken)
    {
        var staging = CreateStagingDirectory();
        try
        {
            var asset = Path.Combine(staging, zipped ? "asset.zip" : executableName);
            await DownloadVerifiedAssetAsync(url, expectedSha256, expectedLength,
                asset, progress, setupMessage, cancellationToken);

            var install = Path.Combine(staging, "install");
            Directory.CreateDirectory(install);
            var destination = Path.Combine(install, executableName);
            if (zipped)
            {
                using var archive = ZipFile.OpenRead(asset);
                var entry = archive.Entries.SingleOrDefault(item =>
                    item.Name.Equals(executableName, StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                    throw new InvalidDataException("O arquivo oficial não contém a ferramenta esperada.");
                await ExtractEntryAsync(entry, destination, 250L * 1024 * 1024, cancellationToken);
            }
            else
            {
                File.Move(asset, destination);
            }

            await WriteExecutableDigestAsync(destination, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(install, InstallMarkerName),
                expectedSha256, cancellationToken);
            PublishInstallation(install, destinationDirectory);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private async Task InstallFfmpegAsync(
        IProgress<MediaDownloadProgress>? progress,
        string setupMessage,
        CancellationToken cancellationToken)
    {
        var staging = CreateStagingDirectory();
        try
        {
            var asset = Path.Combine(staging, "asset.zip");
            await DownloadVerifiedAssetAsync(FfmpegUrl, FfmpegSha256,
                FfmpegArchiveLength, asset, progress, setupMessage, cancellationToken);

            var install = Path.Combine(staging, "install");
            Directory.CreateDirectory(install);
            using (var archive = ZipFile.OpenRead(asset))
            {
                foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" })
                {
                    var suffix = "/bin/" + name;
                    var entry = archive.Entries.SingleOrDefault(item =>
                        item.FullName.Replace('\\', '/').EndsWith(
                            suffix, StringComparison.OrdinalIgnoreCase));
                    if (entry is null)
                        throw new InvalidDataException("O pacote oficial de FFmpeg está incompleto.");
                    await ExtractEntryAsync(entry, Path.Combine(install, name),
                        400L * 1024 * 1024, cancellationToken);
                    await WriteExecutableDigestAsync(Path.Combine(install, name), cancellationToken);
                }
            }

            await File.WriteAllTextAsync(Path.Combine(install, InstallMarkerName),
                FfmpegSha256, cancellationToken);
            PublishInstallation(install, FfmpegDirectory);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private async Task DownloadVerifiedAssetAsync(
        string url,
        string expectedSha256,
        long expectedLength,
        string destination,
        IProgress<MediaDownloadProgress>? progress,
        string setupMessage,
        CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps
            || response.Content.Headers.ContentLength is long announced
               && announced != expectedLength)
            throw new InvalidDataException("O pacote de mídia não corresponde à versão esperada.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 65_536, useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65_536];
        long total = 0;
        var lastReportedPercent = -1;
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            total += count;
            if (total > expectedLength)
                throw new InvalidDataException("O pacote de mídia excede o tamanho esperado.");
            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            var percent = (int)(total * 100 / expectedLength);
            if (percent != lastReportedPercent)
            {
                lastReportedPercent = percent;
                progress?.Report(new MediaDownloadProgress(setupMessage, percent, -1, -1));
            }
        }

        if (total != expectedLength
            || !Convert.ToHexString(hash.GetHashAndReset()).Equals(
                expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A verificação SHA-256 da ferramenta de mídia falhou.");
    }

    private static async Task ExtractEntryAsync(
        ZipArchiveEntry entry,
        string destination,
        long maximumLength,
        CancellationToken cancellationToken)
    {
        if (entry.Length <= 0 || entry.Length > maximumLength)
            throw new InvalidDataException("O executável no pacote tem um tamanho inválido.");

        await using var input = entry.Open();
        await using var output = new FileStream(destination, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 65_536, useAsync: true);
        var buffer = new byte[65_536];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            total += count;
            if (total > maximumLength)
                throw new InvalidDataException("O executável no pacote excede o tamanho permitido.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        if (total != entry.Length)
            throw new InvalidDataException("O executável no pacote está incompleto.");
    }

    private async Task<string> RunYtDlpAsync(
        IReadOnlyList<string> args,
        string pageUrl,
        MediaDownloadKind kind,
        string jobDirectory,
        IProgress<MediaDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(YtDlpPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = jobDirectory
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Não foi possível iniciar a ferramenta de mídia.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("Não foi possível iniciar a ferramenta de mídia.", ex);
        }

        string? reportedPath = null;
        string? lastError = null;
        using var cancellation = cancellationToken.Register(() => TryKill(process));
        var stdout = ReadOutputAsync(process.StandardOutput);
        var stderr = ReadOutputAsync(process.StandardError);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdout, stderr);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(lastError is null
                ? "Não foi possível baixar a mídia desta página."
                : "Não foi possível baixar a mídia: " + lastError);

        if (reportedPath is null)
            throw new InvalidDataException("A ferramenta de mídia não informou o arquivo concluído.");

        var fullPath = Path.GetFullPath(reportedPath);
        if (!string.Equals(Path.GetDirectoryName(fullPath),
                Path.GetFullPath(jobDirectory).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)
            || !File.Exists(fullPath))
            throw new InvalidDataException("A ferramenta de mídia informou um caminho de saída inválido.");

        return fullPath;

        async Task ReadOutputAsync(StreamReader reader)
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                if (line.StartsWith(FilePrefix, StringComparison.Ordinal))
                {
                    reportedPath = line[FilePrefix.Length..].Trim();
                }
                else if (line.StartsWith(ProgressPrefix, StringComparison.Ordinal))
                {
                    var fields = line[ProgressPrefix.Length..].Split(':');
                    if (fields.Length < 3) continue;
                    var received = ParseBytes(fields[0]);
                    var total = ParseBytes(fields[1]);
                    if (total <= 0) total = ParseBytes(fields[2]);
                    double? percent = total > 0 && received >= 0
                        ? Math.Clamp(received * 100d / total, 0, 100)
                        : null;
                    progress?.Report(new MediaDownloadProgress(
                        kind == MediaDownloadKind.Audio ? "Baixando áudio…" : "Baixando vídeo…",
                        percent, received, total));
                }
                else if (line.Contains("[Merger]", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(new MediaDownloadProgress(
                        "Mesclando vídeo e áudio…", null, -1, -1));
                }
                else if (line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                {
                    lastError = line[6..].Trim().Replace(pageUrl, "[URL]",
                        StringComparison.OrdinalIgnoreCase);
                    if (lastError.Length > 500) lastError = lastError[..500];
                }
            }
        }
    }

    private string MoveToDownloads(string sourcePath)
    {
        var name = Path.GetFileName(sourcePath);
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException("O nome do arquivo de mídia é inválido.");

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var candidate = attempt == 0 ? name : $"{stem} ({attempt + 1}){extension}";
            var destination = Path.Combine(_downloadsDirectory, candidate);
            try
            {
                File.Move(sourcePath, destination, overwrite: false);
                return destination;
            }
            catch (IOException) when (File.Exists(destination))
            {
                // Another download already took this name; try another suffix.
            }
        }

        var unique = Path.Combine(_downloadsDirectory,
            $"{stem} [{Guid.NewGuid():N}]{extension}");
        File.Move(sourcePath, unique, overwrite: false);
        return unique;
    }

    private string CreateStagingDirectory()
    {
        var path = Path.Combine(_toolsRoot, ".staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void PublishInstallation(string stagingDirectory, string destination)
    {
        string? invalidDirectory = null;
        if (Directory.Exists(destination))
        {
            // Only this fixed version directory is moved. The verified replacement
            // is already complete before the existing installation is touched.
            invalidDirectory = destination + ".invalid-" + Guid.NewGuid().ToString("N");
            Directory.Move(destination, invalidDirectory);
        }

        try
        {
            Directory.Move(stagingDirectory, destination);
        }
        catch
        {
            if (invalidDirectory is not null && !Directory.Exists(destination))
                Directory.Move(invalidDirectory, destination);
            throw;
        }

        if (invalidDirectory is not null) TryDeleteDirectory(invalidDirectory);
    }

    private static bool HasInstalledFile(string directory, string name, string sha256)
    {
        var executable = Path.Combine(directory, name);
        var marker = Path.Combine(directory, InstallMarkerName);
        var executableDigest = executable + ".sha256";
        try
        {
            return File.Exists(executable)
                && new FileInfo(executable).Length > 0
                && File.Exists(marker)
                && File.Exists(executableDigest)
                && IsSha256(File.ReadAllText(executableDigest).Trim())
                && File.ReadAllText(marker).Trim().Equals(sha256,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static async Task<bool> IsInstallationVerifiedAsync(
        string directory,
        string sourceSha256,
        IReadOnlyList<string> executableNames,
        CancellationToken cancellationToken)
    {
        foreach (var name in executableNames)
        {
            if (!HasInstalledFile(directory, name, sourceSha256)) return false;
            var path = Path.Combine(directory, name);
            var expected = File.ReadAllText(path + ".sha256").Trim();
            if (name.Equals("yt-dlp.exe", StringComparison.OrdinalIgnoreCase)
                && !expected.Equals(YtDlpSha256, StringComparison.OrdinalIgnoreCase))
                return false;
            await using var file = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static async Task WriteExecutableDigestAsync(
        string path, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(path);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
        await File.WriteAllTextAsync(path + ".sha256", digest, cancellationToken);
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private async Task<FileStream> AcquireInstallLockAsync(CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(_toolsRoot, ".install.lock");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
            {
                await Task.Delay(250, cancellationToken);
            }
        }
    }

    private static long ParseBytes(string text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && value >= 0 ? value : -1;

    private static void ReportSetup(IProgress<MediaDownloadProgress>? progress, string message) =>
        progress?.Report(new MediaDownloadProgress(message, null, -1, -1));

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
