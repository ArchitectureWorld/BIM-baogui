using Hifc.Web.Contracts;
using NpgsqlTypes;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hifc.Web.Server;

public sealed class PlatformExportRecord
{
    public ExportPlan Plan { get; set; }
    public ExportReceipt Receipt { get; set; }
}
public sealed partial class WorkflowStore
{
    public async Task<ExportPlan> PreviewExportAsync(Guid actor, ExportRequest request, CancellationToken ct = default)
    {
        var plan = await UseAsync(actor, request?.Context?.ScanId, false, true, false, (state, account) => {
            Match(state, request.Context); RequireEditableScan(state);
            if (state.PendingMaterials.Length > 0 || request.HasPendingMaterials) throw new InvalidOperationException("资料尚未提交，请先写入并全面复检，再导出 IFC。");
            var value = rules.BuildExportPlan(state.Result, state.Values.SelectMany(p => p.Values).ToArray(), request, account.ServerTimeUtc);
            signer.Sign(value); return value;
        }, ct);
        await using var command = source.CreateCommand("INSERT INTO hifc_web.exports(id,actor_id,scan_id,plan) VALUES(@id,@actor,@scan,@plan)");
        command.Parameters.AddWithValue("id", plan.Id); command.Parameters.AddWithValue("actor", actor); command.Parameters.AddWithValue("scan", plan.Context.ScanId);
        command.Parameters.AddWithValue("plan", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(plan)); await command.ExecuteNonQueryAsync(ct);
        return plan;
    }
    public async Task ExportReceiptAsync(Guid actor, ExportReceipt receipt, CancellationToken ct = default)
    {
        Identifier(receipt?.PlanId);
        if (!receipt.Exported || !receipt.SourceRestored || string.IsNullOrWhiteSpace(receipt.SnapshotBefore) || receipt.SnapshotBefore != receipt.SnapshotAfter
            || !Regex.IsMatch(receipt.IfcHash ?? "", "^[a-fA-F0-9]{64}$") || string.IsNullOrWhiteSpace(receipt.IfcPath))
            throw new InvalidOperationException("只有源模型已恢复并成功发布的 IFC 可以登记同步交接。");
        await using var connection = await source.OpenConnectionAsync(ct); await using var tx = await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand(); command.Transaction = tx;
        command.CommandText = "SELECT plan::text,receipt::text FROM hifc_web.exports WHERE id=@id AND actor_id=@actor FOR UPDATE";
        command.Parameters.AddWithValue("id", receipt.PlanId); command.Parameters.AddWithValue("actor", actor);
        ExportPlan plan; ExportReceipt prior;
        await using (var reader = await command.ExecuteReaderAsync(ct)) {
            if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("导出方案不存在或不属于当前账号。");
            plan = JsonSerializer.Deserialize<ExportPlan>(reader.GetString(0)); prior = reader.IsDBNull(1) ? null : JsonSerializer.Deserialize<ExportReceipt>(reader.GetString(1));
        }
        if (receipt.DocumentSessionId != plan.Context.DocumentSessionId || plan.Hash != Canonical.ExportHash(plan)) throw new InvalidOperationException("导出回执不属于当前文档或签名方案。");
        if (prior != null && JsonSerializer.Serialize(prior) != JsonSerializer.Serialize(receipt)) throw new InvalidOperationException("导出回执已经登记，不能更改已冻结的 IFC。");
        command.CommandText = "UPDATE hifc_web.exports SET receipt=@receipt,updated_at=clock_timestamp() WHERE id=@id AND actor_id=@actor";
        command.Parameters.AddWithValue("receipt", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(receipt)); await command.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }
    public async Task<PlatformExportRecord> GetExportRecordAsync(Guid actor, string exportPlanId, CancellationToken ct = default)
    {
        Identifier(exportPlanId);
        await using var command = source.CreateCommand("SELECT plan::text,receipt::text FROM hifc_web.exports WHERE actor_id=@actor AND id=@id");
        command.Parameters.AddWithValue("actor", actor); command.Parameters.AddWithValue("id", exportPlanId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(1)) throw new InvalidOperationException("成功导出回执不存在或不属于当前账号。");
        var result = new PlatformExportRecord { Plan = JsonSerializer.Deserialize<ExportPlan>(reader.GetString(0)), Receipt = JsonSerializer.Deserialize<ExportReceipt>(reader.GetString(1)) };
        if (!result.Receipt.Exported || !result.Receipt.SourceRestored || result.Receipt.SnapshotBefore != result.Receipt.SnapshotAfter || result.Plan.Hash != Canonical.ExportHash(result.Plan))
            throw new InvalidOperationException("导出证据无效，不能进入平台同步。");
        return result;
    }
}
