using Hifc.Web.Contracts;

namespace Hifc.Web.Server;

public sealed partial class PlanSigner
{
    public void Sign(ExportPlan plan)
    {
        ProtocolGuard.Match(plan.Context, plan.Context);
        plan.Hash = Canonical.ExportHash(plan); plan.Signature = Signature(plan.Hash);
    }
}
