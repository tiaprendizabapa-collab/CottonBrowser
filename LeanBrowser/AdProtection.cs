using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Web.WebView2.Core;

namespace LeanBrowser;

/// <summary>Uma extensão por perfil, instalada antes da primeira navegação.</summary>
public sealed class AdProtection
{
    public const string Version = "2026.914.1325";
    public const string PackageHash = "4a8d6c13f772dd53e0b1c2f7cbb8090d4df72987e723b946aeb8bbcc80921be5";
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
                    "LeanBrowser.Assets.uBOLite_2026.914.1325.edge.zip")
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
}
