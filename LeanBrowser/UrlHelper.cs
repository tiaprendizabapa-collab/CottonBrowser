using System.Globalization;
using System.Text.RegularExpressions;

namespace LeanBrowser;

/// <summary>
/// Converte o que o usuario digitou na omnibox em uma URL navegavel.
/// Regras (nesta ordem):
///   1. Operador de pesquisa                -> busca no Google.
///   2. localhost / IP, com ou sem porta    -> http://
///   3. Dominio com porta                  -> https://
///   4. Ja tem esquema explicito           -> usa como esta.
///   5. Contem ponto e nao contem espaco   -> https://
///   6. Qualquer outra coisa               -> busca no Google.
/// </summary>
public static partial class UrlHelper
{
    private const string SearchEndpoint = "https://www.google.com/search?q=";

    // Compilado em tempo de build pelo source generator (nada de Regex
    // interpretado em runtime: menos alocacao e menos CPU no primeiro uso).
    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*:", RegexOptions.CultureInvariant)]
    private static partial Regex SchemePrefix();

    [GeneratedRegex(@"^(site|filetype|ext|inurl|allinurl|intitle|allintitle|intext|allintext|related|before|after):",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SearchOperatorPrefix();

    [GeneratedRegex(@"^(localhost|127\.0\.0\.1|\[::1\]|(\d{1,3}\.){3}\d{1,3})(:\d{1,5})?([/?#].*)?$",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LocalHost();

    public static string Normalize(string input)
    {
        var text = input.Trim();

        if (text.Length == 0)
            return "about:blank";

        // Operadores como site: tambem se parecem com esquemas, mas sao buscas.
        if (SearchOperatorPrefix().IsMatch(text))
            return SearchEndpoint + Uri.EscapeDataString(text);

        // Verifica host:porta antes do esquema: localhost:3000 e exemplo.com:8443
        // satisfazem a sintaxe de um esquema URI, mas sao enderecos de sites.
        if (LocalHost().IsMatch(text))
            return "http://" + text;

        if (HasPort(text) && LooksLikeDomain(text))
            return "https://" + text;

        // Esquema explicito (https:, http:, file:, about:, mailto:, edge:...).
        // A permissao para navegar continua sendo validada pela politica do host.
        if (SchemePrefix().IsMatch(text))
            return text;

        // Protocolo relativo "//exemplo.com"
        if (text.StartsWith("//", StringComparison.Ordinal))
            return "https:" + text;

        // Parece dominio? Precisa de ponto, sem espaco, e um TLD plausivel.
        if (LooksLikeDomain(text))
            return "https://" + text;

        // Fallback: busca.
        return SearchEndpoint + Uri.EscapeDataString(text);
    }

    private static bool HasPort(string text)
    {
        var hostEnd = text.AsSpan().IndexOfAny('/', '?', '#');
        var authority = hostEnd >= 0 ? text.AsSpan(0, hostEnd) : text.AsSpan();
        var colon = authority.LastIndexOf(':');
        return colon > 0
            && int.TryParse(authority[(colon + 1)..], NumberStyles.None,
                CultureInfo.InvariantCulture, out var port)
            && port <= 65535;
    }

    private static bool LooksLikeDomain(string text)
    {
        if (text.AsSpan().ContainsAny(' ', '\t', '"'))
            return false;

        // Separa host de path/query antes de procurar o ponto, para que
        // "meusite/pagina.html" nao seja confundido com dominio.
        var hostEnd = text.AsSpan().IndexOfAny('/', '?', '#');
        var host = hostEnd >= 0 ? text[..hostEnd] : text;

        var colon = host.IndexOf(':');
        if (colon >= 0)
            host = host[..colon];

        var dot = host.LastIndexOf('.');
        if (dot <= 0 || dot == host.Length - 1)
            return false;

        // TLD: pelo menos 2 caracteres, apenas letras.
        var tld = host.AsSpan(dot + 1);
        if (tld.Length < 2)
            return false;

        foreach (var c in tld)
        {
            if (!char.IsLetter(c))
                return false;
        }

        return true;
    }

    /// <summary>Versao curta para exibir na barra (esconde "https://" e "www.").</summary>
    public static string ForDisplay(string url)
    {
        if (string.IsNullOrEmpty(url) || url == "about:blank")
            return string.Empty;

        var text = url;

        if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            text = text[8..];
        else if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            text = text[7..];

        if (text.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            text = text[4..];

        return text.Length > 1 && text.EndsWith('/') && !text.Contains('?')
            ? text[..^1]
            : text;
    }
}
