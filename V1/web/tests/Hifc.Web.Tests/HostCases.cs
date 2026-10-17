using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Hifc.Web.Server;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;

internal static class HostCases
{
    private const string Sentinel = "synthetic-host-test-secret";
    private static void WithHost(Action<Uri> run, string keyPath = null)
    {
        keyPath ??= Path.Combine(Path.GetTempPath(), "HifcWebHostTests", Guid.NewGuid().ToString("N"), "signing.pem");
        var app = ServerApplication.Create(new[] { "--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "None" }, new WebRuntimeOptions {
            DatabaseConnectionString = "Host=127.0.0.1;Port=1;Username=host_test;Database=hifc_host_test;Password=" + Sentinel + ";Timeout=2;Command Timeout=2",
            SigningKeyPath = keyPath, BuildRevision = "host-test"
        });
        app.StartAsync().GetAwaiter().GetResult();
        try { run(new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single())); }
        finally { app.StopAsync().GetAwaiter().GetResult(); app.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    internal static void Run(Action<string, Action> test)
    {
        test("host liveness remains available while the database is unreachable", () => WithHost(uri => {
            using var client = new HttpClient(); using var reply = client.GetAsync(new Uri(uri, "health/live")).GetAwaiter().GetResult();
            if (reply.StatusCode != HttpStatusCode.OK) throw new Exception("Process liveness is unavailable.");
        }));
        test("host readiness fails closed without disclosing the database connection", () => WithHost(uri => {
            using var client = new HttpClient(); using var reply = client.GetAsync(new Uri(uri, "health/ready")).GetAwaiter().GetResult();
            var body = reply.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (reply.StatusCode != HttpStatusCode.ServiceUnavailable || body.Contains(Sentinel) || body.Contains("host_test") || body.Contains("signing.pem"))
                throw new Exception("Database failure was hidden or sensitive settings were disclosed.");
        }));
        test("deployed compatibility route is readable by the actual Web client", () => WithHost(uri => {
            using var client = new WebApiClient(uri); var result = client.CompatibilityAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (result.ProtocolMajor != 1 || result.RuleVersion != "reference-0.4.3-web1" || result.SigningKey == null || string.IsNullOrWhiteSpace(result.SigningKey.Modulus))
                throw new Exception("The host did not expose the actual protocol and public signing key.");
        }));
        test("deferred authentication keeps registration and model mutation routes closed", () => WithHost(uri => {
            using var client = new HttpClient();
            foreach (var path in new[] { "api/v1/accounts/register", "api/v1/sessions", "api/v1/checks/start", "api/v1/edit-plans/test/authorize", "admin/accounts/test/extend" }) {
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(uri, path)) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer unauthenticated-test-input");
                using var reply = client.SendAsync(request).GetAwaiter().GetResult();
                var error = JsonWire.Deserialize<ApiError>(reply.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                if (reply.StatusCode != HttpStatusCode.ServiceUnavailable || error.Code != "AUTHENTICATION_NOT_CONFIGURED") throw new Exception("Deferred authentication did not close " + path);
            }
        }));
        test("host recreation preserves the public signing identity", () => {
            var keyPath = Path.Combine(Path.GetTempPath(), "HifcWebHostTests", Guid.NewGuid().ToString("N"), "persistent.pem");
            string first = null;
            WithHost(uri => { using var client = new WebApiClient(uri); first = client.CompatibilityAsync(CancellationToken.None).GetAwaiter().GetResult().SigningKey.Modulus; }, keyPath);
            WithHost(uri => { using var client = new WebApiClient(uri); if (client.CompatibilityAsync(CancellationToken.None).GetAwaiter().GetResult().SigningKey.Modulus != first) throw new Exception("Signing identity changed on host recreation."); }, keyPath);
        });
    }
}
