using Microsoft.Web.WebView2.Core;

namespace LeanBrowser;

/// <summary>Hosts the existing renderer; never reloads a second copy of the video.</summary>
internal sealed class FloatingVideoWindow : Form
{
    internal const string EnterScript = """
    (() => {
      if (window.__cottonFloatingVideo) return true;
      const video = [...document.querySelectorAll('video')]
        .filter(v => v.readyState > 0 && v.getBoundingClientRect().width > 0)
        .sort((a,b) => b.clientWidth*b.clientHeight-a.clientWidth*a.clientHeight)[0];
      if (!video) return false;
      const path=[]; for(let p=video.parentElement;p;p=p.parentElement) path.push(p);
      const style=document.createElement('style');
      style.textContent=`html,body {overflow:hidden!important;background:#000!important}
        body * {visibility:hidden!important}
        [data-cotton-floating-path] {transform:none!important;contain:none!important;position:static!important;overflow:visible!important}
        video::-webkit-media-controls-fullscreen-button {display:none!important}
        video[data-cotton-floating-video] {visibility:visible!important;position:fixed!important;inset:0!important;
          width:100vw!important;height:100vh!important;max-width:none!important;max-height:none!important;
          object-fit:contain!important;margin:0!important;z-index:2147483647!important;background:#000!important}`;
      const controls=video.controls;
      path.forEach(p=>p.setAttribute('data-cotton-floating-path',''));
      video.setAttribute('data-cotton-floating-video',''); video.controls=true;
      document.head.append(style);
      window.__cottonFloatingVideo={video,path,style,controls}; return true;
    })()
    """;
    internal const string ExitScript = """
    (() => { const s=window.__cottonFloatingVideo;if(!s)return;
      s.style.remove();s.path.forEach(p=>p.removeAttribute('data-cotton-floating-path'));
      s.video.removeAttribute('data-cotton-floating-video');s.video.controls=s.controls;
      delete window.__cottonFloatingVideo;
    })()
    """;
    private readonly BrowserTab _tab;
    private readonly CoreWebView2 _core;
    private bool _returned;
    public FloatingVideoWindow(BrowserTab tab)
    {
        _tab = tab; _core = tab.Web.CoreWebView2;
        Text = "Vídeo flutuante · CottonBrowser"; TopMost = true; ClientSize = new Size(480, 270);
        MinimumSize = new Size(280, 180); StartPosition = FormStartPosition.Manual;
        var screen = Screen.FromControl(tab).WorkingArea;
        Location = new Point(screen.Right - Width - 24, screen.Bottom - Height - 24);
        BackColor = Color.Black; _tab.IsFloatingVideo = true;
        Controls.Add(tab.Web); tab.Web.Dock = DockStyle.Fill;
        _core.NavigationStarting += NavigationStarting; tab.Disposed += TabDisposed;
    }
    private void NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args) => Close();
    private void TabDisposed(object? sender, EventArgs args) => Close();
    internal bool HandleVideoShortcut(Keys key)
    {
        Action? action = key switch
        {
            Keys.Control | Keys.W or Keys.Escape => Close,
            Keys.F5 or Keys.Control | Keys.R => () => _core.Reload(),
            Keys.F12 => () => _core.OpenDevToolsWindow(),
            _ => null
        };
        if (key == ShortcutCatalog.Get("mute", BrowserPreferences.Current)) action = () => _core.IsMuted = !_core.IsMuted;
        if (action is null) return false;
        if (!IsDisposed && IsHandleCreated) BeginInvoke(action);
        return true;
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => HandleVideoShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        ReturnToTab(); base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) ReturnToTab(); base.Dispose(disposing);
    }
    private void ReturnToTab()
    {
        if (_returned) return; _returned = true;
        _core.NavigationStarting -= NavigationStarting; _tab.Disposed -= TabDisposed;
        _tab.IsFloatingVideo = false;
        if (!_tab.IsDisposed && !_tab.Disposing && !_tab.Web.IsDisposed)
        {
            _tab.Controls.Add(_tab.Web); _tab.Web.Dock = DockStyle.Fill;
            _ = RestoreAsync();
        }
    }
    private async Task RestoreAsync()
    {
        try { await _core.ExecuteScriptAsync(ExitScript); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }
}
