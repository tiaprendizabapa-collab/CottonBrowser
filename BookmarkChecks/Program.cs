using LeanBrowser;
using System.Text.Json;

var directory = Path.Combine(Path.GetTempPath(), "LeanBrowser-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var path = Path.Combine(directory, "bookmarks.json");
void Check(bool value, string message) { if (!value) throw new Exception(message); }
try
{
    var store = new BookmarkStore(path);
    Check(store.Load().Count == 0, "Arquivo ausente");
    store.Add("https://example.com/login?q=1", "Título & teste");
    Check(new BookmarkStore(path).Load()[0].Title == "Título & teste", "Persistência e Unicode");
    store.Add("https://example.com/login?q=1", "Atualizado");
    Check(store.Load().Count == 1 && store.Load()[0].Title == "Atualizado", "Atualização sem duplicação");
    foreach (var url in new[] { "javascript:alert(1)", "file:///c:/test", "https://user:secret@example.com" })
    {
        try { store.Add(url, "Inválido"); throw new Exception("URL aceita indevidamente"); }
        catch (ArgumentException) { }
    }
    store.Remove("https://example.com/login?q=1");
    Check(store.Load().Count == 0, "Remoção persistida");
    File.WriteAllText(path, "{corrompido");
    try { store.Add("https://example.com", "Teste"); throw new Exception("Arquivo inválido aceito"); }
    catch (JsonException) { }
    Check(File.ReadAllText(path) == "{corrompido", "Preservar arquivo corrompido");
    File.WriteAllText(path, "[{\"Url\":\"javascript:alert(1)\",\"Title\":\"Teste\"}]");
    try { store.Load(); throw new Exception("Esquema perigoso carregado"); }
    catch (InvalidDataException) { }
    Console.WriteLine("PASS: persistência, Unicode, atualização, remoção, URLs e corrupção.");
}
finally
{
    // Remove somente o arquivo criado por esta execução, sem exclusão recursiva.
    if (File.Exists(path)) File.Delete(path);
    Directory.Delete(directory);
}
