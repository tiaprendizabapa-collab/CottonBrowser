using System.Drawing;

namespace LeanBrowser;

internal static class WindowLayout
{
    public static Rectangle MaximizedBounds(Rectangle monitor, Rectangle workArea) =>
        new(workArea.Left - monitor.Left, workArea.Top - monitor.Top,
            workArea.Width, workArea.Height);

    public static Rectangle FitNormal(Rectangle window, Rectangle workArea)
    {
        var width = Math.Min(window.Width, workArea.Width);
        var height = Math.Min(window.Height, workArea.Height);
        return new Rectangle(
            Math.Clamp(window.Left, workArea.Left, workArea.Right - width),
            Math.Clamp(window.Top, workArea.Top, workArea.Bottom - height),
            width, height);
    }
}
