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
        string Credential() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        static async Task Reject(Func<Task> action) { try { await action(); } catch (InvalidOperationException) { return; } throw new Exception("Unsafe identity operation was accepted."); }
        await test("registration and automatic login verify a 256-bit device credential without resetting the account", async () => {
            var request = new Registration { Phone = Phone(), RealName = "凭证登录验收", Credential = Credential() };
            var first = await identity.RegisterAsync(request); var repeated = await identity.RegisterAsync(request);
            var loggedIn = await identity.LoginAsync(new LoginRequest { Phone = request.Phone, Credential = request.Credential });
            Require(first.Account.Id == repeated.Account.Id && first.Account.RegisteredAtUtc == loggedIn.Account.RegisteredAtUtc && first.Account.ExpiresAtUtc == repeated.Account.ExpiresAtUtc, "Device login reset registration or expiry.");
            Require((await identity.ResolveAsync(loggedIn.AccessToken)).Id == first.Account.Id, "Issued session is unusable.");
            await Reject(async () => await identity.RegisterAsync(new Registration { Phone = request.Phone, RealName = request.RealName, Credential = Credential() }));
            await Reject(async () => await identity.LoginAsync(new LoginRequest { Phone = request.Phone, Credential = Credential() }));
        });
        await test("logout revokes the bearer session and weak credentials cannot register", async () => {
            await Reject(async () => await identity.RegisterAsync(new Registration { Phone = Phone(), RealName = "弱凭证验收", Credential = "short" }));
            var session = await identity.RegisterAsync(new Registration { Phone = Phone(), RealName = "退出登录验收", Credential = Credential() });
            await identity.LogoutAsync(session.AccessToken); Require(await identity.ResolveAsync(session.AccessToken) == null, "Logout did not revoke the session.");
        });
        await test("one-time rebinding revokes the old device and preserves the original deadline", async () => {
            var administrator = await new AccountStore(source).TryCreateOrdinaryAsync(Phone(), "绑定管理员验收");
            await using (var command = source.CreateCommand("UPDATE hifc_web.accounts SET is_admin=true WHERE id=@id")) { command.Parameters.AddWithValue("id", Guid.Parse(administrator.Id)); await command.ExecuteNonQueryAsync(); }
            var request = new Registration { Phone = Phone(), RealName = "换机登录验收", Credential = Credential() }; var original = await identity.RegisterAsync(request);
            var code = await identity.IssueRebindAsync(Guid.Parse(administrator.Id), Guid.Parse(original.Account.Id)); var replacement = Credential();
            var rebound = await identity.RebindAsync(new RebindRequest { Phone = request.Phone, Credential = replacement, Code = code });
            Require(rebound.Account.ExpiresAtUtc == original.Account.ExpiresAtUtc && rebound.Account.RegisteredAtUtc == original.Account.RegisteredAtUtc, "Rebinding reset the deadline.");
            Require(await identity.ResolveAsync(original.AccessToken) == null, "Old bearer session survived rebinding.");
            await Reject(async () => await identity.LoginAsync(new LoginRequest { Phone = request.Phone, Credential = request.Credential }));
            await Reject(async () => await identity.RebindAsync(new RebindRequest { Phone = request.Phone, Credential = Credential(), Code = code }));
            Require((await identity.LoginAsync(new LoginRequest { Phone = request.Phone, Credential = replacement })).Account.Id == original.Account.Id, "New device cannot log in.");
        });
        await test("ordinary accounts cannot issue rebinding codes", async () => {
            var session = await identity.RegisterAsync(new Registration { Phone = Phone(), RealName = "绑定越权验收", Credential = Credential() });
            await Reject(async () => await identity.IssueRebindAsync(Guid.Parse(session.Account.Id), Guid.Parse(session.Account.Id)));
        });
        await test("expired accounts can log in and see their unchanged deadline", async () => {
            var request = new Registration { Phone = Phone(), RealName = "到期登录验收", Credential = Credential() }; var session = await identity.RegisterAsync(request);
            await using var command = source.CreateCommand("UPDATE hifc_web.accounts SET expires_at=clock_timestamp()-interval '1 second' WHERE id=@id");
            command.Parameters.AddWithValue("id", Guid.Parse(session.Account.Id)); await command.ExecuteNonQueryAsync();
            var login = await identity.LoginAsync(new LoginRequest { Phone = request.Phone, Credential = request.Credential });
            Require(!login.Account.IsActive && !(await identity.ResolveAsync(login.AccessToken)).IsActive, "Login restored an expired entitlement.");
        });
        await test("administrator bootstrap verifies a separate password and cannot create a second administrator", async () => {
            Guid? created = null;
            try {
                var phone = Phone(); var account = await identity.CreateAdministratorAsync(phone, "后台密码验收", "Synthetic-Admin-Password-2026"); created = Guid.Parse(account.Id);
                Require(account.IsAdmin && (await identity.AdministratorLoginAsync(phone, "Synthetic-Admin-Password-2026")).Id == account.Id, "Separate administrator password is unusable.");
                await Reject(async () => await identity.AdministratorLoginAsync(phone, "Synthetic-Wrong-Password"));
                await Reject(async () => await identity.CreateAdministratorAsync(Phone(), "第二管理员", "Synthetic-Admin-Password-2026"));
            } finally { if(created.HasValue) { await using var cleanup = source.CreateCommand("DELETE FROM hifc_web.admin_passwords WHERE account_id=@id"); cleanup.Parameters.AddWithValue("id", created.Value); await cleanup.ExecuteNonQueryAsync(); } }
        });
    }
}
