using LeanBrowser;

var addressCases = new (string Input, string Expected)[]
{
    ("", "about:blank"),
    ("   ", "about:blank"),
    ("localhost", "http://localhost"),
    ("localhost:3000", "http://localhost:3000"),
    ("  localhost:5173/app?q=cotton#inicio  ", "http://localhost:5173/app?q=cotton#inicio"),
    ("LOCALHOST:8080?teste=1", "http://LOCALHOST:8080?teste=1"),
    ("localhost#inicio", "http://localhost#inicio"),
    ("127.0.0.1:3000", "http://127.0.0.1:3000"),
    ("192.168.0.10:8080/status", "http://192.168.0.10:8080/status"),
    ("[::1]:3000", "http://[::1]:3000"),
    ("exemplo.com:8443", "https://exemplo.com:8443"),
    ("exemplo.com:8443/app?q=1#inicio", "https://exemplo.com:8443/app?q=1#inicio"),
    ("exemplo.com.br:8080?query=1", "https://exemplo.com.br:8080?query=1"),
    ("exemplo.com", "https://exemplo.com"),
    ("exemplo.com/guia", "https://exemplo.com/guia"),
    ("//cdn.exemplo.com:8443/app", "https://cdn.exemplo.com:8443/app"),
    ("https://localhost:3000", "https://localhost:3000"),
    ("http://exemplo.com:8080", "http://exemplo.com:8080"),
    ("HTTPS://exemplo.com", "HTTPS://exemplo.com"),
    ("about:blank", "about:blank"),
    ("file:///C:/teste.html", "file:///C:/teste.html"),
    ("mailto:pessoa@exemplo.com", "mailto:pessoa@exemplo.com"),
    ("tel:1234", "tel:1234"),
    ("custom+app://destino", "custom+app://destino"),
    ("edge:settings", "edge:settings"),
};

foreach (var (input, expected) in addressCases)
    Check(input, expected);

var searchCases = new[]
{
    "site:exemplo.com",
    "SITE:exemplo.com cotton",
    "filetype:pdf algodão",
    "inurl:cotton",
    "intitle:CottonBrowser",
    "before:2026-09-24",
    "after:2026-01-01",
    "como fazer pão",
    "receita bolo",
    "meusite/pagina.html",
    "exemplo.com artigo",
};

foreach (var input in searchCases)
    Check(input, "https://www.google.com/search?q=" + Uri.EscapeDataString(input));

Console.WriteLine($"PASS: {addressCases.Length + searchCases.Length} casos de enderecos, portas, esquemas e pesquisas.");

Equal(UrlHelper.Normalize("algodão", "Bing"), "https://www.bing.com/search?q=algod%C3%A3o", "pesquisa no Bing");
Equal(UrlHelper.Normalize("site:exemplo.com algodão", "DuckDuckGo"), "https://duckduckgo.com/?q=site%3Aexemplo.com%20algod%C3%A3o", "operador no DuckDuckGo");
Equal(UrlHelper.Normalize("example.com", "Bing"), "https://example.com", "buscador não muda navegação para um site");
Equal(UrlHelper.Normalize("algodão", "desconhecido"), "https://www.google.com/search?q=algod%C3%A3o", "buscador inválido recebe padrão");
Equal(OmniboxNavigation.ResolveTarget("pão", null, true, "pão caseiro", searchEngine: "Bing"),
    "https://www.bing.com/search?q=p%C3%A3o%20caseiro", "sugestão usa o buscador selecionado");

// Enter usa o texto visível, inclusive depois de fechar as sugestões com Esc.
Equal(OmniboxNavigation.ResolveTarget("receita bolo", "https://example.com", true),
    "https://www.google.com/search?q=receita%20bolo", "pesquisa sem estado de sugestões");
Equal(OmniboxNavigation.ResolveTarget("example.com/guia", "http://www.example.com/guia/", false),
    "http://www.example.com/guia/", "Enter sem editar preserva o protocolo e www");
Equal(OmniboxNavigation.ResolveTarget("example.com/guia", "http://www.example.com/guia/", true),
    "https://example.com/guia", "entrada editada é normalizada");
Equal(OmniboxNavigation.ResolveTarget("ex", null, true, "Exemplo", "http://www.example.com/"),
    "http://www.example.com/", "sugestão usa a URL original");
Equal(OmniboxNavigation.ResolveTarget("pão", null, true, "pão caseiro"),
    "https://www.google.com/search?q=p%C3%A3o%20caseiro", "pesquisa sugerida");
Equal(OmniboxNavigation.ResolveTarget("  ", "https://example.com", true), null, "entrada vazia não navega");

var pending = new TabNavigationState();
Equal(pending.Request("https://first.example"), null, "aguarda a inicialização completa");
Equal(pending.Request("https://second.example"), null, "segunda entrada continua em espera");
Equal(pending.CompleteInitialization("https://home.example"), "https://second.example",
    "a última entrada substitui a página inicial");
Equal(pending.Request("https://third.example"), "https://third.example", "guia pronta navega imediatamente");
if (!pending.IsReady || !pending.HasUserNavigation)
    throw new InvalidOperationException("A navegação enviada não deve devolver o foco à página inicial.");
var initial = new TabNavigationState();
Equal(initial.CompleteInitialization("https://home.example"), "https://home.example", "nova guia sem entrada usa página inicial");
if (initial.HasUserNavigation) throw new InvalidOperationException("A página inicial deve manter o foco na barra.");
Console.WriteLine("PASS: Enter, Esc, sugestões, URL original e navegação durante a abertura da guia.");

static void Equal(string? actual, string? expected, string scenario)
{
    if (actual != expected) throw new InvalidOperationException($"{scenario}: esperado '{expected}', obtido '{actual}'.");
}

static void Check(string input, string expected)
{
    var actual = UrlHelper.Normalize(input);
    if (!string.Equals(actual, expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"Entrada '{input}': esperado '{expected}', obtido '{actual}'.");
}
