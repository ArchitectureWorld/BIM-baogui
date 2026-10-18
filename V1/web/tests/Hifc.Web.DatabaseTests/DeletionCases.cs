using Hifc.Web.Contracts;
using Hifc.Web.Server;
using Npgsql;
using System.Security.Cryptography;

internal static class DeletionCases
{
    internal static async Task Run(NpgsqlDataSource source, Func<string, Func<Task>, Task> test)
    {
        var store = new AccountStore(source); var identity = new IdentityStore(source);
        string Phone() => "139" + Random.Shared.NextInt64(10000000, 100000000);
        string Credential() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        static async Task Refused(Func<Task> action, int status) {
            try { await action(); } catch (WebOperationException ex) when (ex.Status == status) { return; }
            throw new Exception("Deletion did not reject an unsafe operation with HTTP " + status);
        }
        var administrator = await store.TryCreateOrdinaryAsync(Phone(), "删除管理验收"); var actor = Guid.Parse(administrator.Id);
        await using (var command = source.CreateCommand("UPDATE hifc_web.accounts SET is_admin=true WHERE id=@id")) { command.Parameters.AddWithValue("id", actor); await command.ExecuteNonQueryAsync(); }
        async Task<long> Count(string table, string column, Guid id) {
            await using var command = source.CreateCommand($"SELECT count(*) FROM hifc_web.{table} WHERE {column}=@id"); command.Parameters.AddWithValue("id", id); return (long)await command.ExecuteScalarAsync();
        }
        await test("account deletion atomically revokes devices sessions rebind and workflows while retaining its own audit", async () => {
            var registration = new Registration { Phone = Phone(), RealName = "完整删除验收", Credential = Credential() };
            var session = await identity.RegisterAsync(registration); var id = Guid.Parse(session.Account.Id);
            await identity.LoginAsync(new LoginRequest { Phone = registration.Phone, Credential = registration.Credential });
            var code = await identity.IssueRebindAsync(actor, id); await store.ExtendAsync(actor, id, 1, 1, null);
            await using (var command = source.CreateCommand("INSERT INTO hifc_web.workflows(actor_id,scan_id,payload) VALUES(@id,'deletion-fixture','{}')")) { command.Parameters.AddWithValue("id", id); await command.ExecuteNonQueryAsync(); }
            await store.DeleteAsync(actor, id, 2);
            Require(await store.GetAsync(id) == null && await identity.ResolveAsync(session.AccessToken) == null, "Deleted account retained access.");
            foreach (var table in new[] { "sessions", "device_credentials", "rebind_codes", "admin_passwords", "account_audit", "identity_audit" })
                Require(await Count(table, "account_id", id) == 0, "Deletion left related rows in " + table);
            Require(await Count("identity_audit", "actor_id", id) == 0 && await Count("workflows", "actor_id", id) == 0, "Deletion left actor data.");
            await using (var command = source.CreateCommand("SELECT actor_id=@actor AND phone=@phone AND real_name=@name AND row_version=2 FROM hifc_web.account_deletion_audit WHERE account_id=@id")) {
                command.Parameters.AddWithValue("actor", actor); command.Parameters.AddWithValue("phone", registration.Phone); command.Parameters.AddWithValue("name", registration.RealName); command.Parameters.AddWithValue("id", id);
                Require(Equals(await command.ExecuteScalarAsync(), true), "Deletion audit lost the target or actor.");
            }
            await Refused(() => store.DeleteAsync(actor, id, 2), 404);
            Require(await Count("account_deletion_audit", "account_id", id) == 1, "Repeated deletion duplicated its audit.");
            try { await identity.RebindAsync(new RebindRequest { Phone = registration.Phone, Credential = Credential(), Code = code }); throw new Exception("Deleted binding code retained access."); } catch (WebOperationException) { }
        });
        await test("ordinary actors administrator targets and stale confirmations cannot delete accounts", async () => {
            var member = await store.TryCreateOrdinaryAsync(Phone(), "删除保护验收"); var id = Guid.Parse(member.Id);
            await Refused(() => store.DeleteAsync(id, id, 1), 403);
            await Refused(() => store.DeleteAsync(Guid.NewGuid(), id, 1), 403);
            await Refused(() => store.DeleteAsync(actor, actor, 1), 403);
            await store.ExtendAsync(actor, id, 1, 2, null);
            await Refused(() => store.DeleteAsync(actor, id, 1), 409);
            Require((await store.GetAsync(id))?.RowVersion == 2 && await Count("account_deletion_audit", "account_id", id) == 0, "Rejected deletion changed account data.");
        });
        await test("deletion audit failure rolls back the account and its live credentials", async () => {
            var session = await identity.RegisterAsync(new Registration { Phone = Phone(), RealName = "删除回滚验收", Credential = Credential() }); var id = Guid.Parse(session.Account.Id);
            var constraint = "hifc_delete_reject_" + id.ToString("N");
            await using var command = source.CreateCommand($"ALTER TABLE hifc_web.account_deletion_audit ADD CONSTRAINT {constraint} CHECK(account_id <> '{id:D}'::uuid) NOT VALID"); await command.ExecuteNonQueryAsync();
            try {
                bool rejected = false;
                try { await store.DeleteAsync(actor, id, 1); } catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation) { rejected = true; }
                Require(rejected && await store.GetAsync(id) != null && await identity.ResolveAsync(session.AccessToken) != null, "Failed audit lost the account or credentials.");
                Require(await Count("account_deletion_audit", "account_id", id) == 0, "Failed deletion left an audit.");
            } finally { command.CommandText = $"ALTER TABLE hifc_web.account_deletion_audit DROP CONSTRAINT {constraint}"; await command.ExecuteNonQueryAsync(); }
        });
        await test("concurrent deletion confirmations remove one account and audit exactly once", async () => {
            var member = await store.TryCreateOrdinaryAsync(Phone(), "并发删除验收"); var id = Guid.Parse(member.Id);
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ => {
                try { await store.DeleteAsync(actor, id, 1); return true; } catch (WebOperationException ex) when (ex.Status == 404) { return false; }
            }));
            Require(results.Count(value => value) == 1 && await Count("account_deletion_audit", "account_id", id) == 1, "Concurrent deletion was not atomic.");
        });
    }
}
