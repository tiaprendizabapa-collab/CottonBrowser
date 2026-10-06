using Microsoft.Web.WebView2.WinForms;

namespace LeanBrowser;

/// <summary>
/// TabControl can hide its native child without raising the child's managed
/// VisibleChanged notification. Synchronize that inherited visibility before
/// asking the WebView2 controller to suspend the renderer.
/// </summary>
public sealed class TabWebView : WebView2
{
    private double _pageZoom = 1;
    private ulong? _navigationId;
    private bool _applyingZoom;

    public TabWebView()
    {
        CoreWebView2InitializationCompleted += (_, args) =>
        {
            if (!args.IsSuccess) return;
            _pageZoom = ZoomFactor;
            CoreWebView2.NavigationStarting += (_, args) => _navigationId = args.NavigationId;
            // Chromium can reset a user-applied Ctrl+wheel zoom while loading.
            // Restore before layout and again at completion, without recording
            // that automatic reset as the user's new preference.
            CoreWebView2.ContentLoading += (_, args) =>
            {
                if (_navigationId == args.NavigationId) ApplyPageZoom();
            };
            CoreWebView2.NavigationCompleted += (_, args) =>
            {
                if (_navigationId != args.NavigationId) return;
                ApplyPageZoom();
                _navigationId = null;
            };
        };
        ZoomFactorChanged += (_, _) =>
        {
            if (!_applyingZoom && _navigationId is null) _pageZoom = ZoomFactor;
        };
    }

    public void SetPageZoom(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        _pageZoom = factor;
        ApplyPageZoom();
    }

    private void ApplyPageZoom()
    {
        if (IsDisposed || CoreWebView2 is null) return;
        _applyingZoom = true;
        try { ZoomFactor = _pageZoom; }
        finally { _applyingZoom = false; }
    }

    public void SynchronizeVisibility() => base.OnVisibleChanged(EventArgs.Empty);
}
