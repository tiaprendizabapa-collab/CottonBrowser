using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace CottonInstaller;

internal static class WebView2Runtime
{
    private const string RegistryPath = @"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    private const string ResourceName = "CottonBrowser.WebView2Setup.exe";

    internal static bool IsInstalledVersion(string? value) =>
        Version.TryParse(value, out var version) && version > new Version(0, 0, 0, 0);

    internal static bool IsInstalled()
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(RegistryPath);
                if (IsInstalledVersion(key?.GetValue("pv") as string)) return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        return false;
    }

    internal static string ExtractOfflineInstaller(string staging)
    {
        using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("O setup não contém o instalador offline do Microsoft WebView2.");
        // O instalador completo é maior que o bootstrapper que depende de internet.
        if (input.Length < 20L * 1024 * 1024 || input.ReadByte() != 'M' || input.ReadByte() != 'Z')
            throw new InvalidDataException("O componente offline do Microsoft WebView2 é inválido.");
        input.Position = 0;
        Directory.CreateDirectory(staging);
        var path = Path.Combine(staging, "MicrosoftEdgeWebView2RuntimeInstallerX64.exe");
        using (var output = new FileStream(path, FileMode.CreateNew)) input.CopyTo(output);
        return path;
    }

    internal static void EnsureInstalled(string staging, Action<string> report)
    {
        if (IsInstalled()) { report("Microsoft WebView2 já está disponível."); return; }
        report("Preparando o Microsoft WebView2 incluído no setup…");
        var installer = ExtractOfflineInstaller(staging);
        var start = new ProcessStartInfo(installer)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = staging
        };
        start.ArgumentList.Add("/silent"); start.ArgumentList.Add("/install");
        report("Instalando Microsoft WebView2. Isso pode levar alguns minutos…");
        using var process = Process.Start(start) ?? throw new IOException("Não foi possível iniciar o componente Microsoft WebView2.");
        if (!process.WaitForExit(15 * 60 * 1000))
            throw new IOException("O Microsoft WebView2 ainda está sendo instalado. Aguarde a conclusão e execute o setup novamente.");
        if (!IsInstalled())
            throw new IOException($"O Microsoft WebView2 não ficou disponível (código {process.ExitCode}). " +
                "Se este PC tem restrições de instalação, consulte o responsável pelo computador e execute o setup novamente.");
        report("Microsoft WebView2 preparado.");
    }
}
