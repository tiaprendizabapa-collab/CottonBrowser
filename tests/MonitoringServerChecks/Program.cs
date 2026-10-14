using MonitoringServer.Admin;
using CottonBrowser.Shared;
using System.Text.Json;

var failures = new List<string>();
var checkCount = 0;
void Check(bool condition, string name)
{
    checkCount++;
    if (!condition) failures.Add(name);
}

var valid = new UpdatePolicyDraftRequest(0, "Política global",
    new UrlPolicy(["https://intranet.example/"], ["https://blocked.example/"]),
    [new ExtensionPolicy(new string('a', 32), true)],
    new DlpPolicy(true, false),
    new BrandingPolicy(null, "#315de8", []));

Check(PolicyValidation.IsValidScope("global"), "global scope");
Check(PolicyValidation.IsValidScope("department:financeiro"), "department scope");
Check(!PolicyValidation.IsValidScope("department:../ti"), "scope traversal rejected");
Check(PolicyValidation.Validate("global", valid).Count == 0, "valid draft");
Check(PolicyValidation.Validate("global", valid with
{
    Urls = new UrlPolicy(["https://same.example/"], ["https://same.example/"])
}).Count > 0, "conflicting URL lists");
Check(PolicyValidation.Validate("global", valid with
{
    Urls = new UrlPolicy(["https://same.example/"], ["https://SAME.example:443"])
}).Count > 0, "equivalent root URLs conflict");
Check(PolicyValidation.Validate("global", valid with
{
    Urls = new UrlPolicy([], ["https://blocked.example", "https://blocked.example:443/"])
}).Count > 0, "equivalent blocked URLs are duplicates");
Check(PolicyValidation.Validate("global", valid with
{
    Urls = new UrlPolicy([], ["https://user:pass@example.com/"])
}).Count > 0, "credentialed URL rejected");
Check(PolicyValidation.Validate("global", valid with
{
    Extensions = [new ExtensionPolicy("bad-id", true)]
}).Count > 0, "invalid extension rejected");
Check(PolicyValidation.Validate("global", valid with
{
    Dlp = new DlpPolicy(true, true)
}).Count > 0, "unsupported upload enforcement rejected");

var navigation = new NavigationTelemetryEvent("navigation", "user", "session",
    "https://example.com/path", "example.com", null, "Example", true, DateTimeOffset.UtcNow);
