using Hifc.Web.Contracts;
using Hifc.Web.RuleEngine;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace Hifc.Web.Server;

public sealed class WorkflowStore(NpgsqlDataSource source, RuleAdapter rules, PlanSigner signer)
{
    private sealed class ScanState
    {
        public ScanState() { }
        public RequestContext Context { get; set; }
        public List<InventoryPage> Inventory { get; set; } = new();
        public CapturePlan Capture { get; set; }
        public List<CapturePage> Values { get; set; } = new();
        public CheckResult Result { get; set; }
        public EditPlan Plan { get; set; }
        public WritePermit Permit { get; set; }
        public string PermitRequest { get; set; }
        public WriteReceipt Receipt { get; set; }
    }
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var command = source.CreateCommand("""
            CREATE TABLE IF NOT EXISTS hifc_web.workflows (
                actor_id uuid NOT NULL REFERENCES hifc_web.accounts(id),
                scan_id text NOT NULL,
                plan_id text,
                payload jsonb NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
                PRIMARY KEY(actor_id, scan_id)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS workflows_plan ON hifc_web.workflows(plan_id) WHERE plan_id IS NOT NULL;
            """);
        await command.ExecuteNonQueryAsync(ct);
    }
    private static void Identifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl)) throw new InvalidOperationException("模型操作标识无效。");
    }
    private async Task<T> UseAsync<T>(Guid actor, string scanId, bool create, bool active, bool save, Func<ScanState, AccountInfo, T> operation, CancellationToken ct)
    {
        Identifier(scanId);
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 9237))", connection, transaction);
        command.Parameters.AddWithValue("key", actor.ToString("N") + ":" + scanId); await command.ExecuteNonQueryAsync(ct);
        command.CommandText = "SELECT is_admin, expires_at, clock_timestamp() FROM hifc_web.accounts WHERE id=@actor FOR SHARE";
        command.Parameters.Clear(); command.Parameters.AddWithValue("actor", actor);
        AccountInfo account;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("请重新登录。");
            account = new AccountInfo { Id = actor.ToString("N"), IsAdmin = reader.GetBoolean(0), ExpiresAtUtc = reader.GetDateTime(1), ServerTimeUtc = reader.GetDateTime(2) };
        }
        if (active && !AccountPolicy.IsActive(account, account.ServerTimeUtc)) throw new InvalidOperationException("账号已到期，请联系管理员延期；本地草稿仍然保留。");
        command.CommandText = "SELECT payload::text FROM hifc_web.workflows WHERE actor_id=@actor AND scan_id=@scan FOR UPDATE";
        command.Parameters.AddWithValue("scan", scanId);
        var stored = await command.ExecuteScalarAsync(ct) as string;
        if (stored == null && !create) throw new InvalidOperationException("检查记录不存在，或不属于当前账号。");
        var state = stored == null ? new ScanState() : JsonSerializer.Deserialize<ScanState>(stored);
        var result = operation(state, account);
        if (save)
        {
            command.CommandText = """
                INSERT INTO hifc_web.workflows(actor_id,scan_id,plan_id,payload) VALUES(@actor,@scan,@plan,@payload)
                ON CONFLICT(actor_id,scan_id) DO UPDATE SET plan_id=excluded.plan_id,payload=excluded.payload,updated_at=clock_timestamp()
                """;
            command.Parameters.AddWithValue("plan", NpgsqlDbType.Text, (object)state.Plan?.Id ?? DBNull.Value);
            command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(state));
            await command.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct); return result;
    }
    private static void Match(ScanState state, RequestContext context)
    {
        if (context == null) throw new InvalidOperationException("模型请求上下文缺失。");
        ProtocolGuard.Match(state.Context, context);
    }
    public Task<CapturePlan> StartAsync(Guid actor, InventoryPage page, CancellationToken ct = default)
    {
        if (page?.Context == null || page.Elements == null || page.Elements.Length > 256 || page.PageCount < 1 || page.PageCount > 10000
            || page.PageIndex < 0 || page.PageIndex >= page.PageCount || page.TotalElements < 0 || page.TotalElements > 2000000 || page.Elements.Any(e => e == null)
            || page.PageHash != Canonical.InventoryHash(page.Elements)) throw new InvalidOperationException("模型盘点页无效。");
        return UseAsync(actor, page.Context.ScanId, true, true, true, (state, _) => {
            state.Context ??= page.Context; Match(state, page.Context);
            if (state.Inventory.Count > 0 && (state.Inventory[0].PageCount != page.PageCount || state.Inventory[0].TotalElements != page.TotalElements
                || state.Inventory[0].Stage != page.Stage || state.Inventory[0].ModelKind != page.ModelKind)) throw new InvalidOperationException("盘点阶段、范围或总数已变化。");
            var previous = state.Inventory.SingleOrDefault(p => p.PageIndex == page.PageIndex);
            if (previous != null && JsonSerializer.Serialize(previous) != JsonSerializer.Serialize(page)) throw new InvalidOperationException("盘点页重试内容不一致。");
            if (previous == null) state.Inventory.Add(page);
            if (state.Inventory.Count != page.PageCount) return new CapturePlan { Context = state.Context, InventoryComplete = false };
            state.Capture ??= rules.CreateCapturePlan(state.Inventory.ToArray()); return state.Capture;
        }, ct);
    }
    public Task CaptureAsync(Guid actor, CapturePage page, CancellationToken ct = default)
    {
        if (page?.Context == null || page.Values == null || page.Values.Length > 1024 || page.PageCount < 1 || page.PageCount > 10000
            || page.PageIndex < 0 || page.PageIndex >= page.PageCount || page.TotalValues < 0 || page.TotalValues > 2000000 || page.Values.Any(v => v == null)
            || page.PageHash != Canonical.ValuesHash(page.Values)) throw new InvalidOperationException("字段取数页无效。");
        return UseAsync(actor, page.Context.ScanId, false, true, true, (state, _) => {
            Match(state, page.Context);
            if (state.Capture?.InventoryComplete != true) throw new InvalidOperationException("盘点数据尚未齐备。");
            if (state.Values.Count > 0 && (state.Values[0].PageCount != page.PageCount || state.Values[0].TotalValues != page.TotalValues)) throw new InvalidOperationException("字段页总数不一致。");
            var previous = state.Values.SingleOrDefault(p => p.PageIndex == page.PageIndex);
            if (previous != null && JsonSerializer.Serialize(previous) != JsonSerializer.Serialize(page)) throw new InvalidOperationException("字段页重试内容不一致。");
            if (previous == null)
            {
                if (state.Result != null) throw new InvalidOperationException("本轮检查已完成，请重新开始扫描。");
                state.Values.Add(page);
            }
            if (state.Values.Count == page.PageCount) ProtocolGuard.CapturePages(state.Values.ToArray(), state.Capture.Targets);
            return true;
        }, ct);
    }
    public Task<CheckResult> CompleteAsync(Guid actor, RequestContext context, CancellationToken ct = default) =>
        UseAsync(actor, context?.ScanId, false, true, true, (state, _) => {
            Match(state, context);
            if (state.Capture?.InventoryComplete != true) throw new InvalidOperationException("模型盘点尚未齐备。");
            state.Result ??= rules.Evaluate(state.Inventory.ToArray(), state.Capture, state.Values.ToArray()); return state.Result;
        }, ct);
    public Task<EditPlan> PreviewAsync(Guid actor, PreviewRequest request, CancellationToken ct = default) =>
        UseAsync(actor, request?.Context?.ScanId, false, true, true, (state, account) => {
            Match(state, request.Context);
            if (state.Result == null) throw new InvalidOperationException("请先完成全面检查。");
            if (state.Receipt?.TransactionCommitted == true) throw new InvalidOperationException("模型已经写入，请重新检查后再创建方案。");
            state.Plan = rules.BuildEditPlan(state.Result, state.Values.SelectMany(p => p.Values).ToArray(), request.Drafts, request.IncludeSuggestions, account.ServerTimeUtc);
            signer.Sign(state.Plan); state.Permit = null; state.PermitRequest = null; state.Receipt = null; return state.Plan;
        }, ct);
    private async Task<string> ScanForPlan(Guid actor, string planId, CancellationToken ct)
    {
        Identifier(planId);
        await using var command = source.CreateCommand("SELECT scan_id FROM hifc_web.workflows WHERE actor_id=@actor AND plan_id=@plan");
        command.Parameters.AddWithValue("actor", actor); command.Parameters.AddWithValue("plan", planId);
        return await command.ExecuteScalarAsync(ct) as string ?? throw new InvalidOperationException("方案不存在、已更新，或不属于当前账号。");
    }
    public async Task<WritePermit> AuthorizeAsync(Guid actor, string planId, AuthorizeRequest request, CancellationToken ct = default)
    {
        Identifier(request?.IdempotencyKey);
        return await UseAsync(actor, await ScanForPlan(actor, planId, ct), false, true, true, (state, account) => {
            if (state.Plan?.Id != planId || state.Plan.Hash != request.PlanHash || state.Receipt != null) throw new InvalidOperationException("方案不匹配或已使用。");
            if (state.Permit != null)
            {
                if (state.PermitRequest != request.IdempotencyKey || account.ServerTimeUtc >= state.Permit.ExpiresAtUtc) throw new InvalidOperationException("许可已使用或过期，请重新生成方案。");
                return state.Permit;
            }
            state.Permit = signer.IssuePermit(state.Plan, account, account.ServerTimeUtc, Guid.NewGuid().ToString("N"));
            state.PermitRequest = request.IdempotencyKey; return state.Permit;
        }, ct);
    }
    public async Task ReceiptAsync(Guid actor, WriteReceipt receipt, CancellationToken ct = default)
    {
        if (receipt == null || receipt.ReadbackPassed && !receipt.TransactionCommitted || receipt.RecheckCompleted && !receipt.ReadbackPassed) throw new InvalidOperationException("写入回执状态不一致。");
        await UseAsync(actor, await ScanForPlan(actor, receipt.PlanId, ct), false, false, true, (state, _) => {
            if (state.Plan?.Id != receipt.PlanId || state.Permit?.Id != receipt.PermitId || receipt.DocumentSessionId != state.Context.DocumentSessionId) throw new InvalidOperationException("写入回执不属于本次许可或文档。");
            if (state.Receipt != null && (state.Receipt.TransactionCommitted != receipt.TransactionCommitted || state.Receipt.ReadbackPassed != receipt.ReadbackPassed)) throw new InvalidOperationException("回执重试的写入结果不一致。");
            state.Receipt = receipt; return true;
        }, ct);
    }
    public Task<CheckResult> GetResultAsync(Guid actor, string scanId, CancellationToken ct = default) => UseAsync(actor, scanId, false, false, false,
        (state, _) => state.Result ?? throw new InvalidOperationException("检查尚未完成。"), ct);
    public async Task<WriteReceipt> GetReceiptAsync(Guid actor, string planId, CancellationToken ct = default) => await UseAsync(actor, await ScanForPlan(actor, planId, ct), false, false, false,
        (state, _) => state.Plan?.Id == planId && state.Receipt != null ? state.Receipt : throw new InvalidOperationException("写入回执尚未收到。"), ct);
}
