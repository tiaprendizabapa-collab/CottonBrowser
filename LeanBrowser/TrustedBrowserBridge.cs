using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LeanBrowser;

/// <summary>
/// The only IPC surface exposed to a WebView. It is available solely to the
/// bundled, top-level pages at https://app.cottonbrowser.test/; Internet pages
/// never receive a host object, a generic RPC method, or OS capabilities.
/// </summary>
public static class TrustedBrowserBridge
{
    public const string HostName = "app.cottonbrowser.test";
    public const string UiUrl = "https://app.cottonbrowser.test/index.html";
    public const string NewTabUrl = "https://app.cottonbrowser.test/newtab.html";
    private const int ProtocolVersion = 1;
    private const int MaxMessageLength = 4 * 1024;
    private const int MaxUrlLength = 2 * 1024;
    private const int MaxTitleLength = 256;
    private const int ReplayCacheCapacity = 256;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Attach(WebView2 control, TrustedBrowserBridgeHandlers handlers)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(handlers);

        var core = control.CoreWebView2
            ?? throw new InvalidOperationException("CoreWebView2 must be initialized before attaching the bridge.");
        var assetDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Bridge");
        if (!Directory.Exists(assetDirectory))
            throw new DirectoryNotFoundException("The trusted browser UI was not deployed with the application.");

        core.SetVirtualHostNameToFolderMapping(
            HostName,
            assetDirectory,
            CoreWebView2HostResourceAccessKind.DenyCors);

