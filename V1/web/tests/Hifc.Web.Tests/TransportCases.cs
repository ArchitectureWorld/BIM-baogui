using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class TransportCases
{
    private static void Reject(Action run) { try { run(); } catch (InvalidOperationException) { return; } throw new Exception("Unsafe network transport was accepted"); }
    private static void WithServer(Action<WebApplication> configure, Action<Uri> test)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build(); configure(app); app.StartAsync().GetAwaiter().GetResult();
        try { var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single(); test(new Uri(address)); }
        finally { app.StopAsync().GetAwaiter().GetResult(); app.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    internal static void Run(Action<string, Action> test)
    {
        test("LAN transport refuses HTTP with bearer credentials", () => Reject(() => { using var client = new WebApiClient(new Uri("http://192.168.6.45:8443")); }));
        test("real HTTP transport preserves bearer and scan envelope", () => {
            string header = null, scan = null;
            WithServer(app => app.MapPost("/api/v1/checks/start", async context => {
                header = context.Request.Headers.Authorization;
                var request = await System.Text.Json.JsonSerializer.DeserializeAsync<InventoryPage>(context.Request.Body);
                scan = request.Context.ScanId;
                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new CapturePlan { Context = request.Context, InventoryComplete = true }));
            }), uri => {
                using var client = new WebApiClient(uri); client.SetSession(new AccountSession { AccessToken = "opaque-test-session" });
                var context = new RequestContext { RuleVersion = "r", DocumentSessionId = "doc", ScanId = "actual-scan", RequestId = "request", SnapshotHash = "snapshot" };
                var reply = client.StartCheckAsync(new InventoryPage { Context = context }, CancellationToken.None).GetAwaiter().GetResult();
                if (header != "Bearer opaque-test-session" || scan != "actual-scan" || !reply.InventoryComplete) throw new Exception("Request was lost or sent without authentication");
            });
        });
        test("HTTP client refuses stale document response", () => WithServer(app => app.MapPost("/api/v1/checks/start", async context => {
            var request = await System.Text.Json.JsonSerializer.DeserializeAsync<InventoryPage>(context.Request.Body); request.Context.DocumentSessionId = "other-doc";
            await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new CapturePlan { Context = request.Context, InventoryComplete = true }));
        }), uri => { using var client = new WebApiClient(uri); Reject(() => client.StartCheckAsync(new InventoryPage { Context = new RequestContext { DocumentSessionId = "doc" } }, CancellationToken.None).GetAwaiter().GetResult()); }));
        test("redirect response never forwards credentials to another route", () => {
            var stolen = 0;
            WithServer(app => { app.MapGet("/api/v1/me", context => { context.Response.StatusCode = 302; context.Response.Headers.Location = "/steal"; return Task.CompletedTask; }); app.MapGet("/steal", () => { stolen++; return "{}"; }); }, uri => {
                using var client = new WebApiClient(uri); client.SetSession(new AccountSession { AccessToken = "opaque-test-session" });
                try { client.MeAsync(CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Redirect response was silently accepted"); } catch (WebApiException) { }
                if (stolen != 0) throw new Exception("Credential was forwarded through a redirect");
            });
        });
        test("server account-expiry error remains actionable", () => WithServer(app => app.MapGet("/api/v1/me", async context => {
            context.Response.StatusCode = 403; await context.Response.WriteAsync("{\"Code\":\"ACCOUNT_EXPIRED\",\"Message\":\"账号已到期，人工草稿保留\"}");
        }), uri => { using var client = new WebApiClient(uri); try { client.MeAsync(CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Expired account response was ignored"); } catch (WebApiException ex) { if (ex.Code != "ACCOUNT_EXPIRED" || !ex.Message.Contains("草稿")) throw new Exception("Actionable server error was discarded"); } }));
        test("a stalled response body obeys the whole-request deadline", () => WithServer(app => app.MapGet("/api/v1/me", async context => {
            context.Response.ContentType = "application/json"; await context.Response.StartAsync(); await context.Response.Body.FlushAsync();
            try { await Task.Delay(10000, context.RequestAborted); } catch (OperationCanceledException) { }
        }), uri => { using var client = new WebApiClient(uri, TimeSpan.FromMilliseconds(100)); using var outer = new CancellationTokenSource(1500); try { client.MeAsync(outer.Token).GetAwaiter().GetResult(); throw new Exception("Stalled body was accepted"); } catch (WebApiException ex) { if (ex.Code != "NETWORK_TIMEOUT") throw; } }));
    }
}
