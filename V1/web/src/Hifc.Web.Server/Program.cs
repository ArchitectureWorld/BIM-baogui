using Hifc.Web.Server;

if (args.Length == 1 && args[0] == "--health-check")
{
    try {
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
        return (await client.GetAsync("http://127.0.0.1:8080/health/ready")).IsSuccessStatusCode ? 0 : 1;
    } catch { return 1; }
}

try
{
    var app = ServerApplication.Create(args, WebRuntimeOptions.FromEnvironment());
    await app.Services.GetRequiredService<AccountStore>().InitializeAsync();
    await app.Services.GetRequiredService<WorkflowStore>().InitializeAsync();
    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("HIFC_WEB_STARTUP_FAILED " + ex.GetType().Name);
    return 1;
}
