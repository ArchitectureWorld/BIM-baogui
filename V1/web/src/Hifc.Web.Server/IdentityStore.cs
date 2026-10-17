using Hifc.Web.Contracts;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

namespace Hifc.Web.Server;

public sealed class IdentityStore(NpgsqlDataSource source)
{
    // Out-of-band initialization only. No public registration request can enter this method.
    public async Task<AccountInfo> CreateAdministratorAsync(string phone, string name, string password, CancellationToken ct = default)
    {
        phone = AccountPolicy.Phone(phone); name = AccountPolicy.RealName(name);
        if (password == null || password.Length < 12 || password.Length > 128 || password.Any(char.IsControl)) throw new InvalidOperationException("管理员密码须为 12–128 个字符。");
        var salt = RandomNumberGenerator.GetBytes(16); var digest = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600000, HashAlgorithmName.SHA256, 32);
        await using var connection = await source.OpenConnectionAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(58739482051003)", connection, transaction); await command.ExecuteNonQueryAsync(ct);
        command.CommandText = "SELECT count(*) FROM hifc_web.admin_passwords";
        if ((long)await command.ExecuteScalarAsync(ct) != 0) throw new InvalidOperationException("管理员已经初始化，不能重复创建。");
        command.CommandText = """
            WITH instant AS MATERIALIZED (SELECT clock_timestamp() AS utc)
            INSERT INTO hifc_web.accounts(id,phone,real_name,registered_at,expires_at,is_admin)
            SELECT @id,@phone,@name,utc,utc+interval '168 hours',true FROM instant
            ON CONFLICT(phone) DO UPDATE SET is_admin=true RETURNING id
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid()); command.Parameters.AddWithValue("phone", phone); command.Parameters.AddWithValue("name", name);
        var id = (Guid)await command.ExecuteScalarAsync(ct);
        command.CommandText = "INSERT INTO hifc_web.admin_passwords(account_id,salt,digest) VALUES(@account,@salt,@digest); INSERT INTO hifc_web.identity_audit(actor_id,account_id,action) VALUES(@account,@account,'BOOTSTRAP_ADMIN')";
        command.Parameters.AddWithValue("account", id); command.Parameters.AddWithValue("salt", salt); command.Parameters.AddWithValue("digest", digest); await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct); return await new AccountStore(source).GetAsync(id, ct);
    }
    public async Task<AccountInfo> AdministratorLoginAsync(string phone, string password, CancellationToken ct = default)
    {
        phone = AccountPolicy.Phone(phone);
        if (password == null || password.Length > 128) throw Refused();
        await using var command = source.CreateCommand("SELECT a.id,p.salt,p.digest FROM hifc_web.accounts a JOIN hifc_web.admin_passwords p ON p.account_id=a.id WHERE a.phone=@phone AND a.is_admin"); command.Parameters.AddWithValue("phone", phone);
        Guid id; byte[] salt, expected;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct)) { id = reader.GetGuid(0); salt = reader.GetFieldValue<byte[]>(1); expected = reader.GetFieldValue<byte[]>(2); }
            else { id = Guid.Empty; salt = new byte[16]; expected = new byte[32]; }
        }
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600000, HashAlgorithmName.SHA256, 32);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected) || id == Guid.Empty) throw Refused();
        return await new AccountStore(source).GetAsync(id, ct);
    }
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT pg_advisory_xact_lock(58739482051002);
            CREATE TABLE IF NOT EXISTS hifc_web.device_credentials(account_id uuid PRIMARY KEY REFERENCES hifc_web.accounts(id), digest bytea NOT NULL CHECK(octet_length(digest)=32));
            CREATE TABLE IF NOT EXISTS hifc_web.sessions(digest bytea PRIMARY KEY, account_id uuid NOT NULL REFERENCES hifc_web.accounts(id), expires_at timestamptz NOT NULL);
            CREATE TABLE IF NOT EXISTS hifc_web.rebind_codes(account_id uuid PRIMARY KEY REFERENCES hifc_web.accounts(id), digest bytea NOT NULL, expires_at timestamptz NOT NULL, used_at timestamptz);
            CREATE TABLE IF NOT EXISTS hifc_web.admin_passwords(account_id uuid PRIMARY KEY REFERENCES hifc_web.accounts(id), salt bytea NOT NULL, digest bytea NOT NULL);
            CREATE TABLE IF NOT EXISTS hifc_web.identity_audit(id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, actor_id uuid NOT NULL REFERENCES hifc_web.accounts(id), account_id uuid NOT NULL REFERENCES hifc_web.accounts(id), action text NOT NULL, occurred_at timestamptz NOT NULL DEFAULT clock_timestamp());
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(ct); await transaction.CommitAsync(ct);
    }
    private static byte[] CredentialHash(string value)
    {
        try { var bytes = Convert.FromBase64String(value ?? ""); if (bytes.Length != 32) throw new FormatException(); return SHA256.HashData(bytes); }
        catch (FormatException) { throw new InvalidOperationException("电脑凭证无效，请从 Web 插件登记或重新绑定。"); }
    }
    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private static string RandomToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static InvalidOperationException Refused() => new WebOperationException("IDENTITY_REJECTED", 401, "登录信息无效；已有账号请使用原电脑，或联系管理员重新绑定。");
    private static AccountInfo Read(NpgsqlDataReader reader)
    {
        var account = new AccountInfo { Id = reader.GetGuid(0).ToString("N"), Phone = reader.GetString(1), RealName = reader.GetString(2), RegisteredAtUtc = reader.GetDateTime(3), ExpiresAtUtc = reader.GetDateTime(4), IsAdmin = reader.GetBoolean(5), RowVersion = reader.GetInt64(6), ServerTimeUtc = reader.GetDateTime(7) };
        account.IsActive = AccountPolicy.IsActive(account, account.ServerTimeUtc); return account;
    }
    private static async Task<AccountSession> Session(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken ct)
    {
        var token = RandomToken();
        await using var command = new NpgsqlCommand("DELETE FROM hifc_web.sessions WHERE expires_at < clock_timestamp(); INSERT INTO hifc_web.sessions(digest,account_id,expires_at) VALUES(@digest,@id,clock_timestamp()+interval '12 hours') RETURNING expires_at", connection, transaction);
        command.Parameters.AddWithValue("digest", Hash(token)); command.Parameters.AddWithValue("id", id);
        var expiry = (DateTime)await command.ExecuteScalarAsync(ct);
        command.CommandText = "SELECT id,phone,real_name,registered_at,expires_at,is_admin,row_version,clock_timestamp() FROM hifc_web.accounts WHERE id=@id";
        await using var reader = await command.ExecuteReaderAsync(ct); await reader.ReadAsync(ct);
        return new AccountSession { Account = Read(reader), AccessToken = token, SessionExpiresAtUtc = expiry };
    }
    public async Task<AccountSession> RegisterAsync(Registration request, CancellationToken ct = default)
    {
        var phone = AccountPolicy.Phone(request?.Phone); var name = AccountPolicy.RealName(request?.RealName); var digest = CredentialHash(request?.Credential);
        await using var connection = await source.OpenConnectionAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@phone, 57293))", connection, transaction);
        command.Parameters.AddWithValue("phone", phone); await command.ExecuteNonQueryAsync(ct);
        command.CommandText = "SELECT a.id,c.digest FROM hifc_web.accounts a LEFT JOIN hifc_web.device_credentials c ON c.account_id=a.id WHERE a.phone=@phone FOR UPDATE OF a";
        Guid id;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                id = reader.GetGuid(0);
                if (reader.IsDBNull(1) || !CryptographicOperations.FixedTimeEquals(reader.GetFieldValue<byte[]>(1), digest)) throw new WebOperationException("ALREADY_REGISTERED", 409, "手机号已登记，请在原电脑登录，或联系管理员领取一次性绑定码。");
            }
            else id = Guid.Empty;
        }
        if (id == Guid.Empty)
        {
            id = Guid.NewGuid(); command.CommandText = """
                WITH instant AS MATERIALIZED (SELECT clock_timestamp() AS utc)
                INSERT INTO hifc_web.accounts(id,phone,real_name,registered_at,expires_at) SELECT @id,@phone,@name,utc,utc+interval '168 hours' FROM instant;
                INSERT INTO hifc_web.device_credentials(account_id,digest) VALUES(@id,@digest);
                INSERT INTO hifc_web.identity_audit(actor_id,account_id,action) VALUES(@id,@id,'REGISTER');
                """;
            command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("name", name); command.Parameters.AddWithValue("digest", digest); await command.ExecuteNonQueryAsync(ct);
        }
        var session = await Session(connection, transaction, id, ct); await transaction.CommitAsync(ct); return session;
    }
    public async Task<AccountSession> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var phone = AccountPolicy.Phone(request?.Phone); var digest = CredentialHash(request?.Credential);
        await using var connection = await source.OpenConnectionAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT a.id,c.digest FROM hifc_web.accounts a JOIN hifc_web.device_credentials c ON c.account_id=a.id WHERE phone=@phone FOR SHARE OF a,c", connection, transaction);
        command.Parameters.AddWithValue("phone", phone); Guid id;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct) || !CryptographicOperations.FixedTimeEquals(reader.GetFieldValue<byte[]>(1), digest)) throw Refused(); id = reader.GetGuid(0);
        }
        var session = await Session(connection, transaction, id, ct); await transaction.CommitAsync(ct); return session;
    }
    public async Task<AccountInfo> ResolveAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 256) return null;
        await using var command = source.CreateCommand("SELECT a.id,a.phone,a.real_name,a.registered_at,a.expires_at,a.is_admin,a.row_version,clock_timestamp() FROM hifc_web.sessions s JOIN hifc_web.accounts a ON a.id=s.account_id WHERE s.digest=@digest AND s.expires_at>clock_timestamp()");
        command.Parameters.AddWithValue("digest", Hash(token)); await using var reader = await command.ExecuteReaderAsync(ct); return await reader.ReadAsync(ct) ? Read(reader) : null;
    }
    public async Task LogoutAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 256) return;
        await using var command = source.CreateCommand("DELETE FROM hifc_web.sessions WHERE digest=@digest"); command.Parameters.AddWithValue("digest", Hash(token)); await command.ExecuteNonQueryAsync(ct);
    }
    public async Task<string> IssueRebindAsync(Guid actor, Guid account, CancellationToken ct = default)
    {
        var code = RandomToken();
        await using var connection = await source.OpenConnectionAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT is_admin FROM hifc_web.accounts WHERE id=@actor FOR SHARE", connection, transaction); command.Parameters.AddWithValue("actor", actor);
        if (!Equals(await command.ExecuteScalarAsync(ct), true)) throw new WebOperationException("ADMIN_REQUIRED", 403, "只有管理员可以重新绑定。");
        command.CommandText = "SELECT is_admin FROM hifc_web.accounts WHERE id=@account FOR UPDATE"; command.Parameters.AddWithValue("account", account);
        if (!Equals(await command.ExecuteScalarAsync(ct), false)) throw new InvalidOperationException("普通账号不存在。");
        command.CommandText = """
            INSERT INTO hifc_web.rebind_codes(account_id,digest,expires_at) VALUES(@account,@digest,clock_timestamp()+interval '10 minutes')
            ON CONFLICT(account_id) DO UPDATE SET digest=excluded.digest,expires_at=excluded.expires_at,used_at=NULL;
            INSERT INTO hifc_web.identity_audit(actor_id,account_id,action) VALUES(@actor,@account,'ISSUE_REBIND');
            """;
        command.Parameters.AddWithValue("digest", Hash(code)); await command.ExecuteNonQueryAsync(ct); await transaction.CommitAsync(ct); return code;
    }
    public async Task<AccountSession> RebindAsync(RebindRequest request, CancellationToken ct = default)
    {
        var phone = AccountPolicy.Phone(request?.Phone); var digest = CredentialHash(request?.Credential);
        if (string.IsNullOrEmpty(request.Code) || request.Code.Length > 128) throw Refused();
        await using var connection = await source.OpenConnectionAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT a.id,r.digest,r.expires_at,r.used_at,clock_timestamp() FROM hifc_web.accounts a JOIN hifc_web.rebind_codes r ON r.account_id=a.id WHERE a.phone=@phone AND NOT a.is_admin FOR UPDATE OF a,r", connection, transaction);
        command.Parameters.AddWithValue("phone", phone); Guid id;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct) || !reader.IsDBNull(3) || reader.GetDateTime(2) <= reader.GetDateTime(4) || !CryptographicOperations.FixedTimeEquals(reader.GetFieldValue<byte[]>(1), Hash(request.Code))) throw Refused();
            id = reader.GetGuid(0);
        }
        command.CommandText = """
            UPDATE hifc_web.rebind_codes SET used_at=clock_timestamp() WHERE account_id=@id;
            INSERT INTO hifc_web.device_credentials(account_id,digest) VALUES(@id,@digest) ON CONFLICT(account_id) DO UPDATE SET digest=excluded.digest;
            DELETE FROM hifc_web.sessions WHERE account_id=@id;
            INSERT INTO hifc_web.identity_audit(actor_id,account_id,action) VALUES(@id,@id,'USE_REBIND');
            """;
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("digest", digest); await command.ExecuteNonQueryAsync(ct);
        var session = await Session(connection, transaction, id, ct); await transaction.CommitAsync(ct); return session;
    }
}

public sealed class WebOperationException(string code, int status, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
