using Hifc.Web.Server;
using Npgsql;

// This executable requires an actual dedicated PostgreSQL database; no in-memory substitute.
var connectionString = Environment.GetEnvironmentVariable("HIFC_TEST_DATABASE")
    ?? "Host=127.0.0.1;Port=55432;Username=zetta;Database=hifc_web_test;SSL Mode=Disable;Timeout=10";
var settings = new NpgsqlConnectionStringBuilder(connectionString);
if (settings.Database != "hifc_web_test") throw new InvalidOperationException("A dedicated hifc_web_test database is required.");
var passwordFile = Environment.GetEnvironmentVariable("HIFC_TEST_DATABASE_PASSWORD_FILE");
if (passwordFile != null) { settings.Password = File.ReadAllText(passwordFile).TrimEnd('\r', '\n'); connectionString = settings.ConnectionString; }
await using var dataSource = NpgsqlDataSource.Create(connectionString);
await using (var version = dataSource.CreateCommand("SELECT version()"))
    Console.WriteLine("DATABASE " + await version.ExecuteScalarAsync());
var store = new AccountStore(dataSource);
await store.InitializeAsync();
if (args.Length != 0) return await PersistenceCases.RunAsync(store, dataSource, args);
int passed = 0, failed = 0;
async Task Test(string name, Func<Task> run)
{
    try { await run(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.GetType().Name + " " + ex.Message); }
}
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
string Phone() => "139" + Random.Shared.NextInt64(10000000, 100000000);

