using System.Collections.Concurrent;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LeanBrowser;

/// <summary>
/// Default-deny permission gate. Camera, microphone, location, and browser
/// notifications require an explicit decision, then remember it per origin
/// in a regular profile. Private decisions are never persisted.
/// </summary>
public sealed class PermissionPolicy : IDisposable
{
    private static readonly TimeSpan ConsentTimeout = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<Guid, PendingPermission> _pending = new();
    private readonly ConcurrentDictionary<string, Guid> _pendingByOriginAndKind = new(StringComparer.Ordinal);
    private readonly object _loadLock = new();
    private static readonly ConcurrentDictionary<string, CoreWebView2PermissionState> _saved = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, bool> _blocked = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, bool> _lastDecisions = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, long> _revisions = new(StringComparer.Ordinal);
    private Task? _loadPersistedPermissions;
    private int _disposed;

    public event Action<PermissionPrompt>? PromptCreated;

    private static string UiKey(string origin, string kind, bool isPrivate) =>
        (isPrivate ? "private" : "regular") + "|" + origin + "|" + kind;

    public bool IsBlocked(string origin, string kind, bool isPrivate)
    {
        var key = UiKey(origin, kind, isPrivate);
        return _blocked.ContainsKey(key) || _saved.TryGetValue(key, out var state) && state == CoreWebView2PermissionState.Deny;
    }

    public bool? LastDecision(string origin, string kind, bool isPrivate)
    {
        var key = UiKey(origin, kind, isPrivate);
        if (_lastDecisions.TryGetValue(key, out var allow)) return allow;
        return _saved.TryGetValue(key, out var state) ? state == CoreWebView2PermissionState.Allow : null;
    }

    public void SetBlocked(string origin, string kind, bool isPrivate, bool blocked)
    {
        var key = UiKey(origin, kind, isPrivate);
        _revisions.AddOrUpdate(key, 1, (_, revision) => revision + 1);
        _saved.TryRemove(key, out _);
        if (blocked)
        {
            _blocked[key] = true;
            _lastDecisions[key] = false;
            foreach (var pending in _pending.Values.Where(p => p.Key == key)) pending.Decision.TrySetResult(false);
        }
        else
        {
            _blocked.TryRemove(key, out _);
            _lastDecisions.TryRemove(key, out _);
        }
    }

    private bool RecordDecision(string key, bool allow)
    {
        allow = allow && !_blocked.ContainsKey(key) && Volatile.Read(ref _disposed) == 0;
        _lastDecisions[key] = allow;
        return allow;
    }

    public IDisposable Attach(WebView2 control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var core = control.CoreWebView2
            ?? throw new InvalidOperationException("CoreWebView2 must be initialized before attaching the permission policy.");
        return new Subscription(this, core);
    }

    /// <summary>Loads existing decisions before the first tab navigates.</summary>
    public Task LoadPersistedPermissionsAsync(CoreWebView2Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (_loadLock)
            return _loadPersistedPermissions ??= LoadPersistedPermissionsCoreAsync(profile);
    }

    public IReadOnlyList<PermissionPrompt> GetPending() =>
        _pending.Values
            .Select(pending => pending.Prompt)
            .OrderBy(prompt => prompt.RequestedAt)
            .ToArray();

