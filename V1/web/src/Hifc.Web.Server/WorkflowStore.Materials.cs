using Hifc.Web.Contracts;
using Hifc.Web.RuleEngine;
using System.Text.Json;

namespace Hifc.Web.Server;

public sealed partial class WorkflowStore
{
    private static void RequireEditableScan(ScanState state)
    {
        if (state.Result?.Complete != true) throw new InvalidOperationException("请先完成全面检查。");
        if (state.Receipt?.TransactionCommitted == true) throw new InvalidOperationException("模型已经写入，请重新全面检查。");
    }
    public Task<MaterialPreview> MaterialPreviewAsync(Guid actor, MaterialPreviewRequest request, CancellationToken ct = default) =>
        UseAsync(actor, request?.Context?.ScanId, false, true, true, (state, _) => {
            Match(state, request.Context); RequireEditableScan(state);
            if (state.MaterialRequests.Count >= 24) throw new InvalidOperationException("本轮资料预览过多，请重新全面检查后继续。");
            var preview = rules.PreviewMaterial(state.Result, request); preview.Context = state.Context;
            state.MaterialRequests.Add(preview.Id, request); state.MaterialPreviews.Add(preview.Id, preview);
            return preview;
        }, ct);
    private static void SelectMaterials(ScanState state, MaterialDecision[] decisions)
    {
        if (decisions == null || decisions.Length > 24 || decisions.Any(d => d == null || string.IsNullOrEmpty(d.PreviewId))
            || decisions.Select(d => d.PreviewId).Distinct().Count() != decisions.Length)
            throw new InvalidOperationException("资料选择为空、重复或超出本轮范围。");
        foreach (var decision in decisions) {
            if (!state.MaterialPreviews.TryGetValue(decision.PreviewId, out var preview) || !state.MaterialRequests.ContainsKey(decision.PreviewId))
                throw new InvalidOperationException("资料预览不存在或不属于当前模型检查，请重新导入。");
            var selected = decision.SelectedKeys ?? [];
            if (selected.Length != selected.Distinct().Count() || selected.Any(k => !preview.Changes.Any(c => c.Key == k && c.CanSelect)))
                throw new InvalidOperationException("选择了范围外、冲突未处理或不可写的资料字段。");
        }
        var accepted = decisions.Where(d => d.Accepted).OrderBy(d => d.PreviewId, StringComparer.Ordinal).ToArray();
        if (JsonSerializer.Serialize(accepted) != JsonSerializer.Serialize(state.PendingMaterials)) {
            state.PendingMaterials = accepted; state.Plan = null; state.Permit = null; state.PermitRequest = null; state.Receipt = null;
        }
    }
    private static ResolvedMaterialDecision[] ResolveMaterials(ScanState state) => (state.PendingMaterials ?? [])
        .Select(d => new ResolvedMaterialDecision { Request = state.MaterialRequests[d.PreviewId], Preview = state.MaterialPreviews[d.PreviewId], Decision = d }).ToArray();
    public Task MaterialSelectionsAsync(Guid actor, MaterialSelectionRequest request, CancellationToken ct = default) =>
        UseAsync(actor, request?.Context?.ScanId, false, true, true, (state, _) => {
            Match(state, request.Context); RequireEditableScan(state); SelectMaterials(state, request.Decisions); return true;
        }, ct);
    public Task<byte[]> WorkbookTemplateAsync(Guid actor, RequestContext context, CancellationToken ct = default) =>
        UseAsync(actor, context?.ScanId, false, true, false, (state, _) => {
            Match(state, context); RequireEditableScan(state); return rules.CreateWorkbookTemplate(state.Result);
        }, ct);
}