        var session = new BridgeSession();
        core.NavigationStarting += (_, args) =>
        {
            core.Settings.IsWebMessageEnabled = IsTrustedUiUri(args.Uri);
        };
        core.WebMessageReceived += async (_, args) =>
        {
            await HandleMessageAsync(core, session, handlers, args);
        };
    }

    private static async Task HandleMessageAsync(
        CoreWebView2 core,
        BridgeSession session,
        TrustedBrowserBridgeHandlers handlers,
        CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!IsTrustedUiUri(args.Source) || !IsTrustedUiUri(core.Source)) return;

        var message = args.WebMessageAsJson;
        if (!TryParseRequest(message, out var request)) return;

        if (!session.TryAccept(request.Id))
        {
            Respond(core, request.Id, false, "duplicate-request");
            return;
        }

        switch (request.Command)
        {
            case "open-tab":
                if (!TryGetHttpsUrl(request.Payload, out var target))
                {
                    Respond(core, request.Id, false, "invalid-url");
                    return;
                }

                try
                {
                    Respond(core, request.Id, await handlers.OpenTabAsync(target), "open-tab");
                }
                catch
                {
                    Respond(core, request.Id, false, "operation-failed");
                }
                return;

            case "save-bookmark":
                if (!TryGetBookmark(request.Payload, out var url, out var title))
                {
                    Respond(core, request.Id, false, "invalid-bookmark");
                    return;
                }

                try
                {
                    Respond(core, request.Id, handlers.SaveBookmark(url, title), "save-bookmark");
                }
                catch
                {
                    Respond(core, request.Id, false, "operation-failed");
                }
                return;

            case "list-pending-permissions":
                if (!HasOnlyProperties(request.Payload))
                {
                    Respond(core, request.Id, false, "invalid-payload");
                    return;
                }
                Respond(core, request.Id, true, "pending-permissions", handlers.GetPendingPermissions());
                return;

            case "resolve-permission":
                if (!TryGetPermissionDecision(request.Payload, out var permissionId, out var allow))
                {
                    Respond(core, request.Id, false, "invalid-permission-decision");
                    return;
                }
                Respond(core, request.Id, handlers.ResolvePermission(permissionId, allow), "resolve-permission");
                return;

            default:
                Respond(core, request.Id, false, "unsupported-command");
                return;
        }
    }

    private static bool TryParseRequest(string message, out BridgeRequest request)
    {
        request = default;
        if (string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageLength) return false;

        try
        {
            using var document = JsonDocument.Parse(message, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyProperties(root, "version", "id", "command", "payload"))
                return false;
            if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var versionNumber) || versionNumber != ProtocolVersion)
                return false;
            if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                || !Guid.TryParseExact(id.GetString(), "D", out var requestId))
                return false;
            if (!root.TryGetProperty("command", out var command) || command.ValueKind != JsonValueKind.String)
                return false;
            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                return false;

            var commandName = command.GetString();
            if (string.IsNullOrEmpty(commandName) || commandName.Length > 32) return false;
            request = new BridgeRequest(requestId, commandName, payload.Clone());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetHttpsUrl(JsonElement payload, out string url)
    {
        url = string.Empty;
        if (!HasOnlyProperties(payload, "url")
            || !payload.TryGetProperty("url", out var urlElement)
            || urlElement.ValueKind != JsonValueKind.String)
            return false;

        return TryNormalizeHttpsUrl(urlElement.GetString(), out url);
    }

    private static bool TryGetBookmark(JsonElement payload, out string url, out string title)
    {
        url = string.Empty;
        title = string.Empty;
        if (!HasOnlyProperties(payload, "url", "title")
            || !payload.TryGetProperty("url", out var urlElement)
            || !payload.TryGetProperty("title", out var titleElement)
            || urlElement.ValueKind != JsonValueKind.String
            || titleElement.ValueKind != JsonValueKind.String
            || !TryNormalizeHttpsUrl(urlElement.GetString(), out url))
            return false;

        title = titleElement.GetString()?.Trim() ?? string.Empty;
        return title.Length is > 0 and <= MaxTitleLength && title.All(character => !char.IsControl(character));
    }

    private static bool TryNormalizeHttpsUrl(string? input, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(input) || input.Length > MaxUrlLength
            || !Uri.TryCreate(input, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !BookmarkStore.IsWebUrl(uri.AbsoluteUri))
            return false;

        url = uri.AbsoluteUri;
        return true;
    }

    private static bool TryGetPermissionDecision(JsonElement payload, out Guid permissionId, out bool allow)
    {
        permissionId = Guid.Empty;
        allow = false;
        if (!HasOnlyProperties(payload, "id", "allow")
            || !payload.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.String
            || !Guid.TryParseExact(id.GetString(), "D", out permissionId)
            || !payload.TryGetProperty("allow", out var decision)
            || decision.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;

        allow = decision.GetBoolean();
        return true;
    }

    public static bool PublishPermissionPrompt(CoreWebView2? core, PermissionPrompt prompt)
    {
        if (core is null || !IsTrustedUiUri(core.Source)) return false;
        var message = new BridgeEvent(ProtocolVersion, "permission-requested", prompt);
        core.PostWebMessageAsJson(JsonSerializer.Serialize(message, JsonOptions));
        return true;
    }

    private static void Respond(CoreWebView2 core, Guid requestId, bool ok, string code, object? data = null)
    {
        if (!IsTrustedUiUri(core.Source)) return;
        var response = new BridgeResponse(ProtocolVersion, requestId.ToString("D"), ok, code, data);
        core.PostWebMessageAsJson(JsonSerializer.Serialize(response, JsonOptions));
    }

    private static bool IsTrustedUiUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.Host, HostName, StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort;

    private static bool HasOnlyProperties(JsonElement element, params string[] allowedNames)
    {
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowedNames.Contains(property.Name, StringComparer.Ordinal) || !seenNames.Add(property.Name))
                return false;
        }
        return true;
    }

    private readonly record struct BridgeRequest(Guid Id, string Command, JsonElement Payload);
    private sealed record BridgeResponse(int Version, string Id, bool Ok, string Code, object? Data);
    private sealed record BridgeEvent(int Version, string Event, object Data);

    private sealed class BridgeSession
    {
        private readonly object _lock = new();
        private readonly HashSet<Guid> _seen = new();
        private readonly Queue<Guid> _order = new();

        public bool TryAccept(Guid id)
        {
            lock (_lock)
            {
                if (!_seen.Add(id)) return false;
                _order.Enqueue(id);
                if (_order.Count > ReplayCacheCapacity) _seen.Remove(_order.Dequeue());
                return true;
            }
        }
    }
}

public sealed record TrustedBrowserBridgeHandlers(
    Func<string, Task<bool>> OpenTabAsync,
    Func<string, string, bool> SaveBookmark,
    Func<IReadOnlyList<PermissionPrompt>> GetPendingPermissions,
    Func<Guid, bool, bool> ResolvePermission);
