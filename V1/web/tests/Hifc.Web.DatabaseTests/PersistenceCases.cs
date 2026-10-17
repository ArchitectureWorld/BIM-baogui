using Hifc.Web.Contracts;
using Hifc.Web.Server;
using Npgsql;
using System.Text.Json;

internal static class PersistenceCases
{
    private sealed record Snapshot(AccountInfo Account, DateTime DatabaseStartedAtUtc);
    internal static async Task<int> RunAsync(AccountStore store, NpgsqlDataSource dataSource, string[] args)
    {
        if (args.Length != 2 || args[0] is not ("--write-persistence" or "--verify-persistence"))
            throw new InvalidOperationException("Use --write-persistence PATH or --verify-persistence PATH.");
        await using var clock = dataSource.CreateCommand("SELECT pg_postmaster_start_time()");
        var databaseStartedAtUtc = (DateTime)(await clock.ExecuteScalarAsync());
        if (args[0] == "--write-persistence")
        {
            var administrator = await store.TryCreateOrdinaryAsync(Phone(), "重启验证管理员");
            await using var command = dataSource.CreateCommand("UPDATE hifc_web.accounts SET is_admin=true WHERE id=@id");
            command.Parameters.AddWithValue("id", Guid.Parse(administrator.Id)); await command.ExecuteNonQueryAsync();
            var account = await store.TryCreateOrdinaryAsync(Phone(), "数据库重启持久化验收");
            var extended = await store.ExtendAsync(Guid.Parse(administrator.Id), Guid.Parse(account.Id), 1, 5, null);
            await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(new Snapshot(extended, databaseStartedAtUtc)));
            Console.WriteLine("PASS persistence checkpoint stored after account extension");
            return 0;
        }
        var snapshot = JsonSerializer.Deserialize<Snapshot>(await File.ReadAllTextAsync(args[1]));
        if (snapshot == null || databaseStartedAtUtc <= snapshot.DatabaseStartedAtUtc)
            throw new Exception("The PostgreSQL server has not restarted since this checkpoint.");
        var retained = await store.GetAsync(Guid.Parse(snapshot.Account.Id));
        if (retained == null || retained.RegisteredAtUtc != snapshot.Account.RegisteredAtUtc || retained.ExpiresAtUtc != snapshot.Account.ExpiresAtUtc
            || retained.RowVersion != 2 || retained.IsAdmin || retained.Phone != snapshot.Account.Phone || retained.RealName != snapshot.Account.RealName)
            throw new Exception("Account data changed or disappeared after PostgreSQL restart.");
        await using var audit = dataSource.CreateCommand("SELECT count(*) FROM hifc_web.account_audit WHERE account_id=@id AND new_expires_at=@expiry");
        audit.Parameters.AddWithValue("id", Guid.Parse(retained.Id)); audit.Parameters.AddWithValue("expiry", retained.ExpiresAtUtc);
        if (!Equals(await audit.ExecuteScalarAsync(), 1L)) throw new Exception("The extension audit did not survive PostgreSQL restart.");
        Console.WriteLine("PASS real PostgreSQL restart preserves account, deadline, revision and audit");
        return 0;
    }
    private static string Phone() => "139" + Random.Shared.NextInt64(10000000, 100000000);
}
