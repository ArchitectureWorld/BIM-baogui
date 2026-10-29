using System;
using System.Linq;
using Hifc.Reference;

internal static class SiteClassificationTests
{
    static void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
    public static void Run(Action<string, Action> check)
    {
        check("green_slab_alias_prepares_without_manual_confirmation", () => {
            var row = new ModelElementCheck { Name = "绿化", TypeName = "绿化", Entity = "IfcSlab" };
            var candidate = CaseWorkflow.GetCandidates(row, "总图").Single();
            Assert(candidate.Slot == "绿地" && candidate.CanPrepare && !candidate.RequiresReview, "Green slab still needs manual classification");
        });
        check("green_type_name_alone_prepares_the_slab", () => {
            var candidate = CaseWorkflow.GetCandidates(new ModelElementCheck { Name = "楼板 18", TypeName = "绿化", Entity = "IfcSlab" }, "总图").Single();
            Assert(candidate.Slot == "绿地" && candidate.CanPrepare, "Green type did not provide the default");
        });
        check("green_alias_does_not_prepare_unsupported_carriers", () => {
            var candidate = CaseWorkflow.GetCandidates(new ModelElementCheck { Name = "绿化", Entity = "IfcBuildingElementProxy" }, "总图").Single();
            Assert(!candidate.CanPrepare && candidate.RequiresReview, "Non-slab greenery was silently classified");
        });
        check("green_name_preserves_an_explicit_business_class", () => {
            Assert(CaseWorkflow.GetCandidates(new ModelElementCheck { Name = "绿化", Entity = "IfcSlab", Slot = "道路红线" }, "总图").Length == 0, "Existing manual business class was overwritten");
        });
        check("green_defaults_remain_in_site_model_scope", () => {
            Assert(CaseWorkflow.GetCandidates(new ModelElementCheck { Name = "绿化", Entity = "IfcSlab" }, "单体建筑").Length == 0, "Site default leaked into building scope");
        });
        check("manual_planning_control_accepts_slabs_and_rejects_spaces", () => {
            var method = typeof(CaseWorkflow).GetMethod("CanClassify");
            Assert(method != null, "Manual carrier policy is missing");
            var rule = CaseWorkflow.ForModel("报规", "总图").Single(r => r.Slot == "规划控制线");
            Assert((bool)method.Invoke(null, new object[] { rule, new ModelElementCheck { Entity = "IfcSlab" } }), "Slab cannot be manually assigned to planning control");
            Assert(!(bool)method.Invoke(null, new object[] { rule, new ModelElementCheck { Entity = "IfcSpace", AreaScheme = "停车" } }), "Area was allowed to become a control-line proxy");
        });
    }
}
