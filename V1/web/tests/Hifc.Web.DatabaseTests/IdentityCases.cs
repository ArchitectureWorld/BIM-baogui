using Hifc.Web.Contracts;
using Hifc.Web.Server;
using Npgsql;
using System.Security.Cryptography;

internal static class IdentityCases
{
    internal static async Task Run(NpgsqlDataSource source, Func<string, Func<Task>, Task> test)
    {
        var identity = new IdentityStore(source); await identity.InitializeAsync();
        string Phone() => "139" + Random.Shared.NextInt64(10000000, 100000000);
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        static async Task Reject(Func<Task> action) { try { await action(); } catch (InvalidOperationException) { return; } throw new Exception("Unsafe identity operation was accepted."); }
        await test("phone and real name support independent computer sessions without changing account lifetime", async () => {
            var request = new Registration { Phone = Phone(), RealName = "人员登录验收" };
            var first = await identity.RegisterAsync(request); var repeated = await identity.RegisterAsync(request);
            var other = await identity.LoginAsync(new LoginRequest { Phone = "+86 " + request.Phone, RealName = "  " + request.RealName + "  " });
            Require(first.Account.Id == other.Account.Id && repeated.Account.Id == first.Account.Id && first.Account.ExpiresAtUtc == other.Account.ExpiresAtUtc && first.Account.RegisteredAtUtc == other.Account.RegisteredAtUtc, "Cross-computer login reset the person or deadline.");
            Require(other.AccessToken != first.AccessToken && await identity.ResolveAsync(first.AccessToken) != null && await identity.ResolveAsync(other.AccessToken) != null, "Independent computers do not retain independent sessions.");
            await Reject(async () => await identity.RegisterAsync(new Registration { Phone = request.Phone, RealName = "不同人员验收" }));
            await Reject(async () => await identity.LoginAsync(new LoginRequest { Phone = request.Phone, RealName = "不同人员验收" }));
            Require((await new AccountStore(source).GetAsync(Guid.Parse(first.Account.Id))).RealName == request.RealName, "A mismatched name overwrote the person.");
        });
        await test("simultaneous first registrations of one person create one account and preserve all sessions", async () => {
            var phone = Phone(); var sessions = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => identity.RegisterAsync(new Registration { Phone = phone, RealName = "并发人员验收" })));
            Require(sessions.Select(value => value.Account.Id).Distinct().Count() == 1 && sessions.Select(value => value.Account.ExpiresAtUtc).Distinct().Count() == 1, "Concurrent computers duplicated the person or lifetime.");
            Require(sessions.Select(value => value.AccessToken).Distinct().Count() == 6, "Computers shared a bearer token.");
        });
        await test("logout revokes only its own session and incomplete person details cannot log in", async () => {
            var request = new Registration { Phone = Phone(), RealName = "退出人员验收" }; var first = await identity.RegisterAsync(request);
            var other = await identity.LoginAsync(new LoginRequest { Phone = request.Phone, RealName = request.RealName });
            await identity.LogoutAsync(first.AccessToken);
            Require(await identity.ResolveAsync(first.AccessToken) == null && await identity.ResolveAsync(other.AccessToken) != null, "Logout revoked another computer or retained its own token.");
            await Reject(async () => await identity.LoginAsync(new LoginRequest { Phone = request.Phone }));
            await Reject(async () => await identity.RegisterAsync(new Registration { Phone = Phone(), RealName = "一" }));
            await Reject(async () => await identity.LoginAsync(new LoginRequest { Phone = Phone(), RealName = request.RealName }));
        });
        await test("expired people can log in across computers without restoring or resetting their deadline", async () => {
            var request = new Registration { Phone = Phone(), RealName = "到期人员验收" }; var first = await identity.RegisterAsync(request);
            await using var command = source.CreateCommand("UPDATE hifc_web.accounts SET expires_at=clock_timestamp()-interval '1 second' WHERE id=@id RETURNING expires_at");
            command.Parameters.AddWithValue("id", Guid.Parse(first.Account.Id)); var expiry = (DateTime)await command.ExecuteScalarAsync();
            var other = await identity.LoginAsync(new LoginRequest { Phone = request.Phone, RealName = request.RealName });
            var repeated = await identity.RegisterAsync(request);
            Require(!other.Account.IsActive && repeated.Account.ExpiresAtUtc == expiry && other.Account.ExpiresAtUtc == expiry, "Another computer restored an expired entitlement.");
        });
        await test("administrator bootstrap verifies a separate password and cannot create a second administrator", async () => {
            Guid? created = null;
            try {
                var phone = Phone(); var account = await identity.CreateAdministratorAsync(phone, "后台密码验收", "Synthetic-Admin-Password-2026"); created = Guid.Parse(account.Id);
                Require(account.IsAdmin && (await identity.AdministratorLoginAsync(phone, "Synthetic-Admin-Password-2026")).Id == account.Id, "Separate administrator password is unusable.");
                await Reject(async () => await identity.AdministratorLoginAsync(phone, "Synthetic-Wrong-Password"));
                await Reject(async () => await identity.LoginAsync(new LoginRequest { Phone = phone, RealName = account.RealName }));
                await Reject(async () => await identity.RegisterAsync(new Registration { Phone = phone, RealName = account.RealName }));
                await Reject(async () => await identity.CreateAdministratorAsync(Phone(), "第二管理员", "Synthetic-Admin-Password-2026"));
            } finally { if(created.HasValue) { await using var cleanup = source.CreateCommand("DELETE FROM hifc_web.admin_passwords WHERE account_id=@id"); cleanup.Parameters.AddWithValue("id", created.Value); await cleanup.ExecuteNonQueryAsync(); } }
        });
    }
}
