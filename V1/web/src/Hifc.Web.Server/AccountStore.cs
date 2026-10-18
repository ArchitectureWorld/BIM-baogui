using Hifc.Web.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Hifc.Web.Server;

// Internal persistence boundary. Public registration still needs the approved authentication scheme.
public sealed class AccountStore(NpgsqlDataSource dataSource)
{
    public async Task InitializeAsync(CancellationToken cancellation = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await using var command = new NpgsqlCommand("""
            SELECT pg_advisory_xact_lock(58739482051001);
            CREATE SCHEMA IF NOT EXISTS hifc_web;
            CREATE TABLE IF NOT EXISTS hifc_web.schema_version (
                singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
                version integer NOT NULL
            );
            INSERT INTO hifc_web.schema_version (version) VALUES (1) ON CONFLICT DO NOTHING;
            CREATE TABLE IF NOT EXISTS hifc_web.accounts (
                id uuid PRIMARY KEY,
                phone text NOT NULL UNIQUE CHECK (phone ~ '^1[3-9][0-9]{9}$'),
                real_name text NOT NULL CHECK (char_length(real_name) BETWEEN 2 AND 40),
                registered_at timestamptz NOT NULL,
                expires_at timestamptz NOT NULL,
                is_admin boolean NOT NULL DEFAULT false,
                row_version bigint NOT NULL DEFAULT 1 CHECK (row_version > 0)
            );
            CREATE TABLE IF NOT EXISTS hifc_web.account_audit (
                id uuid PRIMARY KEY,
                actor_id uuid NOT NULL REFERENCES hifc_web.accounts(id),
                account_id uuid NOT NULL REFERENCES hifc_web.accounts(id),
                occurred_at timestamptz NOT NULL,
                old_expires_at timestamptz NOT NULL,
                new_expires_at timestamptz NOT NULL CHECK (new_expires_at > old_expires_at),
                requested_days integer,
                requested_date text,
                CHECK ((requested_days IS NULL) <> (requested_date IS NULL))
            );
            CREATE TABLE IF NOT EXISTS hifc_web.account_deletion_audit (
                id uuid PRIMARY KEY,
                actor_id uuid NOT NULL REFERENCES hifc_web.accounts(id),
                account_id uuid NOT NULL UNIQUE,
                phone text NOT NULL,
                real_name text NOT NULL,
                registered_at timestamptz NOT NULL,
                expires_at timestamptz NOT NULL,
                row_version bigint NOT NULL,
                occurred_at timestamptz NOT NULL DEFAULT clock_timestamp()
            );
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellation);
        command.CommandText = "SELECT version FROM hifc_web.schema_version WHERE singleton";
        if (!Equals(await command.ExecuteScalarAsync(cancellation), 1))
            throw new InvalidOperationException("数据库版本不受当前服务支持，请检查 Web 升级顺序。");
        await transaction.CommitAsync(cancellation);
    }

    public async Task<AccountInfo> TryCreateOrdinaryAsync(string phone, string realName, CancellationToken cancellation = default)
    {
        var normalizedPhone = AccountPolicy.Phone(phone);
        var normalizedName = AccountPolicy.RealName(realName);
        await using var command = dataSource.CreateCommand("""
            WITH instant AS MATERIALIZED (SELECT clock_timestamp() AS utc), created AS (
                INSERT INTO hifc_web.accounts (id, phone, real_name, registered_at, expires_at)
                SELECT @id, @phone, @name, utc, utc + interval '168 hours' FROM instant
                ON CONFLICT (phone) DO NOTHING
                RETURNING *
            )
            SELECT id, phone, real_name, registered_at, expires_at, is_admin, row_version, utc
            FROM created CROSS JOIN instant
            """);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("phone", normalizedPhone);
        command.Parameters.AddWithValue("name", normalizedName);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        return await reader.ReadAsync(cancellation) ? Read(reader) : null;
    }

    public async Task<AccountInfo> GetAsync(Guid id, CancellationToken cancellation = default)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT id, phone, real_name, registered_at, expires_at, is_admin, row_version, clock_timestamp()
            FROM hifc_web.accounts WHERE id = @id
            """);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        return await reader.ReadAsync(cancellation) ? Read(reader) : null;
    }

    // actorId must come from the authenticated administrator session, never a request body.
    public async Task<AccountInfo> ExtendAsync(Guid actorId, Guid accountId, long expectedVersion, int? days, string untilDate, CancellationToken cancellation = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await using var command = new NpgsqlCommand("SELECT is_admin FROM hifc_web.accounts WHERE id=@actor FOR SHARE", connection, transaction);
        command.Parameters.AddWithValue("actor", actorId);
        if (!Equals(await command.ExecuteScalarAsync(cancellation), true))
            throw new InvalidOperationException("只有管理员可以调整账号使用期限。");

        command.CommandText = """
            SELECT id, phone, real_name, registered_at, expires_at, is_admin, row_version, clock_timestamp()
            FROM hifc_web.accounts WHERE id=@id FOR UPDATE
            """;
        command.Parameters.Clear();
        command.Parameters.AddWithValue("id", accountId);
        AccountInfo account;
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            if (!await reader.ReadAsync(cancellation)) throw new InvalidOperationException("账号不存在。");
            account = Read(reader);
        }
        if (account.RowVersion != expectedVersion) throw new InvalidOperationException("账号期限已被其他操作更新，请刷新后重试。");
        var oldExpiry = account.ExpiresAtUtc;
        var newExpiry = AccountPolicy.Extend(oldExpiry, account.ServerTimeUtc, days, untilDate);
        command.CommandText = "UPDATE hifc_web.accounts SET expires_at=@expiry, row_version=row_version+1 WHERE id=@id AND row_version=@version";
        command.Parameters.AddWithValue("expiry", newExpiry);
        command.Parameters.AddWithValue("version", expectedVersion);
        if (await command.ExecuteNonQueryAsync(cancellation) != 1) throw new InvalidOperationException("账号状态已变化，请刷新后重试。");

