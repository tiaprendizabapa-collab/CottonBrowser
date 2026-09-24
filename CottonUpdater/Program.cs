using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace CottonUpdater;

internal static class Program
{
    private const long MaximumExpandedBytes = 250L * 1024 * 1024;

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            if (args.Length != 4 || !int.TryParse(args[0], out var parentPid) ||
                args[3].Length != 64 || !args[3].All(Uri.IsHexDigit))
                throw new InvalidDataException("Os parâmetros da atualização são inválidos.");

            if (!Path.IsPathFullyQualified(args[1]) || !Path.IsPathFullyQualified(args[2]))
                throw new InvalidDataException("Os caminhos da atualização são inválidos.");
            var archivePath = Path.GetFullPath(args[1]);
            var target = Path.GetFullPath(args[2]);
            if (target == Path.GetPathRoot(target) ||
                !File.Exists(Path.Combine(target, "CottonBrowser.exe")))
                throw new InvalidDataException("A instalação do CottonBrowser não foi encontrada.");

            try
            {
                using var parent = Process.GetProcessById(parentPid);
                if (!parent.WaitForExit(120_000))
                    throw new IOException("Feche o CottonBrowser antes de concluir a atualização.");
            }
            catch (ArgumentException) { /* O navegador já saiu. */ }

            using (var stream = File.OpenRead(archivePath))
            {
                var hash = Convert.ToHexString(SHA256.HashData(stream));
                if (!hash.Equals(args[3], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("A integridade do pacote de atualização falhou.");
            }

            var staging = Path.GetDirectoryName(archivePath)!;
            var extracted = Path.Combine(staging, "extracted");
            var backup = Path.Combine(staging, "backup");
            Directory.CreateDirectory(extracted);

            var files = ExtractPackage(archivePath, extracted);
            if (!files.Contains("CottonBrowser.exe", StringComparer.OrdinalIgnoreCase) ||
                !files.Contains("CottonUpdater.exe", StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("O pacote não contém todos os executáveis necessários.");

            var existing = new List<string>();
            var created = new List<string>();
            try
            {
                // Salva todos os arquivos antes de substituir o primeiro.
                foreach (var relative in files)
                {
                    var destination = Path.Combine(target, relative);
                    if (!File.Exists(destination)) { created.Add(destination); continue; }
                    var saved = Path.Combine(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Copy(destination, saved);
                    existing.Add(relative);
                }

                foreach (var relative in files)
                {
                    var destination = Path.Combine(target, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(Path.Combine(extracted, relative), destination, overwrite: true);
                }
            }
            catch
            {
                foreach (var relative in existing)
                {
                    try { File.Copy(Path.Combine(backup, relative), Path.Combine(target, relative), true); }
                    catch { /* preserva o backup no staging para recuperação */ }
                }
                foreach (var path in created)
                {
                    try { if (File.Exists(path)) File.Delete(path); } catch { }
                }
                throw;
            }

            RefreshInstalledVersion(target);

            try
            {
                Process.Start(new ProcessStartInfo(Path.Combine(target, "CottonBrowser.exe"))
                {
                    WorkingDirectory = target,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Atualização instalada, mas não foi possível reiniciar o navegador. " +
                    "Abra o CottonBrowser normalmente.\n\n" + ex.Message,
                    "CottonBrowser", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Não foi possível instalar a atualização. Os arquivos anteriores " +
                "foram preservados quando possível.\n\n" + ex.Message,
                "CottonBrowser", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void RefreshInstalledVersion(string target)
    {
        var installed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "CottonBrowser");
        if (!Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(installed).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CottonBrowser", writable: true);
            if (key?.GetValue("InstallLocation") is not string location ||
                !Path.GetFullPath(location).TrimEnd(Path.DirectorySeparatorChar)
                    .Equals(Path.GetFullPath(installed).TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase)) return;
            var version = FileVersionInfo.GetVersionInfo(Path.Combine(target, "CottonBrowser.exe"));
            if (!string.IsNullOrWhiteSpace(version.FileVersion)) key.SetValue("DisplayVersion", version.FileVersion);
        }
        catch { /* Uma falha no registro não invalida a atualização instalada. */ }
    }

    private static List<string> ExtractPackage(string archivePath, string destination)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > 256)
            throw new InvalidDataException("O pacote contém arquivos demais.");

        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long declared = 0;
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            var relative = ValidateEntryName(entry.FullName);
            if (!seen.Add(relative))
                throw new InvalidDataException("O pacote contém arquivos duplicados.");
            declared += entry.Length;
            if (declared > MaximumExpandedBytes)
                throw new InvalidDataException("O pacote excede o tamanho permitido.");

            var output = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var input = entry.Open();
            using var file = new FileStream(output, FileMode.CreateNew);
            var buffer = new byte[65536];
            int count;
            while ((count = input.Read(buffer)) != 0)
            {
                expanded += count;
                if (expanded > MaximumExpandedBytes)
                    throw new InvalidDataException("O pacote excede o tamanho permitido.");
                file.Write(buffer, 0, count);
            }
            files.Add(relative);
        }
        return files;
    }

    private static string ValidateEntryName(string raw)
    {
        var name = raw.Replace('\\', '/');
        var parts = name.Split('/');
        if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".." ||
            part.TrimEnd(' ', '.') != part || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("O pacote contém um caminho inválido.");

        var allowed = name.Equals("CottonBrowser.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("CottonUpdater.exe", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Assets/Bridge/", StringComparison.OrdinalIgnoreCase);
        if (!allowed)
            throw new InvalidDataException("O pacote contém um arquivo inesperado.");

        return Path.Combine(parts);
    }
}
