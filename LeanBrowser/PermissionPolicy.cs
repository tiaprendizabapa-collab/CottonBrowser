using System.Collections.Concurrent;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LeanBrowser;

/// <summary>
/// Default-deny permission gate. Camera, microphone, location, and browser
/// notifications are granted only once after an explicit decision in the
/// trusted browser UI. No decision is written into the WebView2 profile.
/// </summary>
public sealed class PermissionPolicy : IDisposable
{
    private static readonly TimeSpan ConsentTimeout = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<Guid, PendingPermission> _pending = new();
    private readonly ConcurrentDictionary<string, Guid> _pendingByOriginAndKind = new(StringComparer.Ordinal);
    private readonly object _resetLock = new();
    private Task? _resetPersistedPermissions;
    private int _disposed;

    public event Action<PermissionPrompt>? PromptCreated;

    public IDisposable Attach(WebView2 control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var core = control.CoreWebView2
            ?? throw new InvalidOperationException("CoreWebView2 must be initialized before attaching the permission policy.");
        return new Subscription(this, core);
    }

    /// <summary>Removes legacy persisted grants before any tab is allowed to navigate.</summary>
    public Task ResetPersistedPermissionsAsync(CoreWebView2Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (_resetLock)
            return _resetPersistedPermissions ??= ResetPersistedPermissionsCoreAsync(profile);
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
            pending.Decision.TrySetResult(false);
    }

    private static async Task ResetPersistedPermissionsCoreAsync(CoreWebView2Profile profile)
    {
        var settings = await profile.GetNonDefaultPermissionSettingsAsync();
        foreach (var setting in settings.Where(setting => IsConsentEligible(setting.PermissionKind)))
        {
            await profile.SetPermissionStateAsync(
                setting.PermissionKind,
                setting.PermissionOrigin,
                CoreWebView2PermissionState.Default);
        }
    }

    private void HandlePermissionRequest(CoreWebView2 core, CoreWebView2PermissionRequestedEventArgs args)
    {
        args.Handled = true;
        args.SavesInProfile = false;
        args.State = CoreWebView2PermissionState.Deny;

        if (Volatile.Read(ref _disposed) != 0
            || !IsConsentEligible(args.PermissionKind)
            || !args.IsUserInitiated
            || !TryGetSecureOrigin(args.Uri, out var origin))
            return;

        var key = origin + "|" + args.PermissionKind;
        if (_pendingByOriginAndKind.ContainsKey(key)) return;

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
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));

        if (!_pending.TryAdd(requestId, pending)
            || !_pendingByOriginAndKind.TryAdd(key, requestId))
        {
            _pending.TryRemove(requestId, out _);
            return;
        }

        var deferral = args.GetDeferral();
        _ = CompleteWhenResolvedAsync(args, deferral, pending);

        try
        {
            PromptCreated?.Invoke(prompt);
        }
        catch
        {
            pending.Decision.TrySetResult(false);
        }
    }

    private async Task CompleteWhenResolvedAsync(
        CoreWebView2PermissionRequestedEventArgs args,
        CoreWebView2Deferral deferral,
        PendingPermission pending)
    {
        try
        {
            var allow = await pending.Decision.Task.WaitAsync(ConsentTimeout);
            args.State = allow ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
        }
        catch (TimeoutException)
        {
            args.State = CoreWebView2PermissionState.Deny;
        }
        finally
        {
            args.Handled = true;
            args.SavesInProfile = false;
            _pending.TryRemove(pending.Prompt.Id, out _);
            _pendingByOriginAndKind.TryRemove(pending.Key, out _);
            deferral.Complete();
        }
    }

    private void DenyRequestsFor(CoreWebView2 core)
    {
        foreach (var pending in _pending.Values.Where(pending => ReferenceEquals(pending.Core, core)))
            pending.Decision.TrySetResult(false);
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
        TaskCompletionSource<bool> Decision);

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
