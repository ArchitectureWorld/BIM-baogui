using System;
using System.Linq;
using System.Reflection;
using Hifc.Reference;

internal static class BuildingCaseTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static ReferenceProperty[] Plan(string entity, string slot, bool typeEvidence)
    {
        var type = typeof(CaseWorkflow).Assembly.GetType("Hifc.Reference.CaseExportPolicy");
        Assert(type != null, "Case export plan is missing");
        return (ReferenceProperty[])type.GetMethod("Properties",new[]{typeof(string),typeof(string),typeof(bool)}).Invoke(null, new object[] { entity, slot, typeEvidence });
    }
    public static void Run(Action<string, Action> check)
    {
        check("building_slab_exports_only_explicit_business_pset", () => {
            var p = Plan("IfcSlab", "建筑区域", true);
            Assert(p.Any(x => x.PropertySet == "Pset_建筑区域信息属性集" && x.Binding == "实例"), "slab instance set missing");
            Assert(p.All(x => x.PropertySet != "Pset_总平构件信息属性集"), "unrelated type text invented total-plan business");
        });
        check("unclassified_slab_with_type_evidence_does_not_invent_building_region", () => {
            var p = Plan("IfcSlab", null, true);
            Assert(p.Length == 0, "type-only slab incorrectly classified");
            Assert(Plan("IfcSlab", null, false).Length == 0, "blank slab is not business evidence");
        });
        check("case_type_binding_reaches_export_configuration_without_mutating_baseline", () => {
            var rule = CaseWorkflow.ForModel("报规", "单体建筑").Single(r => r.Slot == "窗");
            Assert(rule.Properties.All(p => p.Binding == "类型"), "type binding lost in property definitions");
            Assert(ReferenceCatalog.FromEmbeddedBaseline().ForSlot("窗").All(p => p.Binding == "实例"), "frozen evidence was mutated");
        });
        check("type_configuration_targets_native_ifc_type_carriers", () => {
            var p = Plan("IfcSlab", "建筑区域", true);
            var config = ReferenceCatalog.FromEmbeddedBaseline().BuildSelectedExportConfiguration(p);
            Assert(!config.Contains("Pset_总平构件信息属性集"), "unrelated IFC type carrier added");
            Assert(config.Contains("Pset_建筑区域信息属性集\tI\tIfcSlab"), "instance carrier changed");
        });
        check("confirmed_basement_footprint_overrides_name_guess_but_preserves_false", () => {
            var t = typeof(CaseWorkflow).Assembly.GetType("Hifc.Reference.FootprintPolicy");
            Assert(t != null, "Building footprint confirmation is missing");
            var settings = new UndergroundSettings();
            typeof(UndergroundSettings).GetProperty("BaseLevelUniqueId").SetValue(settings, "base");
            var levels = new[] { new UndergroundLevel { UniqueId="base", Name="-2F" }, new UndergroundLevel {UniqueId="ground", Name="1F"} };
            var field = new FieldCheck { Property="建筑基底", State="未填写", SuggestedValue="0" };
            t.GetMethod("Populate").Invoke(null, new object[] {field,settings,levels,"base"});
            Assert(field.SuggestedValue == "1", "confirmed basement ignored");
            field.State="有效"; field.Value="0"; field.SuggestedValue=null;
            t.GetMethod("Populate").Invoke(null, new object[] {field,settings,levels,"base"});
            Assert(field.Value == "0" && field.SuggestedValue == null, "explicit false overwritten");
            field.Value="false";
            t.GetMethod("Populate").Invoke(null, new object[] {field,settings,levels,"ground"});
            Assert(!field.SuggestionReason.Contains("不同"), "false text was treated as different from false");
            field.State="未填写";field.Value=null;
            t.GetMethod("Populate").Invoke(null, new object[] {field,settings,new UndergroundLevel[0],"base"});
            Assert(field.SuggestedValue == null && !string.IsNullOrEmpty(field.Detail), "deleted cached floor still generates values");
        });
        check("window_is_manually_classifiable_but_name_alone_is_not_auto_evidence", () => {
            var row = new ModelElementCheck { Entity = "IfcWindow", Name = "推拉窗", TypeName = "C0613" };
            var rule = CaseWorkflow.ForModel("报规", "单体建筑").Single(r => r.Slot == "窗");
            Assert(CaseWorkflow.CanClassify(rule, row), "window is absent from manual entry");
            Assert(CaseWorkflow.GetCandidates(row, "单体建筑").Length == 0, "all normal windows were automatically made business windows");
        });
        check("dedicated_parking_sources_outrank_civil_defence_scheme_name", () => {
            var row = new ModelElementCheck { Entity = "IfcSpace", AreaScheme = "人防分区面积", Name = "面积", SuggestedSlots = new[] { "停车位" } };
            var candidates = CaseWorkflow.GetCandidates(row, "单体建筑");
            Assert(candidates.Length == 1 && candidates[0].Slot == "停车位" && candidates[0].CanPrepare, "scheme overrode specific evidence");
            row.SuggestedSlots = new[] { "停车位", "人防区域" };
            Assert(CaseWorkflow.GetCandidates(row, "单体建筑").Length == 2, "conflicting dedicated fields were silently resolved");
        });
    }
}
