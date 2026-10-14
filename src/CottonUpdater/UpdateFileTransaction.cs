namespace CottonUpdater;

internal static class UpdateFileTransaction
{
    private const string ManifestName = "CottonBrowserInstallFiles.txt";

    internal static void Apply(string target, string extracted, string backup, IReadOnlyList<string> files)
    {
        var manifest = Path.Combine(target, ManifestName);
        // Portable copies do not have an uninstaller or an installation manifest.
        var updateManifest = File.Exists(manifest) &&
            File.Exists(Path.Combine(target, "CottonBrowserUninstall.exe"));
        var manifestFiles = updateManifest
            ? File.ReadAllLines(manifest).Concat(files.Select(file => file.Replace('\\', '/')))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : null;
        var affected = updateManifest ? files.Concat([ManifestName]) : files;
        var existing = new List<string>();
        var created = new List<string>();
        try
        {
            // Save every affected file before replacing the first one, including the uninstall list.
            foreach (var relative in affected)
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
            if (manifestFiles is not null) File.WriteAllLines(manifest, manifestFiles);
        }
        catch
        {
            foreach (var relative in existing)
            {
                try { File.Copy(Path.Combine(backup, relative), Path.Combine(target, relative), true); }
                catch { /* Preserve backup for recovery if restoration fails. */ }
            }
            foreach (var path in created)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
            throw;
        }
    }
}