        command.CommandText = """
            INSERT INTO hifc_web.account_audit
                (id, actor_id, account_id, occurred_at, old_expires_at, new_expires_at, requested_days, requested_date)
            VALUES (@audit, @actor, @account, @now, @old, @new, @days, @date)
            """;
        command.Parameters.Clear();
        command.Parameters.AddWithValue("audit", Guid.NewGuid());
        command.Parameters.AddWithValue("actor", actorId);
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.AddWithValue("now", account.ServerTimeUtc);
        command.Parameters.AddWithValue("old", oldExpiry);
        command.Parameters.AddWithValue("new", newExpiry);
        command.Parameters.AddWithValue("days", NpgsqlDbType.Integer, (object)days ?? DBNull.Value);
        command.Parameters.AddWithValue("date", NpgsqlDbType.Text, (object)untilDate ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellation);
        await transaction.CommitAsync(cancellation);
        account.ExpiresAtUtc = newExpiry;
        account.RowVersion++;
        account.IsActive = AccountPolicy.IsActive(account, account.ServerTimeUtc);
        return account;
    }

    public async Task<AccountInfo[]> ListForAdministratorAsync(Guid actorId, CancellationToken ct = default)
    {
        if ((await GetAsync(actorId, ct))?.IsAdmin != true) throw new WebOperationException("ADMIN_REQUIRED", 403, "只有管理员可以查看账号。");
        await using var command = dataSource.CreateCommand("SELECT id,phone,real_name,registered_at,expires_at,is_admin,row_version,clock_timestamp() FROM hifc_web.accounts ORDER BY registered_at DESC LIMIT 1000");
        await using var reader = await command.ExecuteReaderAsync(ct); var accounts = new List<AccountInfo>();
        while (await reader.ReadAsync(ct)) accounts.Add(Read(reader)); return accounts.ToArray();
    }

    // The target lock also serializes login, rebinding and workflow writes with deletion.
    public async Task DeleteAsync(Guid actorId, Guid accountId, long expectedVersion, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT is_admin FROM hifc_web.accounts WHERE id=@actor FOR SHARE", connection, transaction);
        command.Parameters.AddWithValue("actor", actorId);
        if (!Equals(await command.ExecuteScalarAsync(ct), true)) throw new WebOperationException("ADMIN_REQUIRED", 403, "只有管理员可以删除普通账号。");
        command.CommandText = "SELECT id,phone,real_name,registered_at,expires_at,is_admin,row_version,clock_timestamp() FROM hifc_web.accounts WHERE id=@id FOR UPDATE";
        command.Parameters.AddWithValue("id", accountId);
        AccountInfo account;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new WebOperationException("ACCOUNT_NOT_FOUND", 404, "账号已不存在，请返回账号管理。");
            account = Read(reader);
        }
        if (account.IsAdmin || actorId == accountId) throw new WebOperationException("ADMIN_PROTECTED", 403, "管理员账号不能删除。");
        if (account.RowVersion != expectedVersion) throw new WebOperationException("ACCOUNT_CHANGED", 409, "账号信息已更新，请刷新并重新确认删除。");
        command.CommandText = """
            INSERT INTO hifc_web.account_deletion_audit(id,actor_id,account_id,phone,real_name,registered_at,expires_at,row_version)
            SELECT @audit,@actor,id,phone,real_name,registered_at,expires_at,row_version FROM hifc_web.accounts WHERE id=@id;
            DELETE FROM hifc_web.workflows WHERE actor_id=@id;
            DELETE FROM hifc_web.sessions WHERE account_id=@id;
            DELETE FROM hifc_web.device_credentials WHERE account_id=@id;
            DELETE FROM hifc_web.rebind_codes WHERE account_id=@id;
            DELETE FROM hifc_web.admin_passwords WHERE account_id=@id;
            DELETE FROM hifc_web.account_audit WHERE account_id=@id OR actor_id=@id;
            DELETE FROM hifc_web.identity_audit WHERE account_id=@id OR actor_id=@id;
            DELETE FROM hifc_web.accounts WHERE id=@id;
            """;
        command.Parameters.AddWithValue("audit", Guid.NewGuid());
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }
    private static AccountInfo Read(NpgsqlDataReader reader)
    {
        var account = new AccountInfo {
            Id = reader.GetGuid(0).ToString("N"), Phone = reader.GetString(1), RealName = reader.GetString(2),
            RegisteredAtUtc = reader.GetDateTime(3), ExpiresAtUtc = reader.GetDateTime(4),
            IsAdmin = reader.GetBoolean(5), RowVersion = reader.GetInt64(6), ServerTimeUtc = reader.GetDateTime(7)
        };
        account.IsActive = AccountPolicy.IsActive(account, account.ServerTimeUtc);
        return account;
    }
}
