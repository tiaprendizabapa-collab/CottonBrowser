using Microsoft.Web.WebView2.WinForms;

namespace LeanBrowser;

/// <summary>
/// TabControl can hide its native child without raising the child's managed
/// VisibleChanged notification. Synchronize that inherited visibility before
/// asking the WebView2 controller to suspend the renderer.
/// </summary>
public sealed class TabWebView : WebView2
{
    private readonly Dictionary<string, double> _privateSiteZoom = new(StringComparer.OrdinalIgnoreCase);
    private bool _restoringZoom;
    private bool _navigating;

    public TabWebView()
    {
        CoreWebView2InitializationCompleted += (_, args) =>
        {
            if (!args.IsSuccess || CoreWebView2 is not { } core) return;
            core.NavigationStarting += (_, navigation) => { if (!navigation.Cancel) _navigating = true; };
            core.SourceChanged += (_, _) => RestoreSiteZoom();
            core.ContentLoading += (_, _) =>
            {
                RestoreSiteZoom();
                _navigating = false;
            };
            core.NavigationCompleted += (_, _) =>
            {
                RestoreSiteZoom();
                _navigating = false;
            };
            ZoomFactorChanged += (_, _) =>
            {
                if (!_restoringZoom && !_navigating) SaveSiteZoom();
            };
        };
    }

    public void SaveSiteZoom()
    {
        if (CoreWebView2 is not { } core
            || !BrowserPreferences.TryGetZoomHost(core.Source, out var host)) return;
        if (core.Profile.IsInPrivateModeEnabled)
        {
            if (ZoomFactor == 1) _privateSiteZoom.Remove(host);
            else _privateSiteZoom[host] = ZoomFactor;
        }
        else BrowserPreferences.Current.SetSiteZoom(core.Source, ZoomFactor);
    }

    public void SetPageZoom(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        ZoomFactor = factor;
        SaveSiteZoom();
    }

    private void RestoreSiteZoom()
    {
        if (CoreWebView2 is not { } core) return;
        _restoringZoom = true;
        try
        {
            var factor = core.Profile.IsInPrivateModeEnabled
                ? BrowserPreferences.TryGetZoomHost(core.Source, out var host)
                    && _privateSiteZoom.TryGetValue(host, out var privateFactor) ? privateFactor : 1
                : BrowserPreferences.Current.GetSiteZoom(core.Source);
            if (Math.Abs(ZoomFactor - factor) > 0.0001) ZoomFactor = factor;
        }
        finally { _restoringZoom = false; }
    }

    public void SynchronizeVisibility() => base.OnVisibleChanged(EventArgs.Empty);
}
