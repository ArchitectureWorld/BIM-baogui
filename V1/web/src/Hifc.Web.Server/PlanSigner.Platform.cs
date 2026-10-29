using Hifc.Web.Contracts;

namespace Hifc.Web.Server;

public sealed partial class PlanSigner
{
    public PlatformLaunchPermit IssuePlatformLaunch(PlatformLaunchRequest request, AccountInfo account, DateTime nowUtc, ExportPlan plan, ExportReceipt receipt)
    {
        PlatformLaunchTrust.ValidateRequest(request);
        if (account == null || !AccountPolicy.IsActive(account, nowUtc)) throw new InvalidOperationException("账号已到期，不能开始新的平台同步；已启动任务继续运行。");
        if (plan == null || receipt == null || plan.Hash != Canonical.ExportHash(plan) || plan.Id != request.ExportPlanId || receipt.PlanId != plan.Id
            || receipt.DocumentSessionId != plan.Context?.DocumentSessionId || request.DocumentSessionId != receipt.DocumentSessionId || !receipt.Exported || !receipt.SourceRestored
            || string.IsNullOrWhiteSpace(receipt.SnapshotBefore) || receipt.SnapshotBefore != receipt.SnapshotAfter || receipt.IfcHash != request.IfcSha256)
            throw new InvalidOperationException("平台同步需要服务器已接收且与当前冻结 IFC 一致的原生导出回执。");
        var expiry = nowUtc.AddMinutes(15); if (!account.IsAdmin && account.ExpiresAtUtc < expiry) expiry = account.ExpiresAtUtc;
        var permit = new PlatformLaunchPermit { Id = Guid.NewGuid().ToString("D"), AccountId = account.Id, ExportPlanId = request.ExportPlanId, DocumentSessionId = request.DocumentSessionId,
            ProjectId = request.ProjectId, TargetModelId = request.TargetModelId, TargetGeneration = request.TargetGeneration, IfcSha256 = request.IfcSha256, RunId = request.RunId,
            MainReleaseId = request.MainReleaseId, MainSourceCommit = request.MainSourceCommit, ComponentVersion = request.ComponentVersion, NodeVersion = request.NodeVersion, ConfirmedReplacement = true,
            ExportPlanHash = plan.Hash, ReceiptIdentityHash = PlatformLaunchTrust.ReceiptIdentity(receipt), IssuedAtUnixSeconds = new DateTimeOffset(nowUtc).ToUnixTimeSeconds(), ExpiresAtUnixSeconds = new DateTimeOffset(expiry).ToUnixTimeSeconds() };
        permit.Signature = Signature(PlatformLaunchTrust.Payload(permit)); PlatformLaunchTrust.Validate(permit, request, account.Id, PublicKey, nowUtc); return permit;
    }
    public void Sign(PlatformLaunchConsumption consumption) => consumption.Signature = Signature(PlatformLaunchTrust.ConsumptionPayload(consumption));
}