Check(NavigationTelemetryEvent.IsValid(navigation), "valid navigation event");
Check(NavigationTelemetryEvent.IsValid(navigation with { EventType = "active_tab" }), "active tab event");
Check(!NavigationTelemetryEvent.IsValid(null), "null telemetry entry rejected");
foreach (var field in new[] { "eventType", "userId", "sessionId", "url", "domain" })
{
    var payload = JsonSerializer.SerializeToNode(navigation, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    payload[field] = null;
    var malformed = payload.Deserialize<NavigationTelemetryEvent>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    Check(!NavigationTelemetryEvent.IsValid(malformed), $"null telemetry {field} rejected without exception");
}
Check(!NavigationTelemetryEvent.IsValid(navigation with { Domain = "spoofed.example" }), "spoofed report domain rejected");
Check(!NavigationTelemetryEvent.IsValid(navigation with { EventType = "unknown" }), "unknown event type rejected");
Check(!NavigationTelemetryEvent.IsValid(navigation with { SearchTerm = new string('x', 501) }), "oversized search rejected");
Check(!NavigationTelemetryEvent.IsValid(navigation with { Title = new string('x', 301) }), "oversized title rejected");
Check(!NavigationTelemetryEvent.IsValid(navigation with { Url = "https://user:secret@example.com/" }), "credentialed telemetry rejected");
Check(!NavigationTelemetryEvent.IsValid(navigation with { UserId = " " }), "blank telemetry user rejected");

var temporaryDirectory = Path.Combine(Path.GetTempPath(), "cotton-site-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporaryDirectory);
var exceptions = new SiteExceptionStore(Path.Combine(temporaryDirectory, "site-exceptions.json"));
var store = new PolicyDraftStore(exceptions);
var original = store.Get("global");
Check(original.Revision == 0, "initial revision");
Check(store.Save("global", valid, out var saved) == PolicySaveOutcome.Saved &&
      saved.Revision == 1 && saved.Dlp.BlockExeDownloads, "save draft");
Check(store.Save("global", valid, out var current) == PolicySaveOutcome.Conflict &&
      current.Revision == 1, "optimistic concurrency");
Check(store.Get("department:ti").Revision == 0, "scope isolation");
var parallelSaves = Enumerable.Range(0, 20).AsParallel()
    .Select(index => store.Save("department:concurrency", valid, out _)).ToArray();
Check(parallelSaves.Count(outcome => outcome == PolicySaveOutcome.Saved) == 1 &&
      parallelSaves.Count(outcome => outcome == PolicySaveOutcome.Conflict) == 19,
    "concurrent writers preserve optimistic revision");
var canonicalSave = valid with { Urls = new UrlPolicy([], ["https://BLOCKED.example:443"]) };
Check(store.Save("department:canonical", canonicalSave, out var canonicalDraft) == PolicySaveOutcome.Saved &&
      canonicalDraft.Urls.Blocked.Single() == "https://blocked.example/", "blocked URLs saved canonically");
Check(SiteExceptionAddress.TryNormalizeOrigin("192.168.3.205/login", out var privateOrigin) &&
      privateOrigin == "http://192.168.3.205/", "private IP normalizes to HTTP origin");
Check(SiteExceptionAddress.TryNormalizeOrigin("example.com/path", out var publicOrigin) &&
      publicOrigin == "https://example.com/", "public site normalizes to HTTPS origin");
Check(!SiteExceptionAddress.TryNormalizeOrigin("https://user:secret@example.com/", out _),
    "credentialed exception rejected");
var localRequest = valid with
{
    ExpectedRevision = saved.Revision,
    Urls = new UrlPolicy(["192.168.3.205/login"], [])
};
var localRequestValid = PolicyValidation.Validate("global", localRequest).Count == 0;
var localSaveOutcome = store.Save("global", localRequest, out var normalized);
Check(localRequestValid && localSaveOutcome == PolicySaveOutcome.Saved &&
      normalized.Urls.Allowed.Single() == privateOrigin,
    "draft accepts private IP path and stores origin");
Check(!store.TryApplySites(saved.Revision, out _), "stale revision cannot activate sites");
Check(store.TryApplySites(normalized.Revision, out var applied) &&
      applied?.AllowedOrigins.Length == 1 && exceptions.Read().AllowedOrigins.Single() == privateOrigin,
    "applied exception persists");
Check(new PolicyDraftStore(exceptions).Get("global").Urls.Allowed.Single() == privateOrigin,
    "new server draft loads active exception");
var allowlist = new SiteAllowlist(exceptions);
allowlist.Refresh();
Check(allowlist.IsAllowed(new Uri("http://192.168.3.205/login")) &&
      !allowlist.IsAllowed(new Uri("http://192.168.3.206/login")) &&
      !allowlist.IsAllowed(new Uri("https://192.168.3.205/login")), "exact origin matching");
File.WriteAllText(exceptions.PathOnDisk, "{invalid");
allowlist.Refresh();
Check(!allowlist.IsAllowed(new Uri("http://192.168.3.205/")), "corrupt policy fails closed");
File.Delete(exceptions.PathOnDisk);
Directory.Delete(temporaryDirectory);

if (failures.Count > 0)
{
    Console.Error.WriteLine("Falhas: " + string.Join(", ", failures));
    return 1;
}
Console.WriteLine($"{checkCount} verificações de políticas, exceções e telemetria passaram.");
return 0;
