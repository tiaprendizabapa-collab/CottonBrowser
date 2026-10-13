using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Web.WebView2.Core;

namespace LeanBrowser;

/// <summary>Uma extensão por perfil, instalada antes da primeira navegação.</summary>
public sealed class AdProtection
{
    public const string Version = "2026.1006.1931";
    public const string PackageHash = "dd059665fd1d3195f39ade95d30029b7b2582283365b137f83b2fa18cf9250ee";
    private CoreWebView2BrowserExtension? _extension;
    private Task? _initialization;
    public bool Available => _extension is not null;
    public bool Enabled { get; private set; } = true;
    public string? Failure { get; private set; }
    public string? DashboardUrl => _extension is null ? null : $"chrome-extension://{_extension.Id}/dashboard.html";

    public Task InitializeAsync(CoreWebView2 core, string dataDirectory) =>
        _initialization ??= InstallAsync(core, dataDirectory);

    private async Task InstallAsync(CoreWebView2 core, string dataDirectory)
    {
        try
        {
            var root = Path.Combine(dataDirectory, "Extensions");
            Directory.CreateDirectory(root);
            var directory = Path.Combine(root, "uBOL-" + Version + "-" + PackageHash[..12]);
            var idFile = directory + ".id";
            if (!Directory.Exists(directory))
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                    "LeanBrowser.Assets.uBOLite_2026.1006.1931.edge.zip")
                    ?? throw new FileNotFoundException("Pacote do bloqueador ausente.");
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                buffer.Position = 0;
                var hash = Convert.ToHexString(SHA256.HashData(buffer)).ToLowerInvariant();
                if (hash != PackageHash) throw new InvalidDataException("Pacote do bloqueador corrompido.");
                buffer.Position = 0;
                // Extração em diretório novo; um encerramento não deixa a instalação incompleta publicada.
                var staging = directory + ".staging-" + Guid.NewGuid().ToString("N");
                ZipFile.ExtractToDirectory(buffer, staging);
                try { Directory.Move(staging, directory); }
                catch (IOException) when (Directory.Exists(directory))
                { /* Outra instância já publicou esta mesma versão. */ }
            }

            if (File.Exists(idFile))
            {
                var id = (await File.ReadAllTextAsync(idFile)).Trim();
                _extension = (await core.Profile.GetBrowserExtensionsAsync()).FirstOrDefault(e => e.Id == id);
            }
            if (_extension is null)
            {
                _extension = await core.Profile.AddBrowserExtensionAsync(directory);
                await File.WriteAllTextAsync(idFile, _extension.Id);
            }
            await _extension.EnableAsync(Enabled);
            // Versioned unpacked paths have different extension IDs. Leaving
            // the old copy enabled would keep its obsolete blocking rules active.
            await DisablePreviousVersionsAsync(core, root);
        }
        catch (Exception ex)
        {
            _extension = null;
            Failure = $"Não foi possível ativar o uBlock Origin Lite ({ex.GetType().Name}). " +
                "O bloqueio básico permanece ativo. Atualize o WebView2 Runtime e reinicie o navegador.";
        }
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        if (_extension is not null) await _extension.EnableAsync(enabled);
        Enabled = enabled;
    }

    private async Task DisablePreviousVersionsAsync(CoreWebView2 core, string root)
    {
        var previousIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var idFile in Directory.EnumerateFiles(root, "uBOL-*.id"))
        {
            var id = (await File.ReadAllTextAsync(idFile)).Trim();
            if (id != _extension!.Id && id.Length == 32 && id.All(c => c is >= 'a' and <= 'p')) previousIds.Add(id);
        }
        foreach (var installed in await core.Profile.GetBrowserExtensionsAsync())
            if (previousIds.Contains(installed.Id) && installed.IsEnabled) await installed.EnableAsync(false);
    }
}
