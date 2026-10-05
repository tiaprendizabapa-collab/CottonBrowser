using Microsoft.Web.WebView2.WinForms;

namespace LeanBrowser;

/// <summary>
/// TabControl can hide its native child without raising the child's managed
/// VisibleChanged notification. Synchronize that inherited visibility before
/// asking the WebView2 controller to suspend the renderer.
/// </summary>
public sealed class TabWebView : WebView2
{
    public void SynchronizeVisibility() => base.OnVisibleChanged(EventArgs.Empty);
}
