using System.IO.Compression;

namespace CottonBrowser.Shared;

internal sealed record BrowserPackageFile(ZipArchiveEntry Entry, string RelativePath);

internal static class BrowserPackage
{
    internal const long MaximumExpandedBytes = 250L * 1024 * 1024;
    private const string InvalidNameCharacters = "<>:\"/\\|?*";

    internal static List<BrowserPackageFile> Validate(ZipArchive archive)
    {
        if (archive.Entries.Count > 256)
            throw new InvalidDataException("O pacote contém arquivos demais.");

        var files = new List<BrowserPackageFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/'))
            {
                var directory = name.TrimEnd('/');
                ValidateParts(directory);
                if (!(directory.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                    directory.Equals("Assets/Bridge", StringComparison.OrdinalIgnoreCase) ||
                    directory.StartsWith("Assets/Bridge/", StringComparison.OrdinalIgnoreCase)) || entry.Length != 0)
                    throw new InvalidDataException("O pacote contém uma pasta inesperada.");
                continue;
            }
            ValidateParts(name);
            if (!(name.Equals("CottonBrowser.exe", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("CottonUpdater.exe", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Assets/Bridge/", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("O pacote contém um arquivo inesperado.");
            if (!seen.Add(name))
                throw new InvalidDataException("O pacote contém arquivos duplicados.");
            if (entry.Length < 0 || entry.Length > MaximumExpandedBytes - total)
                throw new InvalidDataException("O pacote excede o tamanho permitido.");
            total += entry.Length;
            files.Add(new BrowserPackageFile(entry, Path.Combine(name.Split('/'))));
        }

        foreach (var name in seen)
        {
            for (var slash = name.IndexOf('/'); slash >= 0; slash = name.IndexOf('/', slash + 1))
                if (seen.Contains(name[..slash]))
                    throw new InvalidDataException("O pacote contém caminhos conflitantes.");
        }
        foreach (var required in new[] { "CottonBrowser.exe", "CottonUpdater.exe" })
        {
            var file = files.SingleOrDefault(file => file.RelativePath.Equals(required, StringComparison.OrdinalIgnoreCase));
            if (file is null)
                throw new InvalidDataException("O pacote não contém todos os executáveis necessários.");
            ValidateExecutable(file.Entry);
        }
        foreach (var required in new[] { "Assets/Bridge/index.html", "Assets/Bridge/newtab.html", "Assets/Bridge/private.html" })
            if (!files.Any(file => file.RelativePath.Replace('\\', '/').Equals(required, StringComparison.OrdinalIgnoreCase)
                && file.Entry.Length > 0))
                throw new InvalidDataException("O pacote não contém todas as páginas internas do navegador.");
        return files;
    }

    private static void ValidateExecutable(ZipArchiveEntry entry)
    {
        if (entry.Length < 64)
            throw new InvalidDataException("O pacote contém um executável incompleto.");
        using var input = entry.Open();
        var header = new byte[64];
        input.ReadExactly(header);
        var signatureOffset = BitConverter.ToInt32(header, 60);
        if (header[0] != 'M' || header[1] != 'Z' || signatureOffset < 64 || signatureOffset > entry.Length - 4)
            throw new InvalidDataException("O pacote contém um executável inválido.");
        var buffer = new byte[65536];
        for (var remaining = signatureOffset - header.Length; remaining > 0;)
        {
            var count = input.Read(buffer, 0, Math.Min(remaining, buffer.Length));
            if (count == 0) throw new InvalidDataException("O pacote contém um executável incompleto.");
            remaining -= count;
        }
        input.ReadExactly(header.AsSpan(0, 4));
        if (header[0] != 'P' || header[1] != 'E' || header[2] != 0 || header[3] != 0)
            throw new InvalidDataException("O pacote contém um executável inválido.");
    }

    private static void ValidateParts(string name)
    {
        foreach (var part in name.Split('/'))
        {
            var stem = part.Split('.')[0];
            var reserved = stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (stem.Length == 4 && stem[3] is >= '1' and <= '9' &&
                    (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                     stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)));
            if (string.IsNullOrWhiteSpace(part) || part is "." or ".." ||
                part.TrimEnd(' ', '.') != part || part.Any(c => c < 32 || InvalidNameCharacters.Contains(c)) || reserved)
                throw new InvalidDataException("O pacote contém um caminho inválido.");
        }
    }

    internal static void Extract(BrowserPackageFile file, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var input = file.Entry.Open();
        using var output = new FileStream(destination, FileMode.CreateNew);
        var buffer = new byte[65536];
        long total = 0;
        int count;
        while ((count = input.Read(buffer)) != 0)
        {
            total += count;
            if (total > file.Entry.Length)
                throw new InvalidDataException("O arquivo no pacote excede o tamanho declarado.");
            output.Write(buffer, 0, count);
        }
        if (total != file.Entry.Length)
            throw new InvalidDataException("O arquivo no pacote está incompleto.");
    }

    internal static async Task ExtractAsync(BrowserPackageFile file, string destination,
        CancellationToken cancellationToken)
    {
        await using var input = file.Entry.Open();
        await using var output = new FileStream(destination, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 65536, useAsync: true);
        var buffer = new byte[65536];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            total += count;
            if (total > file.Entry.Length)
                throw new InvalidDataException("O arquivo no pacote excede o tamanho declarado.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        if (total != file.Entry.Length)
            throw new InvalidDataException("O arquivo no pacote está incompleto.");
    }
}
