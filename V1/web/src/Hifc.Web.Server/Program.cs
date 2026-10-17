using Hifc.Web.Server;

if (args.Length == 1 && args[0] == "--bootstrap-admin")
{
    if (Console.IsInputRedirected || Console.IsOutputRedirected) { Console.Error.WriteLine("Administrator initialization requires an interactive console."); return 2; }
    try {
        var settings = WebRuntimeOptions.FromEnvironment();
        if (!settings.EnableAccounts) throw new InvalidOperationException("Authentication must be enabled first.");
        await using var source = Npgsql.NpgsqlDataSource.Create(settings.DatabaseConnectionString);
        var accounts = new AccountStore(source); var identities = new IdentityStore(source);
        await accounts.InitializeAsync(); await identities.InitializeAsync();
        Console.Write("Administrator phone: "); var phone = Console.ReadLine();
        Console.Write("Real name: "); var name = Console.ReadLine();
        static string Password(string prompt) {
            Console.Write(prompt); var value = new System.Text.StringBuilder();
            while (true) {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return value.ToString(); }
                if (key.Key == ConsoleKey.Escape) throw new OperationCanceledException();
                if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.Length--; }
                else if (!char.IsControl(key.KeyChar) && value.Length < 129) value.Append(key.KeyChar);
            }
        }
        var password = Password("Password (12-128 characters; hidden): ");
        if (password != Password("Repeat password (hidden): ")) throw new InvalidOperationException("Passwords do not match.");
        await identities.CreateAdministratorAsync(phone, name, password);
        Console.WriteLine("Administrator initialized. Open https://192.168.6.45:8443/admin"); return 0;
    } catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); return 1; }
    catch (Exception ex) { Console.Error.WriteLine("ADMIN_INITIALIZATION_FAILED " + ex.GetType().Name); return 1; }
}

if (args.Length == 1 && args[0] == "--health-check")
{
    try {
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
        return (await client.GetAsync("http://127.0.0.1:8080/health/ready")).IsSuccessStatusCode ? 0 : 1;
    } catch { return 1; }
}

try
{
    var options = WebRuntimeOptions.FromEnvironment();
    var app = ServerApplication.Create(args, options);
    await app.Services.GetRequiredService<AccountStore>().InitializeAsync();
    await app.Services.GetRequiredService<WorkflowStore>().InitializeAsync();
    if (options.EnableAccounts) await app.Services.GetRequiredService<IdentityStore>().InitializeAsync();
    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("HIFC_WEB_STARTUP_FAILED " + ex.GetType().Name);
    return 1;
}
