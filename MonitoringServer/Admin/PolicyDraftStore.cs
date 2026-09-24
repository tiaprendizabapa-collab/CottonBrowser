using CottonBrowser.Shared;

namespace MonitoringServer.Admin;

public enum PolicySaveOutcome { Saved, Conflict }

// Prototype storage only. Server restart loses all drafts.
public sealed class PolicyDraftStore(SiteExceptionStore siteExceptions)
{
    private readonly Dictionary<string, BrowserPolicyDraft> _drafts = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public BrowserPolicyDraft Get(string scope)
    {
        lock (_gate) return GetOrCreate(scope);
    }

    public PolicySaveOutcome Save(string scope, UpdatePolicyDraftRequest request,
        out BrowserPolicyDraft draft)
    {
        lock (_gate)
        {
            var current = GetOrCreate(scope);
            if (current.Revision != request.ExpectedRevision)
            {
                draft = current;
                return PolicySaveOutcome.Conflict;
            }

            draft = current with
            {
                Name = request.Name.Trim(),
                Revision = current.Revision + 1,
                Urls = new UrlPolicy(request.Urls.Allowed.Select(value =>
                {
                    SiteExceptionAddress.TryNormalizeOrigin(value, out var origin);
                    return origin;
                }).ToArray(), request.Urls.Blocked.ToArray()),
                Extensions = request.Extensions.ToArray(),
                Dlp = request.Dlp,
                Branding = request.Branding with
                {
                    MandatoryBookmarks = request.Branding.MandatoryBookmarks.ToArray()
                },
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _drafts[scope] = draft;
            return PolicySaveOutcome.Saved;
        }
    }

    public bool TryApplySites(int expectedRevision, out SiteExceptionSnapshot? snapshot)
    {
        lock (_gate)
        {
            var draft = GetOrCreate("global");
            if (draft.Revision != expectedRevision)
            {
                snapshot = null;
                return false;
            }
            snapshot = siteExceptions.Write(draft.Urls.Allowed);
            return true;
        }
    }

    private BrowserPolicyDraft GetOrCreate(string scope)
    {
        if (_drafts.TryGetValue(scope, out var draft)) return draft;
        draft = new BrowserPolicyDraft(Guid.NewGuid(), scope,
            scope == "global" ? "Política global" : "Política do departamento",
            0, new UrlPolicy(scope == "global" ? siteExceptions.Read().AllowedOrigins :
                Array.Empty<string>(), Array.Empty<string>()),
            Array.Empty<ExtensionPolicy>(), new DlpPolicy(false, false),
            new BrandingPolicy(null, "#315de8", Array.Empty<MandatoryBookmark>()),
            DateTimeOffset.UtcNow);
        _drafts.Add(scope, draft);
        return draft;
    }
}
