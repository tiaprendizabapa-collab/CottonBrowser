using System.Reflection;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace LeanBrowser;

/// <summary>
/// Bloqueador de anuncios a nivel de rede.
///
/// PONTO CENTRAL DE PERFORMANCE
/// ----------------------------
/// A abordagem ingenua seria registrar um filtro curinga global:
///
///     core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
///
/// Isso faz TODA requisicao da pagina (200-500 por site moderno) atravessar a
/// fronteira COM/IPC ate o nosso codigo gerenciado, ser transformada em objetos
/// .NET, avaliada e devolvida. E o equivalente a colocar um pedagio em cada
/// pacote: custa CPU no processo do browser, custa CPU no nosso processo, e
/// segura a thread de UI.
///
/// Aqui fazemos o oposto: registramos filtros ESTREITOS, um par por dominio da
/// blocklist. O casamento de padrao acontece dentro do processo do motor, em
/// codigo nativo, antes de qualquer marshalling. O handler gerenciado so e
/// invocado para requisicoes que JA sao anuncio - ou seja, exatamente as que
/// vamos matar. Trafego legitimo nunca sai do motor.
///
/// Resultado pratico: ~0% de overhead de CPU no caminho quente, e o custo
/// total do bloqueio fica proporcional ao numero de anuncios bloqueados, nao
/// ao numero de requisicoes da pagina.
/// </summary>
public sealed class AdBlocker
{
    private const string EmbeddedResourceName = "LeanBrowser.Assets.blocklist.txt";

    private readonly List<string> _domains = new();
    private CoreWebView2? _core;

    /// <summary>Total de requisicoes barradas desde o inicio do processo.</summary>
    public int TotalBlocked { get; private set; }

    /// <summary>Barradas apenas na navegacao atual (resetado a cada NavigationStarting).</summary>
    public int BlockedOnCurrentPage { get; private set; }

    /// <summary>Quantidade de dominios carregados.</summary>
    public int DomainCount => _domains.Count;

    /// <summary>Liga/desliga o bloqueio sem remover os filtros nativos.</summary>
    public bool Enabled { get; set; } = true;

    public AdBlocker()
    {
        LoadDomains();
    }

    /// <summary>
    /// Caminho da lista editavel pelo usuario. Se existir, tem prioridade
    /// sobre a lista embutida no executavel.
    /// </summary>
    public static string UserListPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LeanBrowser", "blocklist.txt");

    private void LoadDomains()
    {
        string? raw = null;

        try
        {
            if (File.Exists(UserListPath))
                raw = File.ReadAllText(UserListPath, Encoding.UTF8);
        }
        catch
        {
            // Lista do usuario ilegivel -> cai para a embutida.
        }

        if (raw is null)
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(EmbeddedResourceName);

            if (stream is not null)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                raw = reader.ReadToEnd();
            }
        }

        if (string.IsNullOrEmpty(raw))
            return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in raw.Split('\n'))
        {
            var entry = line.Trim();

            if (entry.Length == 0 || entry[0] == '#')
                continue;

            // Tolera formato hosts-file ("0.0.0.0 dominio.com").
            var space = entry.LastIndexOf(' ');
            if (space >= 0)
                entry = entry[(space + 1)..].Trim();

            if (entry.Length == 0 || !entry.Contains('.'))
                continue;

            if (seen.Add(entry))
                _domains.Add(entry.ToLowerInvariant());
        }
    }

    /// <summary>
    /// Registra os filtros nativos e conecta o handler. Deve ser chamado uma
    /// unica vez, logo apos o CoreWebView2 ficar pronto.
    /// </summary>
    public void Attach(CoreWebView2 core)
    {
        _core = core;

        foreach (var domain in _domains)
        {
            // Dois padroes por dominio: o apex e qualquer subdominio.
            // O casamento roda em codigo nativo, dentro do processo do motor.
            TryAddFilter($"*://{domain}/*");
            TryAddFilter($"*://*.{domain}/*");
        }

        core.WebResourceRequested += OnWebResourceRequested;
    }

    private void TryAddFilter(string pattern)
    {
        try
        {
            _core!.AddWebResourceRequestedFilter(
                pattern,
                CoreWebView2WebResourceContext.All);
        }
        catch
        {
            // Padrao rejeitado pelo runtime: ignora esse dominio em vez de
            // derrubar o app inteiro.
        }
    }

    /// <summary>
    /// So e chamado para requisicoes que ja casaram com um padrao de anuncio.
    /// Por isso o corpo e minusculo: nao ha decisao a tomar, so responder.
    /// </summary>
    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!Enabled || _core is null)
            return; // Deixa seguir para a rede.

        // Resposta sintetica vazia. 403 + CORS liberado evita que scripts de
        // terceiros disparem excecoes ruidosas que gerariam re-tentativas
        // (e, ironicamente, mais CPU).
        e.Response = _core.Environment.CreateWebResourceResponse(
            null,                              // corpo vazio
            403,
            "Blocked by CottonBrowser",
            "Access-Control-Allow-Origin: *");

        TotalBlocked++;
        BlockedOnCurrentPage++;

        // Nota deliberada: NAO atualizamos a UI aqui. Tocar em controles a cada
        // bloqueio causaria dezenas de invalidacoes/repaints por pagina. O
        // contador e lido uma vez, em NavigationCompleted.
    }

    public void ResetPageCounter() => BlockedOnCurrentPage = 0;

    /// <summary>Grava a lista embutida em disco para o usuario editar.</summary>
    public static void ExportDefaultList()
    {
        var dir = Path.GetDirectoryName(UserListPath)!;
        Directory.CreateDirectory(dir);

        if (File.Exists(UserListPath))
            return;

        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(EmbeddedResourceName);

        if (stream is null)
            return;

        using var file = File.Create(UserListPath);
        stream.CopyTo(file);
    }
}
