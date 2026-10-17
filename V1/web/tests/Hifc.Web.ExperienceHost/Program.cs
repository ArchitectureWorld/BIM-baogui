using Hifc.Web.Server;
using Npgsql;

var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HIFC_TEST_DATABASE") ?? "Host=127.0.0.1;Port=55432;Username=zetta;Database=hifc_web_test;SSL Mode=Disable;Timeout=10") { GssEncryptionMode = GssEncryptionMode.Disable };
if (connection.Database != "hifc_web_test") throw new InvalidOperationException("Only the dedicated test database is permitted.");
var root = Path.Combine(Directory.GetCurrentDirectory(), ".local", "experience-host"); Directory.CreateDirectory(root);
var app = ServerApplication.Create(new[] { "--urls", "http://127.0.0.1:55443", "--Logging:LogLevel:Default", "Warning" }, new WebRuntimeOptions { DatabaseConnectionString = connection.ConnectionString, SigningKeyPath = Path.Combine(root, "signing.pem"), EnableAccounts = true, BuildRevision = "isolated-native-experience-test" });
await app.Services.GetRequiredService<AccountStore>().InitializeAsync(); await app.Services.GetRequiredService<WorkflowStore>().InitializeAsync(); await app.Services.GetRequiredService<IdentityStore>().InitializeAsync();
await app.RunAsync();
