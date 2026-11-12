using Hifc.Reference;
using Hifc.Web.Contracts;

namespace Hifc.Web.RuleEngine;

public sealed partial class RuleAdapter
{
    public static void RequirePlatformExport(ExportPlan plan, ExportReceipt receipt)
    {
        var proof = plan?.InputValidation;
        if (proof == null || receipt?.InputValidation == null || plan.Context?.RuleVersion != new RuleAdapter().RuleVersion
            || RequiredInputProof.Identity(proof) != RequiredInputProof.Identity(receipt.InputValidation))
            throw new InvalidOperationException("请使用当前 Web 规则重新全面检查并导出，平台同步需要本次必填检查通过记录。");
        RequiredInputValidation.RequireReceipt(new Dictionary<string, object> {
            ["schema"] = proof.schema, ["rule_profile_id"] = proof.rule_profile_id, ["rule_revision"] = proof.rule_revision,
            ["rule_evidence_sha256"] = proof.rule_evidence_sha256, ["complete"] = proof.complete, ["model_kind"] = proof.model_kind,
            ["checked_objects"] = proof.checked_objects, ["checked_fields"] = proof.checked_fields, ["blocking_count"] = proof.blocking_count
        });
    }
}