    public bool Resolve(Guid requestId, bool allow) =>
        _pending.TryGetValue(requestId, out var pending)
        && pending.Decision.TrySetResult(allow);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var pending in _pending.Values)
            pending.Decision.TrySetResult(null);
    }

    private async Task LoadPersistedPermissionsCoreAsync(CoreWebView2Profile profile)
    {
        var settings = await profile.GetNonDefaultPermissionSettingsAsync();
        foreach (var setting in settings)
        {
            if (IsConsentEligible(setting.PermissionKind)
                && TryGetSecureOrigin(setting.PermissionOrigin, out var origin)
                && setting.PermissionState is CoreWebView2PermissionState.Allow or CoreWebView2PermissionState.Deny)
            {
                var key = PermissionKey("regular", origin, setting.PermissionKind);
                if (!_blocked.ContainsKey(key)) _saved[key] = setting.PermissionState;
            }
        }
    }

    private void HandlePermissionRequest(CoreWebView2 core, CoreWebView2PermissionRequestedEventArgs args)
    {
        args.Handled = true;
        args.SavesInProfile = false;
        args.State = CoreWebView2PermissionState.Deny;

        if (Volatile.Read(ref _disposed) != 0
            || !IsConsentEligible(args.PermissionKind)
            || !TryGetSecureOrigin(args.Uri, out var origin))
            return;

        var isPrivate = core.Profile.IsInPrivateModeEnabled;
        var key = PermissionKey(isPrivate ? "private" : "regular", origin, args.PermissionKind);
        if (_blocked.ContainsKey(key)) return;
        if (!isPrivate && _saved.TryGetValue(key, out var savedState))
        {
            args.State = RecordDecision(key, savedState == CoreWebView2PermissionState.Allow)
                ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            return;
        }
        if (!args.IsUserInitiated) return;
        if (_pendingByOriginAndKind.TryGetValue(key, out var activeRequestId)
            && _pending.TryGetValue(activeRequestId, out var activePending))
        {
            var sharedDeferral = args.GetDeferral();
            _ = CompleteWithDecisionAsync(args, sharedDeferral, activePending.Decision.Task,
                saveInProfile: !isPrivate, key: key);
            return;
        }

        var requestId = Guid.NewGuid();
        var prompt = new PermissionPrompt(
            requestId,
            origin,
            args.PermissionKind.ToString(),
            DateTimeOffset.UtcNow);
        var pending = new PendingPermission(
            core,
            key,
            prompt,
            new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously));

        if (!_pending.TryAdd(requestId, pending)
            || !_pendingByOriginAndKind.TryAdd(key, requestId))
        {
            _pending.TryRemove(requestId, out _);
            return;
        }

        var deferral = args.GetDeferral();
        _ = CompleteWhenResolvedAsync(args, deferral, pending, !isPrivate);

        try
        {
            PromptCreated?.Invoke(prompt);
        }
        catch
        {
            pending.Decision.TrySetResult(null);
        }
    }

    private async Task CompleteWhenResolvedAsync(
        CoreWebView2PermissionRequestedEventArgs args,
        CoreWebView2Deferral deferral,
        PendingPermission pending,
        bool saveInProfile)
    {
        try
        {
            await CompleteWithDecisionAsync(
                args,
                deferral,
                pending.Decision.Task,
                saveInProfile,
                pending.Key,
                () => pending.Decision.TrySetResult(null));
        }
        finally
        {
            _pending.TryRemove(pending.Prompt.Id, out _);
            _pendingByOriginAndKind.TryRemove(pending.Key, out _);
        }
    }

    private async Task CompleteWithDecisionAsync(
        CoreWebView2PermissionRequestedEventArgs args,
        CoreWebView2Deferral deferral,
        Task<bool?> decision,
        bool saveInProfile,
        string key,
        Action? onTimeout = null)
    {
        var decided = false;
        var revision = _revisions.GetValueOrDefault(key);
        try
        {
            var allow = await decision.WaitAsync(ConsentTimeout);
            args.State = RecordDecision(key, allow == true && revision == _revisions.GetValueOrDefault(key))
                ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            decided = allow.HasValue;
        }
        catch (TimeoutException)
        {
            onTimeout?.Invoke();
            RecordDecision(key, false);
            args.State = CoreWebView2PermissionState.Deny;
        }
        finally
        {
            args.Handled = true;
            args.SavesInProfile = decided && saveInProfile && revision == _revisions.GetValueOrDefault(key)
                && !_blocked.ContainsKey(key) && Volatile.Read(ref _disposed) == 0;
            if (args.SavesInProfile) _saved[key] = args.State;
            deferral.Complete();
        }
    }

    private static string PermissionKey(string scope, string origin, CoreWebView2PermissionKind kind) =>
        scope + "|" + origin + "|" + kind;

    private void DenyRequestsFor(CoreWebView2 core)
    {
        foreach (var pending in _pending.Values.Where(pending => ReferenceEquals(pending.Core, core)))
            pending.Decision.TrySetResult(null);
    }

    private static bool IsConsentEligible(CoreWebView2PermissionKind kind) => kind is
        CoreWebView2PermissionKind.Camera or
        CoreWebView2PermissionKind.Microphone or
        CoreWebView2PermissionKind.Geolocation or
        CoreWebView2PermissionKind.Notifications;

    private static bool TryGetSecureOrigin(string value, out string origin)
    {
        origin = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || string.Equals(uri.Host, TrustedBrowserBridge.HostName, StringComparison.OrdinalIgnoreCase))
            return false;

        origin = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    private sealed record PendingPermission(
        CoreWebView2 Core,
        string Key,
        PermissionPrompt Prompt,
        TaskCompletionSource<bool?> Decision);

    private sealed class Subscription : IDisposable
    {
        private readonly PermissionPolicy _policy;
        private readonly CoreWebView2 _core;
        private readonly List<CoreWebView2Frame> _frames = new();
        private int _disposed;

        public Subscription(PermissionPolicy policy, CoreWebView2 core)
        {
            _policy = policy;
            _core = core;
            _core.PermissionRequested += OnPermissionRequested;
            _core.FrameCreated += OnFrameCreated;
        }

        private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs args) =>
            _policy.HandlePermissionRequest(_core, args);

        private void OnFrameCreated(object? sender, CoreWebView2FrameCreatedEventArgs args)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            args.Frame.PermissionRequested += OnPermissionRequested;
            _frames.Add(args.Frame);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _core.PermissionRequested -= OnPermissionRequested;
            _core.FrameCreated -= OnFrameCreated;
            foreach (var frame in _frames) frame.PermissionRequested -= OnPermissionRequested;
            _policy.DenyRequestsFor(_core);
        }
    }
}

public sealed record PermissionPrompt(
    Guid Id,
    string Origin,
    string Kind,
    DateTimeOffset RequestedAt);
