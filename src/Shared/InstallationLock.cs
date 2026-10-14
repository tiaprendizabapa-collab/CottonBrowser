using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace CottonBrowser.Shared;

/// <summary>Allows profiles to run together, while keeping installation changes exclusive.</summary>
internal sealed class InstallationLock : IDisposable
{
    private readonly FileStream _file;

    private InstallationLock(FileStream file) => _file = file;

    internal static InstallationLock AcquireBrowser(string directory, string? lockDirectory = null) =>
        Acquire(directory, FileShare.ReadWrite, TimeSpan.Zero, lockDirectory);

    internal static InstallationLock AcquireUpdate(string directory, TimeSpan timeout, string? lockDirectory = null) =>
        Acquire(directory, FileShare.None, timeout, lockDirectory);

    private static InstallationLock Acquire(string directory, FileShare sharing, TimeSpan timeout, string? lockDirectory)
    {
        var location = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)).ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(location)));
        lockDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LeanBrowser", "InstallationLocks");
        Directory.CreateDirectory(lockDirectory);
        var path = Path.Combine(lockDirectory, key + ".lock");
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new InstallationLock(new FileStream(path, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, sharing));
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
            {
                var remaining = timeout - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    throw new IOException(sharing == FileShare.None
                        ? "Feche todas as janelas e perfis do CottonBrowser antes de continuar."
                        : "O CottonBrowser está sendo atualizado ou instalado. Aguarde a conclusão e abra o navegador novamente.", ex);
                Thread.Sleep(remaining < TimeSpan.FromMilliseconds(100)
                    ? remaining : TimeSpan.FromMilliseconds(100));
            }
        }
    }

    public void Dispose() => _file.Dispose();
}
