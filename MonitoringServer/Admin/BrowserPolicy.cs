using System.Text.RegularExpressions;
using CottonBrowser.Shared;

namespace MonitoringServer.Admin;

// Draft contracts. Only global allowed origins can be activated locally today;
// blocked URLs, extension choices, DLP and branding are not enforced.
public sealed record UrlPolicy(IReadOnlyList<string> Allowed, IReadOnlyList<string> Blocked);
public sealed record ExtensionPolicy(string Id, bool Enabled);
public sealed record DlpPolicy(bool BlockExeDownloads, bool BlockUnapprovedUploads);
public sealed record MandatoryBookmark(string Title, string Url);
public sealed record BrandingPolicy(string? LogoUrl, string PrimaryColor,
    IReadOnlyList<MandatoryBookmark> MandatoryBookmarks);

public sealed record BrowserPolicyDraft(Guid Id, string Scope, string Name, int Revision,
    UrlPolicy Urls, IReadOnlyList<ExtensionPolicy> Extensions, DlpPolicy Dlp,
    BrandingPolicy Branding, DateTimeOffset UpdatedAt);

public sealed record UpdatePolicyDraftRequest(int ExpectedRevision, string Name,
    UrlPolicy Urls, IReadOnlyList<ExtensionPolicy> Extensions, DlpPolicy Dlp,
    BrandingPolicy Branding);

public sealed record ApplySiteExceptionsRequest(int ExpectedRevision);

public static partial class PolicyValidation
{
    [GeneratedRegex("^[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DepartmentIdPattern();

    [GeneratedRegex("^[a-p]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionIdPattern();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();

    public static bool IsValidScope(string scope) => scope == "global" ||
        (scope.StartsWith("department:", StringComparison.Ordinal) &&
         DepartmentIdPattern().IsMatch(scope["department:".Length..]));

    public static IReadOnlyList<string> Validate(string scope, UpdatePolicyDraftRequest? request)
    {
        var errors = new List<string>();
        if (!IsValidScope(scope)) errors.Add("Escopo inválido.");
        if (request is null) { errors.Add("Rascunho ausente."); return errors; }
        if (request.ExpectedRevision < 0) errors.Add("Revisão inválida.");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 80)
            errors.Add("O nome deve conter de 1 a 80 caracteres.");

        if (request.Urls is null || request.Urls.Allowed is null || request.Urls.Blocked is null)
            errors.Add("As listas de URLs são obrigatórias.");
        else
        {
            CheckAllowedOrigins(request.Urls.Allowed, errors);
            CheckUrls(request.Urls.Blocked, "bloqueadas", errors);
            if (request.Urls.Allowed.Select(value => SiteExceptionAddress.TryNormalizeOrigin(value, out var origin) ? origin : value)
                .Intersect(request.Urls.Blocked, StringComparer.OrdinalIgnoreCase).Any())
                errors.Add("Uma URL não pode estar nas duas listas.");
        }

        if (request.Extensions is null || request.Extensions.Count > 100)
            errors.Add("Informe no máximo 100 extensões.");
        else
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var extension in request.Extensions)
            {
                var id = extension?.Id;
                if (id is null || !ExtensionIdPattern().IsMatch(id) || !ids.Add(id))
                    errors.Add("ID de extensão inválido ou duplicado.");
            }
        }

        if (request.Dlp is null) errors.Add("Configuração DLP obrigatória.");
        else if (request.Dlp.BlockUnapprovedUploads)
            errors.Add("Bloqueio de uploads ainda não possui implementação no navegador.");

        if (request.Branding is null || !ColorPattern().IsMatch(request.Branding.PrimaryColor ?? "") ||
            request.Branding.MandatoryBookmarks is null || request.Branding.MandatoryBookmarks.Count > 50)
            errors.Add("Configuração de marca inválida.");
        else
        {
            if (request.Branding.LogoUrl is not null && !IsWebUrl(request.Branding.LogoUrl))
                errors.Add("URL do logotipo inválida.");
            foreach (var bookmark in request.Branding.MandatoryBookmarks)
                if (bookmark is null || string.IsNullOrWhiteSpace(bookmark.Title) ||
                    bookmark.Title.Length > 100 || !IsWebUrl(bookmark.Url))
                    errors.Add("Favorito obrigatório inválido.");
        }
        return errors.Distinct().ToArray();
    }

    private static void CheckUrls(IReadOnlyList<string> urls, string label, List<string> errors)
    {
        if (urls.Count > 200) { errors.Add($"Informe no máximo 200 URLs {label}."); return; }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in urls)
            if (!IsWebUrl(url) || !seen.Add(url)) errors.Add($"URL {label} inválida ou duplicada.");
    }

    private static void CheckAllowedOrigins(IReadOnlyList<string> urls, List<string> errors)
    {
        if (urls.Count > 200) { errors.Add("Informe no máximo 200 sites permitidos."); return; }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in urls)
            if (!SiteExceptionAddress.TryNormalizeOrigin(url, out var origin) || !seen.Add(origin))
                errors.Add("Site permitido inválido ou duplicado.");
    }

    private static bool IsWebUrl(string? value) => value is { Length: > 0 and <= 2048 } &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
        !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}
