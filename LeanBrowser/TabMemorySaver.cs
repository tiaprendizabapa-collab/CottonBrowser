using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace LeanBrowser;

/// <summary>
/// Suspends only hidden, idle pages. The renderer is preserved, so waking a tab does not
/// reload it or discard its forms. This observer exposes no native object or web-message channel.
/// </summary>
public sealed class TabMemorySaver : IDisposable
{
    private readonly BrowserTabControl _view;
    private readonly Func<bool> _hasActiveDownloads;
    private readonly BrowserPreferences _preferences;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30_000 };
    private readonly Dictionary<BrowserTab, TabState> _states = new();
    private BrowserTab? _previousActive;
    private bool _lastEnabled;
    private bool _checking;
    private bool _disposed;

    public TabMemorySaver(BrowserTabControl view, Func<bool> hasActiveDownloads,
        BrowserPreferences? preferences = null)
    {
        _view = view;
        _hasActiveDownloads = hasActiveDownloads;
        _preferences = preferences ?? BrowserPreferences.Current;
        _lastEnabled = _preferences.MemorySaverEnabled;
        _timer.Tick += OnTimerTick;
        _view.SelectedIndexChanged += OnSelectionChanged;
        BrowserPreferences.Changed += OnPreferencesChanged;
    }

    public void Start()
    {
        if (_disposed) return;
        RefreshActiveTab();
        _timer.Start();
    }

    /// <summary>Call after WebView2 initialization and before the first navigation.</summary>
    public async Task RegisterTabAsync(BrowserTab tab)
    {
        if (_disposed || tab.IsDisposed || _states.ContainsKey(tab)) return;
        var core = tab.Web.CoreWebView2;
        if (core is null) return;
        var state = new TabState();
        _states.Add(tab, state);
        tab.Disposed += (_, _) => _states.Remove(tab);
        core.NavigationStarting += (_, _) =>
        {
            state.DocumentVersion++;
            state.CapturedMedia = false;
            tab.LastActivatedAt = DateTimeOffset.UtcNow;
            SetSuspended(tab, false);
        };
        core.PermissionRequested += (_, args) =>
        {
            // A capture request keeps this document awake even if capture has not started
            // yet. It does not approve or alter the existing permission decision.
            if (args.PermissionKind is CoreWebView2PermissionKind.Camera or CoreWebView2PermissionKind.Microphone)
                state.CapturedMedia = true;
        };
        core.ScreenCaptureStarting += (_, _) => state.CapturedMedia = true;
        core.FrameCreated += (_, args) => TrackFrame(state, args.Frame);
        try
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(ActivityObserver);
            if (_disposed || tab.IsDisposed) return;
            state.ObserverReady = true;
            // Also cover an existing document if registration happens after initialization.
            if (!core.IsSuspended) await core.ExecuteScriptAsync(ActivityObserver);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            // Without a working observer, keep the page running at the existing low
            // memory priority; saving memory must not risk a user's draft or call.
            state.ObserverReady = false;
        }
    }

    private static void TrackFrame(TabState state, CoreWebView2Frame frame)
    {
        if (!state.Frames.Add(frame)) return;
        frame.Destroyed += (_, _) => state.Frames.Remove(frame);
        frame.FrameCreated += (_, args) => TrackFrame(state, args.Frame);
        frame.NavigationStarting += (_, _) => state.DocumentVersion++;
        frame.PermissionRequested += (_, args) =>
        {
            if (args.PermissionKind is CoreWebView2PermissionKind.Camera or CoreWebView2PermissionKind.Microphone)
                state.CapturedMedia = true;
        };
        frame.ScreenCaptureStarting += (_, _) => state.CapturedMedia = true;
    }

    public void RefreshActiveTab()
    {
        if (_disposed || _view.IsDisposed) return;
        var active = _view.SelectedTab as BrowserTab;
        var now = DateTimeOffset.UtcNow;
        // Idle time starts when the user leaves the tab, even after a long reading session.
        if (_previousActive is { IsDisposed: false } previous && previous != active)
            previous.LastActivatedAt = now;
        if (active is not null)
        {
            active.LastActivatedAt = now;
            Resume(active);
        }
        _previousActive = active;
    }

    private void OnSelectionChanged(object? sender, EventArgs e) => RefreshActiveTab();

    private void OnPreferencesChanged()
    {
        if (_disposed || _view.IsDisposed) return;
        if (_view.InvokeRequired)
        {
            if (_view.IsHandleCreated) _view.BeginInvoke(new Action(OnPreferencesChanged));
            return;
        }
        if (_preferences.MemorySaverEnabled && !_lastEnabled)
            foreach (var tab in _states.Keys.ToArray())
                tab.LastActivatedAt = DateTimeOffset.UtcNow;
        _lastEnabled = _preferences.MemorySaverEnabled;
        foreach (var tab in _states.Keys.ToArray())
            if (!_preferences.MemorySaverEnabled ||
                TabMemorySafety.IsSiteException(tab.Web.CoreWebView2?.Source, _preferences.MemorySaverExceptions))
                Resume(tab);
    }

    public void Resume(BrowserTab tab)
    {
        if (tab.IsDisposed) return;
        try
        {
            var core = tab.Web.CoreWebView2;
            if (core?.IsSuspended == true) core.Resume();
            SetSuspended(tab, false);
        }
        catch (Exception ex) when (IsUnavailable(ex)) { }
    }

    private async void OnTimerTick(object? sender, EventArgs e) => await CheckNowAsync();

    /// <summary>Checks the current idle pages once; overlapping checks are ignored.</summary>
    public async Task CheckNowAsync()
    {
        if (_checking || _disposed || _view.IsDisposed) return;
        _checking = true;
        try
        {
            foreach (var (tab, state) in _states.ToArray())
            {
                if (_disposed) break;
                await TrySuspendAsync(tab, state);
            }
        }
        finally { _checking = false; }
    }

    public async Task<bool> SuspendNowAsync(BrowserTab tab)
    {
        if (_checking || !_states.TryGetValue(tab, out var state)) return false;
        _checking = true;
        try { await TrySuspendAsync(tab, state, true); return !tab.IsDisposed && tab.IsSuspended; }
        finally { _checking = false; }
    }

    private bool CanConsider(BrowserTab tab, TabState state, bool ignoreIdle = false)
    {
        if (_disposed || _view.IsDisposed || tab.IsDisposed || tab.IsFloatingVideo || !state.ObserverReady ||
            !_view.TabPages.Contains(tab)) return false;
        var core = tab.Web.CoreWebView2;
        if (core is null || TabMemorySafety.IsSiteException(core.Source, _preferences.MemorySaverExceptions))
            return false;
        if (core.IsSuspended)
        {
            SetSuspended(tab, true);
            return false;
        }
        SetSuspended(tab, false);
        return TabMemorySafety.ShouldConsider(ignoreIdle || _preferences.MemorySaverEnabled,
            _view.SelectedTab == tab, tab.Web.Visible, tab.Loading, core.IsDocumentPlayingAudio,
            _hasActiveDownloads(), state.CapturedMedia, false, ignoreIdle ? DateTimeOffset.UtcNow.AddMinutes(-_preferences.SuspendAfterMinutes - 1) : tab.LastActivatedAt,
            DateTimeOffset.UtcNow, _preferences.SuspendAfterMinutes);
    }

    private async Task TrySuspendAsync(BrowserTab tab, TabState state, bool ignoreIdle = false)
    {
        try
        {
            if (!CanConsider(tab, state, ignoreIdle)) return;
            var core = tab.Web.CoreWebView2;
            var version = state.DocumentVersion;
            var lastActivated = tab.LastActivatedAt;
            if (await core.ExecuteScriptAsync(SafeToSuspendProbe).WaitAsync(TimeSpan.FromSeconds(3)) != "true")
                return;
            foreach (var frame in state.Frames.ToArray())
            {
                if (!CanConsider(tab, state, ignoreIdle) || state.DocumentVersion != version) return;
                if (frame.IsDestroyed() != 0) continue;
                if (await frame.ExecuteScriptAsync(SafeToSuspendProbe).WaitAsync(TimeSpan.FromSeconds(3)) != "true")
                    return;
            }
            // Script execution yields to the UI: the user may have switched tabs,
            // navigated, started a download or changed a setting while it was running.
            if (!CanConsider(tab, state, ignoreIdle) || state.DocumentVersion != version ||
                tab.LastActivatedAt != lastActivated) return;
            if (tab.Web is TabWebView tabWeb) tabWeb.SynchronizeVisibility();
            var suspended = await core.TrySuspendAsync();
            if (tab.IsDisposed) return;
            if (_disposed || _view.IsDisposed || _view.SelectedTab == tab || tab.Web.Visible || tab.IsFloatingVideo ||
                (!ignoreIdle && !_preferences.MemorySaverEnabled) || state.DocumentVersion != version ||
                state.CapturedMedia || tab.Loading || core.IsDocumentPlayingAudio ||
                tab.LastActivatedAt != lastActivated || _hasActiveDownloads() ||
                TabMemorySafety.IsSiteException(core.Source, _preferences.MemorySaverExceptions))
            {
                // Resume also covers a late completion after selection already woke it.
                if (core.IsSuspended) core.Resume();
                SetSuspended(tab, false);
            }
            else SetSuspended(tab, suspended && core.IsSuspended);
        }
        catch (Exception ex) when (IsUnavailable(ex) || ex is TimeoutException)
        {
            // Best effort; navigation, frame removal and unsupported runtimes stay awake.
        }
    }

    private void SetSuspended(BrowserTab tab, bool value)
    {
        if (tab.IsDisposed || tab.IsSuspended == value) return;
        tab.IsSuspended = value;
        if (!_view.IsDisposed) _view.RefreshTab(tab);
    }

    private static bool IsUnavailable(Exception ex) =>
        ex is InvalidOperationException or COMException or NotImplementedException or OperationCanceledException;

    public void Dispose()
    {
        if (_disposed) return;
        _timer.Stop();
        _timer.Dispose();
        BrowserPreferences.Changed -= OnPreferencesChanged;
        _view.SelectedIndexChanged -= OnSelectionChanged;
        foreach (var tab in _states.Keys.ToArray()) Resume(tab);
        _states.Clear();
        _disposed = true;
    }

    private sealed class TabState
    {
        public bool ObserverReady;
        public bool CapturedMedia;
        public int DocumentVersion;
        public HashSet<CoreWebView2Frame> Frames { get; } = [];
    }

    internal const string ActivityObserver = """
        (() => {
          if (typeof window.__cottonHasDraft === 'function') return;
          let edited = false;
          // Only a boolean lives in the document. No field value, key, clipboard
          // content or browsing data is sent to the host or persisted.
          document.addEventListener('input', e => { if (e.isTrusted) edited = true; }, true);
          document.addEventListener('change', e => { if (e.isTrusted) edited = true; }, true);
          document.addEventListener('reset', e => { if (e.isTrusted) edited = false; }, true);
          Object.defineProperty(window, '__cottonHasDraft', { value: () => edited, configurable: false });
        })();
        """;

    internal const string SafeToSuspendProbe = """
        (() => {
          try {
            if (document.readyState !== 'complete' || typeof window.__cottonHasDraft !== 'function'
                || window.__cottonHasDraft()) return false;
            if (document.querySelector('[contenteditable="true"], [contenteditable=""], [role="textbox"]')) return false;
            for (const media of document.querySelectorAll('audio, video')) {
              if (!media.paused && !media.ended) return false;
              if (media.srcObject && media.srcObject.active) return false;
            }
            for (const field of document.querySelectorAll('input, textarea, select')) {
              if (field.type === 'hidden' || field.type === 'button' || field.type === 'submit' || field.type === 'reset') continue;
              if (field.type === 'checkbox' || field.type === 'radio') {
                if (field.checked !== field.defaultChecked) return false;
              } else if (field.tagName === 'SELECT') {
                const options = Array.from(field.options);
                const defaults = options.some(option => option.defaultSelected);
                if (options.some((option, index) => option.selected !==
                    (defaults || field.multiple ? option.defaultSelected : index === 0))) return false;
              } else if (field.value !== field.defaultValue) return false;
            }
            return true;
          } catch { return false; }
        })();
        """;
}
