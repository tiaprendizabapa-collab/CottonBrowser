using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace CottonInstaller;

internal static class Program
{
    private const string ProductName = "CottonBrowser";
    private const string PayloadName = "CottonBrowser.Payload.zip";
    private const string ManifestName = "CottonBrowserInstallFiles.txt";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CottonBrowser";
    private static readonly string InstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", ProductName);
    private static readonly string StartMenuShortcut = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), ProductName + ".lnk");

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.SequenceEqual(["--install"]))
            {
                Install();
                return 0;
            }
            if (args.SequenceEqual(["--uninstall"]))
            {
                if (MessageBox.Show("Desinstalar o CottonBrowser? Seus favoritos, histórico e preferências serão preservados.",
                    ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;

                var helper = Path.Combine(Path.GetTempPath(), "CottonBrowserUninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(Environment.ProcessPath!, helper);
                var start = new ProcessStartInfo(helper) { UseShellExecute = true };
                start.ArgumentList.Add("--uninstall-worker");
                start.ArgumentList.Add(Environment.ProcessId.ToString());
                Process.Start(start);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--uninstall-worker" && int.TryParse(args[1], out var parentPid))
            {
                try { Process.GetProcessById(parentPid).WaitForExit(15000); }
                catch (ArgumentException) { }
                Uninstall();
                MessageBox.Show("CottonBrowser desinstalado. Seus dados de navegação foram preservados.",
                    ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            if (args.Length != 0) throw new ArgumentException("Parâmetros desconhecidos.");
            Application.Run(new InstallerForm());
            return 0;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "CottonBrowserSetup-last-error.txt"), ex.ToString()); }
            catch { }
            MessageBox.Show("Não foi possível concluir a operação.\n\n" + ex.Message,
                ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static string Destination => InstallDirectory;
    internal static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    internal static void Install()
    {
        if (Directory.Exists(InstallDirectory) &&
            !File.Exists(Path.Combine(InstallDirectory, "CottonBrowserUninstall.exe")))
            throw new IOException("A pasta de instalação já existe e não pertence a este instalador: " + InstallDirectory);
        EnsureBrowserClosed();

        var staging = Path.Combine(Path.GetTempPath(), "CottonBrowserInstall-" + Guid.NewGuid().ToString("N"));
        var extracted = Path.Combine(staging, "extracted");
        var backup = Path.Combine(staging, "backup");
        var written = new List<string>();
        var replaced = new List<string>();
        var shortcutCreated = false;
        var shortcutExisted = File.Exists(StartMenuShortcut);
        var rollbackFailed = false;
        try
        {
            Directory.CreateDirectory(extracted);
            var files = ExtractPayload(extracted);
            Directory.CreateDirectory(InstallDirectory);
            foreach (var relative in files)
            {
                var destination = Path.Combine(InstallDirectory, relative);
                if (File.Exists(destination))
                {
                    var saved = Path.Combine(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Copy(destination, saved);
                    replaced.Add(relative);
                }
                else written.Add(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(extracted, relative), destination, overwrite: true);
            }

            var uninstaller = Path.Combine(InstallDirectory, "CottonBrowserUninstall.exe");
            if (File.Exists(uninstaller))
            {
                Directory.CreateDirectory(backup);
                File.Copy(uninstaller, Path.Combine(backup, "CottonBrowserUninstall.exe"));
                replaced.Add("CottonBrowserUninstall.exe");
            }
            else written.Add(uninstaller);
            File.Copy(Environment.ProcessPath!, uninstaller, overwrite: true);

            var manifest = Path.Combine(InstallDirectory, ManifestName);
            if (File.Exists(manifest))
            {
                Directory.CreateDirectory(backup);
                File.Copy(manifest, Path.Combine(backup, ManifestName));
                replaced.Add(ManifestName);
            }
            else written.Add(manifest);
            File.WriteAllLines(manifest, files.Select(file => file.Replace('\\', '/')));

            if (shortcutExisted)
            {
                Directory.CreateDirectory(backup);
                File.Copy(StartMenuShortcut, Path.Combine(backup, "StartMenu.lnk"));
            }
            CreateShortcut(Path.Combine(InstallDirectory, "CottonBrowser.exe"));
            shortcutCreated = true;
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey, writable: true)
                ?? throw new IOException("Não foi possível registrar o aplicativo no Windows.");
            key.SetValue("DisplayName", ProductName);
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", ProductName);
            key.SetValue("InstallLocation", InstallDirectory);
            key.SetValue("DisplayIcon", Path.Combine(InstallDirectory, "CottonBrowser.exe"));
            key.SetValue("UninstallString", "\"" + uninstaller + "\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue,
                files.Sum(file => new FileInfo(Path.Combine(InstallDirectory, file)).Length) / 1024), RegistryValueKind.DWord);
        }
        catch
        {
            if (shortcutCreated && !shortcutExisted) TryDelete(StartMenuShortcut);
            if (shortcutCreated && shortcutExisted)
            {
                try { File.Copy(Path.Combine(backup, "StartMenu.lnk"), StartMenuShortcut, true); }
                catch { rollbackFailed = true; }
            }
            foreach (var path in written) TryDelete(path);
            foreach (var relative in replaced)
            {
                try { File.Copy(Path.Combine(backup, relative), Path.Combine(InstallDirectory, relative), true); }
                catch { rollbackFailed = true; }
            }
            throw;
        }
        finally
        {
            if (!rollbackFailed)
                try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    private static List<string> ExtractPayload(string destination)
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadName)
            ?? throw new InvalidDataException("O instalador não contém o pacote do navegador.");
        using var archive = new ZipArchive(resource, ZipArchiveMode.Read);
        if (archive.Entries.Count > 256) throw new InvalidDataException("Pacote com arquivos demais.");
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            var name = entry.FullName.Replace('\\', '/');
            var parts = name.Split('/');
            if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".." ||
                part.TrimEnd(' ', '.') != part || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) ||
                !(name.Equals("CottonBrowser.exe", StringComparison.OrdinalIgnoreCase) ||
                  name.Equals("CottonUpdater.exe", StringComparison.OrdinalIgnoreCase) ||
                  name.StartsWith("Assets/Bridge/", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Arquivo inválido no pacote: " + name);
            var relative = Path.Combine(parts);
            if (!seen.Add(relative)) throw new InvalidDataException("Arquivo duplicado no pacote: " + name);
            total += entry.Length;
            if (total > 250L * 1024 * 1024) throw new InvalidDataException("Pacote grande demais.");
            var output = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            entry.ExtractToFile(output);
            files.Add(relative);
        }
        if (!seen.Contains("CottonBrowser.exe") || !seen.Contains("CottonUpdater.exe"))
            throw new InvalidDataException("O pacote não contém os executáveis necessários.");
        return files;
    }

    private static void CreateShortcut(string executable)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new IOException("O Windows não disponibilizou a criação de atalhos.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(StartMenuShortcut);
        shortcut.TargetPath = executable;
        shortcut.WorkingDirectory = InstallDirectory;
        shortcut.IconLocation = executable + ",0";
        shortcut.Description = "Navegador CottonBrowser";
        shortcut.Save();
    }

    private static void Uninstall()
    {
        EnsureBrowserClosed();
        if (!File.Exists(Path.Combine(InstallDirectory, "CottonBrowserUninstall.exe")))
            throw new IOException("A instalação do CottonBrowser não foi encontrada.");
        var manifest = Path.Combine(InstallDirectory, ManifestName);
        if (!File.Exists(manifest)) throw new IOException("A lista de arquivos instalados não foi encontrada.");
        var files = File.ReadAllLines(manifest);
        foreach (var name in files)
        {
            var parts = name.Split('/');
            if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".." ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) ||
                !(name.Equals("CottonBrowser.exe", StringComparison.OrdinalIgnoreCase) ||
                  name.Equals("CottonUpdater.exe", StringComparison.OrdinalIgnoreCase) ||
                  name.StartsWith("Assets/Bridge/", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Lista de instalação inválida.");
        }
        foreach (var name in files) File.Delete(Path.Combine(InstallDirectory, name.Replace('/', Path.DirectorySeparatorChar)));
        File.Delete(Path.Combine(InstallDirectory, "CottonBrowserUninstall.exe"));
        File.Delete(manifest);
        TryDelete(StartMenuShortcut);
        Registry.CurrentUser.DeleteSubKey(UninstallKey, throwOnMissingSubKey: false);
        foreach (var directory in Directory.EnumerateDirectories(InstallDirectory, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        if (!Directory.EnumerateFileSystemEntries(InstallDirectory).Any()) Directory.Delete(InstallDirectory);
    }

    private static void EnsureBrowserClosed()
    {
        foreach (var process in Process.GetProcessesByName(ProductName))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is { } executable && Path.GetFullPath(executable)
                        .StartsWith(InstallDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Feche o CottonBrowser instalado antes de continuar.");
                }
                catch (System.ComponentModel.Win32Exception) { /* outro processo sem acesso */ }
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed class InstallerForm : Form
    {
        internal InstallerForm()
        {
            Text = "Instalar CottonBrowser";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(420, 190);
            Font = new Font("Segoe UI", 10);

            var title = new Label { Text = "CottonBrowser " + Version, Font = new Font(Font, FontStyle.Bold),
                AutoSize = true, Location = new Point(24, 22) };
            var description = new Label { Text = "Instalar para este usuário e adicionar ao menu Iniciar.",
                AutoSize = true, Location = new Point(24, 60) };
            var path = new Label { Text = Destination, AutoEllipsis = true,
                Location = new Point(24, 91), Size = new Size(372, 42) };
            var install = new Button { Text = "Instalar", Location = new Point(288, 143),
                Size = new Size(108, 32) };
            install.Click += (_, _) =>
            {
                install.Enabled = false;
                try
                {
                    Install();
                    MessageBox.Show(this, "CottonBrowser instalado. Procure por CottonBrowser no menu Iniciar.",
                        ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Falha na instalação.\n\n" + ex.Message,
                        ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    install.Enabled = true;
                }
            };
            AcceptButton = install;
            Controls.AddRange([title, description, path, install]);
        }
    }
}
