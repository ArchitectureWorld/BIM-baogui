using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Hifc.Web.Server;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Net;
using System.Security.Cryptography;
using System.Text;

internal static class EndpointCases
{
    internal static async Task Run(NpgsqlDataSource source, string databaseConnectionString, Func<string, Func<Task>, Task> test)
    {
        var fixture = Path.Combine(Path.GetTempPath(), "HifcEndpointTests", Guid.NewGuid().ToString("N")); var downloads = Path.Combine(fixture, "downloads"); Directory.CreateDirectory(downloads);
        var archive = Encoding.UTF8.GetBytes("Synthetic endpoint download fixture"); File.WriteAllBytes(Path.Combine(downloads, "HIFC-0.5.0-Web.zip"), archive);
        File.WriteAllText(Path.Combine(downloads, "package.json"), JsonWire.Serialize(new ClientPackageInfo { Version = "0.5.0-Web", AddInId = UpdateTrust.AddInId, ReleaseId = "0.5.0-dev-20261019T120000Z", SourceCommit = new string('a', 40) }));
        var app = ServerApplication.Create(new[] { "--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "None" }, new WebRuntimeOptions { DatabaseConnectionString = databaseConnectionString, SigningKeyPath = Path.Combine(fixture, "signing.pem"), ClientUpdateDirectory = downloads, EnableAccounts = true });
        await app.StartAsync(); var address = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single());
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = address };
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        async Task<AccountSession> Register()
        {
            var registration = new Registration { Phone = "139" + Random.Shared.NextInt64(10000000, 100000000), RealName = "HTTP流程验收" };
            var reply = await http.PostAsync("api/v1/accounts/register", new StringContent(JsonWire.Serialize(registration), Encoding.UTF8, "application/json"));
            Require(reply.IsSuccessStatusCode, "Registration endpoint is unavailable: " + (int)reply.StatusCode);
            return JsonWire.Deserialize<AccountSession>(await reply.Content.ReadAsStringAsync());
        }
        try
        {
            await test("zetta publishes a signed update manifest and its exact package through the actual HTTP server", async () => {
                using var reply = await http.GetAsync("updates/latest"); Require(reply.IsSuccessStatusCode, "Update manifest endpoint is unavailable.");
                var update = JsonWire.Deserialize<ClientUpdateManifest>(await reply.Content.ReadAsStringAsync());
                using var compatibility = await http.GetAsync("api/v1/compatibility"); var key = JsonWire.Deserialize<Compatibility>(await compatibility.Content.ReadAsStringAsync()).SigningKey;
                UpdateTrust.Validate(update, key); Require(update.PackageUrl.StartsWith(UpdateTrust.Server, StringComparison.Ordinal), "Update source points away from zetta.");
                using var package = await http.GetAsync(new Uri(update.PackageUrl).AbsolutePath);
                Require(package.IsSuccessStatusCode && (await package.Content.ReadAsByteArrayAsync()).SequenceEqual(archive), "Update download differs from the signed manifest.");
            });
            await test("phone and real name register once and log in from another computer without a binding credential", async () => {
                var pair = new { Phone = "139" + Random.Shared.NextInt64(10000000, 100000000), RealName = "内网人员登录验收" };
                async Task<HttpResponseMessage> SignIn(string path, object value) => await http.PostAsync(path, new StringContent(JsonWire.Serialize(value), Encoding.UTF8, "application/json"));
                using var first = await SignIn("api/v1/accounts/register", pair);
                Require(first.IsSuccessStatusCode, "Phone/name registration still requires a computer binding credential.");
                var original = JsonWire.Deserialize<AccountSession>(await first.Content.ReadAsStringAsync());
                using var next = await SignIn("api/v1/sessions", pair); Require(next.IsSuccessStatusCode, "Another computer cannot log in with the same person.");
                var other = JsonWire.Deserialize<AccountSession>(await next.Content.ReadAsStringAsync());
                Require(other.Account.Id == original.Account.Id && other.Account.ExpiresAtUtc == original.Account.ExpiresAtUtc && other.AccessToken != original.AccessToken, "Cross-computer login reset the person, deadline or session.");
                using var wrong = await SignIn("api/v1/accounts/register", new { pair.Phone, RealName = "姓名不一致验收" });
                Require(!wrong.IsSuccessStatusCode, "Repeated registration overwrote another person's name.");
                using var retired = await SignIn("api/v1/accounts/rebind", new { pair.Phone, Code = "retired" });
                Require(retired.StatusCode == HttpStatusCode.NotFound, "The removed rebinding endpoint remains available.");
            });
            await test("enabled deployment health reports the active authentication mode", async () => {
                foreach (var path in new[] { "health/live", "health/ready" }) {
                    using var reply = await http.GetAsync(path);
                    var payload = System.Text.Json.JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
                    Require(reply.IsSuccessStatusCode, path + " returned HTTP " + (int)reply.StatusCode);
                    Require(payload.RootElement.GetProperty("authenticationConfigured").GetBoolean(), "Health metadata still reports the closed foundation mode.");
                }
            });
            await test("enabled model endpoints require a real bearer session", async () => {
                using var reply = await http.GetAsync("api/v1/me"); Require(reply.StatusCode == HttpStatusCode.Unauthorized, "Anonymous access was not denied.");
            });
            await test("actual Web HTTP client completes registration check preview permit and receipt against PostgreSQL", async () => {
                var session = await Register(); using var client = new WebApiClient(address); client.SetSession(session);
                var compatibility = await client.CompatibilityAsync(CancellationToken.None); var me = await client.MeAsync(CancellationToken.None);
                Require(me.Id == session.Account.Id && me.IsActive && !me.IsAdmin, "Registration created an unusable account or administrator.");
                var rows = new[] { new ElementRecord { Id = 1, UniqueId = "http-element", Name = "规划净用地", Entity = "IfcSlab", DeclaredSlot = "规划净用地", ProjectedAreaM2 = 91.2 } };
                var context = new RequestContext { RuleVersion = compatibility.RuleVersion, RequestId = Guid.NewGuid().ToString("N"), DocumentSessionId = "http-doc", ScanId = Guid.NewGuid().ToString("N"), SnapshotHash = Canonical.InventoryHash(rows) };
                var capture = await client.StartCheckAsync(new InventoryPage { Context = context, Stage = "报规", ModelKind = "总图", PageCount = 1, TotalElements = 1, Elements = rows, PageHash = Canonical.InventoryHash(rows) }, CancellationToken.None);
                var values = capture.Targets.GroupBy(t => t.OwnerUniqueId + "|" + t.Origin + "|" + t.Source).Select(g => g.First()).Select(t => new ParameterValue { OwnerId = t.OwnerId, OwnerUniqueId = t.OwnerUniqueId, Source = t.Source, Origin = t.Origin }).ToArray();
                await client.CaptureAsync(new CapturePage { Context = context, PageCount = 1, TotalValues = values.Length, Values = values, PageHash = Canonical.ValuesHash(values) }, CancellationToken.None);
                var check = await client.CompleteAsync(context, CancellationToken.None); Require(check.Complete && check.Fields.Any(f => f.SuggestedValue == "91.2"), "HTTP workflow did not run real rules.");
                var plan = await client.PreviewAsync(new PreviewRequest { Context = context }, CancellationToken.None);
                var permit = await client.AuthorizeAsync(plan, "one-click", CancellationToken.None); Require(permit.PlanId == plan.Id, "HTTP write authorization is missing.");
                await client.ReceiptAsync(new WriteReceipt { PlanId = plan.Id, PermitId = permit.Id, DocumentSessionId = context.DocumentSessionId, TransactionCommitted = true, ReadbackPassed = true, CompletedUtc = DateTime.UtcNow }, CancellationToken.None);
            });
            await test("ordinary bearer credentials cannot use administrator operations", async () => {
                var session = await Register(); using var request = new HttpRequestMessage(HttpMethod.Post, "admin/accounts/" + session.Account.Id + "/extend");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken); request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                using var reply = await http.SendAsync(request); Require(reply.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, "Ordinary bearer entered administrator operations.");
            });
            await test("administrator confirmation and signed CSRF protect deadline changes and account deletion", async () => {
                var identities = new IdentityStore(source); var administrator = await identities.CreateAdministratorAsync("139" + Random.Shared.NextInt64(10000000, 100000000), "后台页面验收", "Synthetic-Portal-Password-2026");
                try {
                    var cookies = new Dictionary<string, string>();
                    async Task<HttpResponseMessage> Send(HttpMethod method, string path, Dictionary<string, string> form = null) {
                        var request = new HttpRequestMessage(method, path); if (cookies.Count > 0) request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies.Select(p => p.Key + "=" + p.Value)));
                        if (form != null) request.Content = new FormUrlEncodedContent(form);
                        var response = await http.SendAsync(request); if (response.Headers.TryGetValues("Set-Cookie", out var set)) foreach (var cookie in set) { var pair = cookie.Split(';')[0].Split(new[] { '=' }, 2); cookies[pair[0]] = pair[1]; }
                        return response;
                    }
                    static string Csrf(string html) => System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html, "name=\"csrf\" value=\"([^\"]+)\"").Groups[1].Value);
                    using var login = await Send(HttpMethod.Get, "admin/login"); Require(login.StatusCode == HttpStatusCode.OK, "Administrator login page is unavailable.");
                    var token = Csrf(await login.Content.ReadAsStringAsync()); Require(token.Length > 20, "Login lacks CSRF token.");
                    using var signedIn = await Send(HttpMethod.Post, "admin/login", new Dictionary<string, string> { ["phone"] = administrator.Phone, ["password"] = "Synthetic-Portal-Password-2026", ["csrf"] = token });
                    Require(signedIn.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther, "Administrator password did not establish a cookie session.");
                    using var dashboard = await Send(HttpMethod.Get, "admin"); token = Csrf(await dashboard.Content.ReadAsStringAsync()); Require(dashboard.StatusCode == HttpStatusCode.OK && token.Length > 20, "Administrator dashboard is unavailable.");
                    var member = await Register(); var path = "admin/accounts/" + member.Account.Id + "/extend";
                    using var forged = await Send(HttpMethod.Post, path, new Dictionary<string, string> { ["days"] = "2", ["version"] = "1" }); Require(forged.StatusCode == HttpStatusCode.BadRequest, "A missing CSRF token changed an account.");
                    using var extended = await Send(HttpMethod.Post, path, new Dictionary<string, string> { ["days"] = "2", ["version"] = "1", ["csrf"] = token });
                    Require(extended.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther, "A valid administrator form was rejected.");
                    Require((await new AccountStore(source).GetAsync(Guid.Parse(member.Account.Id))).ExpiresAtUtc == member.Account.ExpiresAtUtc.AddDays(2), "Browser form did not persist the intended extension.");
                    path = "admin/accounts/" + member.Account.Id + "/delete";
                    using var confirmation = await Send(HttpMethod.Get, path);
                    var html = WebUtility.HtmlDecode(await confirmation.Content.ReadAsStringAsync());
                    Require(confirmation.StatusCode == HttpStatusCode.OK && html.Contains(member.Account.Phone) && html.Contains(member.Account.RealName) && html.Contains("确认删除"), "Account deletion lacks an identifiable confirmation page.");
                    Require(await new AccountStore(source).GetAsync(Guid.Parse(member.Account.Id)) != null, "Opening confirmation deleted the account.");
                    token = Csrf(html);
                    using var noCsrf = await Send(HttpMethod.Post, path, new Dictionary<string, string> { ["version"] = "2", ["confirm"] = "delete" });
                    Require(noCsrf.StatusCode == HttpStatusCode.BadRequest, "Deletion accepted a missing CSRF token.");
                    using var noConfirmation = await Send(HttpMethod.Post, path, new Dictionary<string, string> { ["version"] = "2", ["csrf"] = token });
                    Require(noConfirmation.StatusCode == HttpStatusCode.BadRequest, "Deletion accepted a missing confirmation.");
                    using var stale = await Send(HttpMethod.Post, path, new Dictionary<string, string> { ["version"] = "1", ["csrf"] = token, ["confirm"] = "delete" });
                    Require(stale.StatusCode == HttpStatusCode.Conflict, "Deletion accepted stale account details.");
                    Require(await new AccountStore(source).GetAsync(Guid.Parse(member.Account.Id)) != null, "A rejected form deleted the account.");
                    using var deleted = await Send(HttpMethod.Post, path, new Dictionary<string, string> { ["version"] = "2", ["csrf"] = token, ["confirm"] = "delete" });
                    Require(deleted.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther, "Confirmed account deletion was rejected.");
                    Require(await new AccountStore(source).GetAsync(Guid.Parse(member.Account.Id)) == null, "Deleted account remained in storage.");
                    using var oldSession = new HttpRequestMessage(HttpMethod.Get, "api/v1/me"); oldSession.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", member.AccessToken);
                    using var revoked = await http.SendAsync(oldSession); Require(revoked.StatusCode == HttpStatusCode.Unauthorized, "Deleted account retained its bearer session.");
                    using var refreshed = await Send(HttpMethod.Get, "admin"); html = await refreshed.Content.ReadAsStringAsync();
                    Require(!html.Contains(member.Account.Phone) && html.Contains("删除"), "Account list did not persist deletion or has no delete action.");
                } finally { await using var cleanup = source.CreateCommand("DELETE FROM hifc_web.admin_passwords WHERE account_id=@id"); cleanup.Parameters.AddWithValue("id", Guid.Parse(administrator.Id)); await cleanup.ExecuteNonQueryAsync(); }
            });
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); }
    }
}
