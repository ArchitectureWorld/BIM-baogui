using Hifc.Web.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

internal static class ForwardedHeadersCases
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (var trusted in new[] { false, true }) test(trusted ? "known reverse proxy supplies the real client address" : "an untrusted caller cannot spoof its client address", () => Check(trusted).GetAwaiter().GetResult());
    }
    private static async Task Check(bool trusted)
    {
        var key = Path.Combine(Path.GetTempPath(), "hifc-forwarded-tests", Guid.NewGuid().ToString("N"), "key.pem");
        var app = ServerApplication.Create(new[] { "--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "None" }, new WebRuntimeOptions { DatabaseConnectionString = "Host=127.0.0.1;Database=hifc_web_test;Username=unused", SigningKeyPath = key, TrustedProxyNetwork = trusted ? "127.0.0.1/32" : "10.22.0.0/16" });
        app.MapGet("/_test/source", (Microsoft.AspNetCore.Http.HttpContext context) => context.Connection.RemoteIpAddress.MapToIPv4().ToString());
        await app.StartAsync();
        try {
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single();
            using var request = new HttpRequestMessage(HttpMethod.Get, address + "/_test/source"); request.Headers.Add("X-Forwarded-For", "198.51.100.13");
            using var response = await http.SendAsync(request); var actual = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode || actual != (trusted ? "198.51.100.13" : "127.0.0.1")) throw new Exception("Trusted proxy boundary or forwarded address is wrong: " + actual);
        } finally { await app.StopAsync(); await app.DisposeAsync(); }
    }
}
