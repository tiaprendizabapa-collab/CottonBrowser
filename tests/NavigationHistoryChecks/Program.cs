using LeanBrowser;
using System.Text.Json;

var directory = Path.Combine(Path.GetTempPath(), "CottonHistoryChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var path = Path.Combine(directory, "history.json");
    var now = DateTimeOffset.UtcNow;
    var legacy = Enumerable.Range(0, 10100).Select(index => new NavigationHistoryEntry(
        "https://history.example/" + index, index == 0 ? "Algodão e café" : "Página " + index, now.AddMinutes(-index))).ToArray();
    File.WriteAllText(path, JsonSerializer.Serialize(legacy));
    var history = new NavigationHistoryStore(path);
    await history.Ready;
    Check(history.Search().Count == 10000, "O histórico antigo é carregado até o novo limite.");
    Check(history.Search("algodao").Count == 1, "A pesquisa de título ignora acentos.");
    Check(history.Search("history.example/20").Count > 0, "A pesquisa localiza endereços.");
    Check(history.Search()[0].VisitedAt == legacy[0].VisitedAt, "A migração preserva a data da visita.");
    history.Record("https://history.example/0", "Visita repetida");
    await history.FlushAsync();
    Check(history.Search("https://history.example/0").Count == 2, "Visitas repetidas preservam seu próprio horário.");
    var selected = history.Search()[0];
    history.RemoveEntries(new[] { selected });
    await history.FlushAsync();
    Check(history.Search("https://history.example/0").Count == 1, "Apagar uma visita não apaga as outras do mesmo endereço.");
    Check(history.Find("history.example").Count == 4, "Sugestões continuam limitadas a quatro endereços.");

    var loadedAgain = new NavigationHistoryStore(path);
    await loadedAgain.Ready;
    Check(loadedAgain.Search().SequenceEqual(history.Search()), "A gravação mantém o histórico e suas datas.");
    loadedAgain.RemoveRange(now.AddMinutes(-10), now.AddMinutes(-2));
    await loadedAgain.FlushAsync();
    Check(!loadedAgain.Search().Any(entry => entry.VisitedAt >= now.AddMinutes(-10) && entry.VisitedAt < now.AddMinutes(-2)), "A exclusão por período usa as fronteiras corretas.");
    Check(loadedAgain.Search().Any(entry => entry.VisitedAt == now.AddMinutes(-2)), "O fim do intervalo é exclusivo.");

    // A leitura ocorre em outra thread. Estas mutações devem valer mesmo se ela ainda estiver pendente.
    File.WriteAllText(path, JsonSerializer.Serialize(legacy));
    var concurrent = new NavigationHistoryStore(path);
    concurrent.RemoveRange(null, null);
    concurrent.Record("https://fresh.example/", "Após apagar");
    await concurrent.FlushAsync();
    Check(concurrent.Search().Count == 1 && concurrent.Search()[0].Url == "https://fresh.example/", "Apagar durante o carregamento não ressuscita visitas antigas.");
    var concurrentAgain = new NavigationHistoryStore(path);
    await concurrentAgain.Ready;
    Check(concurrentAgain.Search().Count == 1, "A exclusão concorrente também é persistida.");
    concurrentAgain.Record("about:blank", "Privada");
    concurrentAgain.Record("https://user:password@secret.example", "Credenciais");
    Check(concurrentAgain.Search().Count == 1, "Endereços internos e com credenciais não entram no histórico.");

    File.WriteAllText(path, "{ inválido");
    var corrupted = new NavigationHistoryStore(path);
    await corrupted.Ready;
    Check(corrupted.Search().Count == 0, "Um histórico corrompido não impede a abertura.");
    Check(Directory.GetFiles(directory, "history.json.corrupt-*.json").Length == 1, "O arquivo corrompido é preservado antes de iniciar outro histórico.");
    corrupted.Record("https://recovered.example", "Recuperado");
    await corrupted.FlushAsync();

    var preferencesPath = Path.Combine(directory, "preferences.json");
    var preferences = BrowserPreferences.Load(preferencesPath);
    Check(preferences.RestoreSession && preferences.MemorySaverEnabled && preferences.SuspendAfterMinutes == 10
        && preferences.SearchEngine == "Google" && preferences.MemorySaverExceptions.Length == 0, "As preferências iniciais são previsíveis.");
    preferences.RestoreSession = false;
    preferences.MemorySaverEnabled = false;
    preferences.SuspendAfterMinutes = 999;
    preferences.SearchEngine = "DuckDuckGo";
    preferences.MemorySaverExceptions = new[] { "HTTPS://EXAMPLE.COM/page", "example.com", "calls.example.org", "bad host" };
    Check(preferences.Save(), "Preferências são salvas atomicamente.");
    var reopened = BrowserPreferences.Load(preferencesPath);
    Check(!reopened.RestoreSession && !reopened.MemorySaverEnabled && reopened.SuspendAfterMinutes == 240
        && reopened.SearchEngine == "DuckDuckGo", "As opções são preservadas ao reabrir.");
    Check(reopened.MemorySaverExceptions.SequenceEqual(new[] { "example.com", "calls.example.org" }), "Exceções normalizam URLs, removem repetidas e valores inválidos.");
    Check(reopened.IsMemorySaverException("https://sub.example.com/a") && !reopened.IsMemorySaverException("https://badexample.com"), "Exceções incluem somente subdomínios reais.");
    File.WriteAllText(preferencesPath, "{\"SearchEngine\":\"invalid\",\"SuspendAfterMinutes\":0}");
    var repaired = BrowserPreferences.Load(preferencesPath);
    Check(repaired.SearchEngine == "Google" && repaired.SuspendAfterMinutes == 1 && repaired.RestoreSession, "Valores inválidos e campos antigos recebem padrões válidos.");
    Console.WriteLine("PASS: histórico de 10.000 visitas, migração, pesquisa, datas, repetição, exclusão seletiva e por período, concorrência e preferências persistidas.");
}
finally
{
    // Somente o diretório isolado deste teste, criado acima, é removido.
    Directory.Delete(directory, recursive: true);
}

static void Check(bool result, string scenario)
{
    if (!result) throw new InvalidOperationException(scenario);
}
