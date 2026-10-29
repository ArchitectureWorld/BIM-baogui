using System.Net;
using System.Security.Cryptography;
using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Hifc.Web.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

static class ComponentServerCases
{
    public static void Run(Action<string, Action> test)
    {
        test("component endpoints sign versioned archive with existing signing identity", () => {
            var root = Path.Combine(Path.GetTempPath(), "hifc-component-server-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                var data = new byte[] { 1, 2, 3 };
                var component = new ComponentLock { MainReleaseId = "0.5.0-dev-20261029T000000Z", MainSourceCommit = new string('a', 40), ComponentId = "node", Version = "25.4.0", EntryPoint = "node.exe", ArchiveSha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), ArchiveLength = data.Length };
                var directory = Path.Combine(root, "components", component.MainReleaseId, component.ComponentId, component.Version); Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, "archive.zip"), data); File.WriteAllText(Path.Combine(directory, "lock.json"), JsonWire.Serialize(component));
                using var signer = new PlanSigner(Path.Combine(root, "key.pem"));
                var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Services.AddSingleton(signer);
                using var app = builder.Build(); app.Use(async (context, next) => { try { await next(); } catch (InvalidOperationException) { context.Response.StatusCode = 400; } });
                ComponentEndpoints.Map(app, Path.Combine(root, "components")); app.StartAsync().GetAwaiter().GetResult();
                try {
                    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single();
                    using var http = new HttpClient { BaseAddress = new Uri(address) };
                    var route = $"components/{component.MainReleaseId}/node/25.4.0/";
                    var manifest = JsonWire.Deserialize<SignedComponentManifest>(http.GetStringAsync(route + "manifest").GetAwaiter().GetResult());
                    ComponentTrust.Validate(manifest, component, signer.PublicKey, new Uri(UpdateTrust.Server));
                    if (!http.GetByteArrayAsync(route + "archive.zip").GetAwaiter().GetResult().SequenceEqual(data)) throw new Exception("archive differs");
                    if (http.GetAsync(route.Replace("25.4.0", "25.4.1") + "manifest").GetAwaiter().GetResult().StatusCode != HttpStatusCode.NotFound) throw new Exception("version fell back to latest");
                    File.WriteAllBytes(Path.Combine(directory, "archive.zip"), new byte[] { 4, 5, 6 });
                    if (http.GetAsync(route + "manifest").GetAwaiter().GetResult().StatusCode != HttpStatusCode.BadRequest) throw new Exception("changed archive signed");
                } finally { app.StopAsync().GetAwaiter().GetResult(); }
            } finally { Directory.Delete(root, true); }
        });
    }
}
