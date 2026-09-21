using System.Text.RegularExpressions;

namespace LeanBrowser;

/// <summary>
/// Converte o que o usuario digitou na omnibox em uma URL navegavel.
/// Regras (nesta ordem):
///   1. Ja tem esquema conhecido            -> usa como esta.
///   2. localhost / IP / host:porta          -> http://
///   3. Contem ponto e nao contem espaco     -> https://
///   4. Qualquer outra coisa                 -> busca no Google.
/// </summary>
public static partial class UrlHelper
{
    private const string SearchEndpoint = "https://www.google.com/search?q=";

    // Compilado em tempo de build pelo source generator (nada de Regex
    // interpretado em runtime: menos alocacao e menos CPU no primeiro uso).
    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*:", RegexOptions.CultureInvariant)]
    private static partial Regex SchemePrefix();

    [GeneratedRegex(@"^(localhost|127\.0\.0\.1|\[::1\]|(\d{1,3}\.){3}\d{1,3})(:\d{1,5})?(/.*)?$",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LocalHost();

    public static string Normalize(string input)
    {
        var text = input.Trim();

        if (text.Length == 0)
            return "about:blank";

        // 1. Esquema explicito (https:, http:, file:, about:, mailto:, edge:...)
        if (SchemePrefix().IsMatch(text))
            return text;

        // Protocolo relativo "//exemplo.com"
        if (text.StartsWith("//", StringComparison.Ordinal))
            return "https:" + text;

        // 2. Ambiente local: http, porque quase nenhum dev server local tem TLS.
        if (LocalHost().IsMatch(text))
            return "http://" + text;

        // 3. Parece dominio? Precisa de ponto, sem espaco, e um TLD plausivel.
        if (LooksLikeDomain(text))
            return "https://" + text;

        // 4. Fallback: busca.
        return SearchEndpoint + Uri.EscapeDataString(text);
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