await Test("registration uses database UTC time and an exact 168 hour lifetime", async () => {
    await using var clock = dataSource.CreateCommand("SELECT clock_timestamp()");
    var before = (DateTime)(await clock.ExecuteScalarAsync());
    var account = await store.TryCreateOrdinaryAsync("+86 " + Phone(), "  数据库验收  ");
    var after = (DateTime)(await clock.ExecuteScalarAsync());
    Require(account != null, "Registration did not persist an account.");
    Require(account.RealName == "数据库验收" && account.Phone.Length == 11 && !account.IsAdmin, "Registration normalization or role is wrong.");
    Require(account.RegisteredAtUtc.Kind == DateTimeKind.Utc && account.RegisteredAtUtc >= before && account.RegisteredAtUtc <= after, "Registration did not use the database clock.");
    Require(account.ExpiresAtUtc - account.RegisteredAtUtc == TimeSpan.FromHours(168) && account.IsActive && account.RowVersion == 1, "Initial lifetime or active state is wrong.");
});
await Test("concurrent normalized duplicate registrations persist exactly one account", async () => {
    var phone = Phone();
    var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => store.TryCreateOrdinaryAsync(i % 2 == 0 ? phone : "+86 " + phone, "并发登记验收")));
    Require(results.Count(a => a != null) == 1, "Duplicate registrations were not resolved atomically.");
    var original = results.Single(a => a != null);
    Require(await store.TryCreateOrdinaryAsync(phone, "重复登记不改姓名") == null, "Duplicate registration disclosed an existing account.");
    var retained = await store.GetAsync(Guid.Parse(original.Id));
    Require(retained != null && retained.RealName == "并发登记验收" && retained.RegisteredAtUtc == original.RegisteredAtUtc && retained.ExpiresAtUtc == original.ExpiresAtUtc && retained.RowVersion == 1, "Duplicate registration reset the account.");
});
await Test("a new connection sees the persisted account", async () => {
    var account = await store.TryCreateOrdinaryAsync(Phone(), "重连持久化验收");
    Require(account != null, "Registration did not persist an account.");
    await using var reopened = NpgsqlDataSource.Create(connectionString);
    var retained = await new AccountStore(reopened).GetAsync(Guid.Parse(account.Id));
    Require(retained != null && retained.Id == account.Id && retained.ExpiresAtUtc == account.ExpiresAtUtc, "Account disappeared on reconnect.");
});
await Test("invalid registration input cannot reach storage", async () => {
    foreach (var pair in new[] { ("1380000", "有效姓名"), (Phone(), "一"), (Phone(), "错误\n姓名") }) {
        try { await store.TryCreateOrdinaryAsync(pair.Item1, pair.Item2); }
        catch (InvalidOperationException) { continue; }
        throw new Exception("Invalid registration input was accepted.");
    }
});
async Task SetAdmin(Guid id) {
    await using var command = dataSource.CreateCommand("UPDATE hifc_web.accounts SET is_admin=true WHERE id=@id");
    command.Parameters.AddWithValue("id", id); await command.ExecuteNonQueryAsync();
}
async Task Expire(Guid id) {
    await using var command = dataSource.CreateCommand("UPDATE hifc_web.accounts SET expires_at=clock_timestamp() - interval '24 hours' WHERE id=@id");
    command.Parameters.AddWithValue("id", id); await command.ExecuteNonQueryAsync();
}
async Task<long> AuditCount(Guid id) {
    await using var command = dataSource.CreateCommand("SELECT count(*) FROM hifc_web.account_audit WHERE account_id=@id");
    command.Parameters.AddWithValue("id", id); return (long)(await command.ExecuteScalarAsync());
}
async Task Reject(Func<Task> run) {
    try { await run(); } catch (InvalidOperationException) { return; }
    throw new Exception("Unauthorized or stale account operation was accepted.");
}
var administrator = await store.TryCreateOrdinaryAsync(Phone(), "管理员数据库验收");
var administratorId = Guid.Parse(administrator.Id); await SetAdmin(administratorId);
await Test("expired ordinary accounts lose access while administrators remain active", async () => {
    var account = await store.TryCreateOrdinaryAsync(Phone(), "到期数据库验收");
    await Expire(Guid.Parse(account.Id)); await Expire(administratorId);
    Require(!(await store.GetAsync(Guid.Parse(account.Id))).IsActive, "Expired ordinary account remained active.");
    Require((await store.GetAsync(administratorId)).IsActive, "Administrator incorrectly expired.");
});
await Test("administrator extension updates the deadline and audit atomically", async () => {
    var account = await store.TryCreateOrdinaryAsync(Phone(), "有效期内延期验收");
    var id = Guid.Parse(account.Id);
    var extended = await store.ExtendAsync(administratorId, id, account.RowVersion, 3, null);
    Require(extended != null, "Extension did not return the persisted deadline.");
    Require(extended.ExpiresAtUtc == account.ExpiresAtUtc.AddHours(72) && extended.RowVersion == 2 && extended.RegisteredAtUtc == account.RegisteredAtUtc, "Extension changed the original registration or used the wrong base.");
    Require(await AuditCount(id) == 1, "Extension did not create exactly one audit record.");
});
await Test("an expired account extends from the database current time", async () => {
    var account = await store.TryCreateOrdinaryAsync(Phone(), "过期后延期验收"); var id = Guid.Parse(account.Id);
    await Expire(id);
    var extended = await store.ExtendAsync(administratorId, id, account.RowVersion, 2, null);
    Require(extended != null, "Extension did not return the persisted deadline.");
    Require(extended.ExpiresAtUtc - extended.ServerTimeUtc == TimeSpan.FromHours(48) && extended.IsActive, "Expired account did not extend from database now.");
});
await Test("calendar deadline includes the entire selected Beijing date", async () => {
    var account = await store.TryCreateOrdinaryAsync(Phone(), "北京时间延期验收");
    var extended = await store.ExtendAsync(administratorId, Guid.Parse(account.Id), 1, null, "2031-01-01");
    Require(extended != null && extended.ExpiresAtUtc == new DateTime(2031, 1, 1, 16, 0, 0, DateTimeKind.Utc), "Selected Beijing date was truncated.");
});
await Test("ordinary or unknown actors cannot extend an account or produce an audit", async () => {
    var actor = await store.TryCreateOrdinaryAsync(Phone(), "普通操作者验收");
    var account = await store.TryCreateOrdinaryAsync(Phone(), "越权目标验收"); var id = Guid.Parse(account.Id);
    await Reject(async () => await store.ExtendAsync(Guid.Parse(actor.Id), id, 1, 2, null));
    await Reject(async () => await store.ExtendAsync(Guid.NewGuid(), id, 1, 2, null));
    Require((await store.GetAsync(id)).ExpiresAtUtc == account.ExpiresAtUtc && await AuditCount(id) == 0, "Unauthorized operation changed persisted data.");
});
await Test("concurrent stale administrator forms cannot extend the same account twice", async () => {
    var account = await store.TryCreateOrdinaryAsync(Phone(), "并发延期验收"); var id = Guid.Parse(account.Id);
    var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => {
        try { return await store.ExtendAsync(administratorId, id, 1, 1, null); }
        catch (InvalidOperationException) { return null; }
    }));
    Require(results.Count(a => a != null) == 1, "Stale forms were not rejected atomically.");
    var retained = await store.GetAsync(id);
    Require(retained.ExpiresAtUtc == account.ExpiresAtUtc.AddHours(24) && retained.RowVersion == 2 && await AuditCount(id) == 1, "Concurrent extension duplicated the deadline or audit.");
});
await Test("invalid extensions leave the deadline and audit untouched", async () => {
    var account = await store.TryCreateOrdinaryAsync(Phone(), "无效延期验收"); var id = Guid.Parse(account.Id);
    await Reject(async () => await store.ExtendAsync(administratorId, id, 1, 0, null));
    await Reject(async () => await store.ExtendAsync(administratorId, id, 1, 2, "2031-01-01"));
    await Reject(async () => await store.ExtendAsync(administratorId, id, 1, null, "2020-01-01"));
    Require((await store.GetAsync(id)).RowVersion == 1 && (await store.GetAsync(id)).ExpiresAtUtc == account.ExpiresAtUtc && await AuditCount(id) == 0, "Rejected extension changed persisted data.");
});
await Test("an audit storage failure rolls back the deadline update", async () => {
    var account = await store.TryCreateOrdinaryAsync(Phone(), "审计失败回滚验收"); var id = Guid.Parse(account.Id);
    var constraint = "hifc_test_reject_" + id.ToString("N");
    await using var command = dataSource.CreateCommand($"ALTER TABLE hifc_web.account_audit ADD CONSTRAINT {constraint} CHECK (account_id <> '{id:D}'::uuid) NOT VALID");
    await command.ExecuteNonQueryAsync();
    try {
        bool rejected = false;
        try { await store.ExtendAsync(administratorId, id, 1, 5, null); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation) { rejected = true; }
        Require(rejected, "Audit failure was silently ignored.");
        var retained = await store.GetAsync(id);
        Require(retained.RowVersion == 1 && retained.ExpiresAtUtc == account.ExpiresAtUtc && await AuditCount(id) == 0, "Failed audit committed the account update.");
    } finally {
        command.CommandText = $"ALTER TABLE hifc_web.account_audit DROP CONSTRAINT {constraint}";
        await command.ExecuteNonQueryAsync();
    }
});
Console.WriteLine($"RESULT {passed} passed; {failed} failed");
return failed == 0 ? 0 : 1;
