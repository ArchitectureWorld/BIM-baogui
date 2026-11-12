using Hifc.Web.Contracts;
using Npgsql;
using System.Text.RegularExpressions;

namespace Hifc.Web.Server;

public sealed class PlatformLaunchStore(NpgsqlDataSource source, PlanSigner signer, AccountStore accounts)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var command = source.CreateCommand("""
            CREATE TABLE IF NOT EXISTS hifc_web.platform_launch_permits (
                account_id uuid NOT NULL,
                run_id uuid NOT NULL,
                permit_id uuid NOT NULL UNIQUE,
                payload_hash text NOT NULL,
                expires_at timestamptz NOT NULL,
                consumed_at timestamptz,
                consumer_nonce text,
                PRIMARY KEY(account_id, run_id)
            );
            """);
        await command.ExecuteNonQueryAsync(ct);
    }
    public async Task<PlatformLaunchPermit> IssueAsync(PlatformLaunchRequest request, AccountInfo actor, ExportPlan plan, ExportReceipt receipt, CancellationToken ct = default)
    {
        Hifc.Web.RuleEngine.RuleAdapter.RequirePlatformExport(plan, receipt);
        var account = await accounts.GetAsync(Guid.Parse(actor.Id), ct);
        var permit = signer.IssuePlatformLaunch(request, account, account?.ServerTimeUtc ?? DateTime.UtcNow, plan, receipt);
        await using var command = source.CreateCommand("""
            INSERT INTO hifc_web.platform_launch_permits(account_id,run_id,permit_id,payload_hash,expires_at)
            VALUES(@account,@run,@permit,@hash,@expires)
            ON CONFLICT(account_id,run_id) DO UPDATE SET permit_id=excluded.permit_id,payload_hash=excluded.payload_hash,expires_at=excluded.expires_at
            WHERE hifc_web.platform_launch_permits.consumed_at IS NULL RETURNING permit_id
            """);
        command.Parameters.AddWithValue("account", Guid.Parse(permit.AccountId)); command.Parameters.AddWithValue("run", Guid.Parse(permit.RunId)); command.Parameters.AddWithValue("permit", Guid.Parse(permit.Id));
        command.Parameters.AddWithValue("hash", Canonical.Hash(PlatformLaunchTrust.Payload(permit))); command.Parameters.AddWithValue("expires", DateTimeOffset.FromUnixTimeSeconds(permit.ExpiresAtUnixSeconds).UtcDateTime);
        if (await command.ExecuteScalarAsync(ct) == null) throw new InvalidOperationException("该同步任务已经领取启动许可，请只读刷新状态，不能重新提交 apply。");
        return permit;
    }
    public async Task<PlatformLaunchConsumption> ConsumeAsync(PlatformConsumeRequest request, CancellationToken ct = default)
    {
        var permit = request?.Permit;
        if (!Regex.IsMatch(request?.ConsumerNonce ?? "", "^[a-f0-9]{64}$")) throw new InvalidOperationException("平台启动实例标识无效。");
        PlatformLaunchTrust.Validate(permit, permit, permit?.AccountId, signer.PublicKey, DateTime.UtcNow);
        var hash = Canonical.Hash(PlatformLaunchTrust.Payload(permit));
        await using var command = source.CreateCommand("""
            WITH instant AS MATERIALIZED (SELECT clock_timestamp() AS utc)
            UPDATE hifc_web.platform_launch_permits p SET consumed_at=instant.utc,consumer_nonce=@nonce FROM instant
            WHERE p.account_id=@account AND p.run_id=@run AND p.permit_id=@permit AND p.payload_hash=@hash
                AND p.consumed_at IS NULL AND p.expires_at>instant.utc
                AND EXISTS(SELECT 1 FROM hifc_web.accounts a WHERE a.id=p.account_id AND (a.is_admin OR a.expires_at>instant.utc))
            RETURNING p.consumed_at
            """);
        command.Parameters.AddWithValue("account", Guid.Parse(permit.AccountId)); command.Parameters.AddWithValue("run", Guid.Parse(permit.RunId)); command.Parameters.AddWithValue("permit", Guid.Parse(permit.Id));
        command.Parameters.AddWithValue("hash", hash); command.Parameters.AddWithValue("nonce", request.ConsumerNonce);
        if (await command.ExecuteScalarAsync(ct) is not DateTime consumed) throw new InvalidOperationException("启动许可已使用、账号已到期或任务不匹配；请刷新状态，不要重复替换模型。");
        var response = new PlatformLaunchConsumption { PermitId = permit.Id, PermitHash = hash, AccountId = permit.AccountId, RunId = permit.RunId, ConsumerNonce = request.ConsumerNonce, ConsumedAtUnixSeconds = new DateTimeOffset(consumed).ToUnixTimeSeconds() };
        signer.Sign(response); return response;
    }
}
