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
    internal static async Task Run(NpgsqlDataSource source, Func<string, Func<Task>, Task> test)
    {
        var app = ServerApplication.Create(new[] { "--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "None" }, new WebRuntimeOptions { DatabaseConnectionString = source.ConnectionString, SigningKeyPath = Path.Combine(Path.GetTempPath(), "HifcEndpointTests", Guid.NewGuid().ToString("N"), "signing.pem"), EnableAccounts = true });
        await app.StartAsync(); var address = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single());
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = address };
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        async Task<AccountSession> Register()
        {
            var registration = new Registration { Phone = "139" + Random.Shared.NextInt64(10000000, 100000000), RealName = "HTTP流程验收", Credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
            var reply = await http.PostAsync("api/v1/accounts/register", new StringContent(JsonWire.Serialize(registration), Encoding.UTF8, "application/json"));
            Require(reply.IsSuccessStatusCode, "Registration endpoint is unavailable: " + (int)reply.StatusCode);
            return JsonWire.Deserialize<AccountSession>(await reply.Content.ReadAsStringAsync());
        }
        try
        {
            await test("enabled deployment health reports the active authentication mode", async () => {
                foreach (var path in new[] { "health/live", "health/ready" }) {
                    using var reply = await http.GetAsync(path);
                    var payload = System.Text.Json.JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
                    Require(reply.IsSuccessStatusCode && payload.RootElement.GetProperty("authenticationConfigured").GetBoolean(), "Health metadata still reports the closed foundation mode.");
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
            await test("administrator cookie and signed CSRF token protect browser deadline changes", async () => {
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
                } finally { await using var cleanup = source.CreateCommand("DELETE FROM hifc_web.admin_passwords WHERE account_id=@id"); cleanup.Parameters.AddWithValue("id", Guid.Parse(administrator.Id)); await cleanup.ExecuteNonQueryAsync(); }
            });
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); }
    }
}
