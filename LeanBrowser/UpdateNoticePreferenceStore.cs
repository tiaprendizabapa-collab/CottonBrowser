namespace LeanBrowser;

internal sealed class UpdateNoticePreferenceStore
{
    private readonly string _path;

    internal UpdateNoticePreferenceStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LeanBrowser", "update-notice.txt");
    }

    internal bool Suppressed
    {
        get
        {
            try
            {
                return File.Exists(_path)
                    && File.ReadAllText(_path).Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    internal void SetSuppressed(bool suppressed)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, suppressed ? "true" : "false");
    }
}
