using System;
using System.Linq;
using System.Reflection;
using Hifc.Reference;

internal static class CaseWorkflowTests
{
    // Invoke the new public policy through reflection so the preimplementation run
    // fails as a missing workflow, rather than failing to compile the test suite.
    private static object Call(string name, params object[] args)
    {
        var type = typeof(ModelChecks).Assembly.GetType("Hifc.Reference.CaseWorkflow");
        if (type == null) throw new Exception("Stage/type case workflow has not been implemented");
        try { return type.GetMethods().Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(null, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    private static object Value(object item, string name) => item.GetType().GetProperty(name).GetValue(item);
    private static void Assert(bool value) { if (!value) throw new Exception("case workflow assertion failed"); }
    private static ModelElementCheck Row(string entity, string slot = null, string name = "") => new ModelElementCheck {
        Id = 1, UniqueId = Guid.NewGuid().ToString(), Entity = entity, Slot = slot, Classified = slot != null, Name = name
    };
    public static void Run(Action<string, Action> check)
    {
        check("phase_must_be_chosen_by_human", () => {
            var result = (ModelSummary)Call("Summarize", "", "总图", new ModelElementCheck[0]);
            Assert(!result.Ready && result.PendingRequirements > 0);
        });
        check("construction_never_inherits_planning_rules", () => {
            Assert(((Array)Call("ForModel", "施工图", "总图")).Length == 0);
            Assert(!((ModelSummary)Call("Summarize", "施工图", "总图", new[] { Row("IfcSlab", "规划净用地") })).Ready);
        });
        check("site_retains_case_roles_while_adding_optional_reference_areas", () => {
            var rules = ((Array)Call("ForModel", "报规", "总图")).Cast<object>().ToArray();
            Assert(rules.Count(r => !(bool)Value(r, "ReferenceSupplement")) == 8);
            var typeRule = rules.Single(r => (string)Value(r, "Slot") == "总平构件");
            Assert((string)Value(typeRule, "Binding") == "实例");
            Assert(((ReferenceProperty[])Value(typeRule, "Properties")).Select(p=>p.Property).Distinct().Count() == 6);
        });
        check("single_building_common_case_does_not_require_factory_windows", () => {
            var rules = ((Array)Call("ForModel", "报规", "单体建筑")).Cast<object>().ToArray();
            Assert(rules.Count(r => (bool)Value(r, "Required")) == 4);
            Assert(!(bool)Value(rules.Single(r => (string)Value(r, "Slot") == "总平构件"), "Required"));
            Assert(!(bool)Value(rules.Single(r => (string)Value(r, "Slot") == "窗"), "Required"));
        });
        check("site_classification_is_explainable", () => {
            var detection = Call("Detect", new object[] { new[] { Row("IfcSlab", "规划净用地") } });
            Assert((string)Value(detection, "Kind") == "总图");
            Assert(((string[])Value(detection, "Evidence")).Length > 0);
        });
        check("blank_project_is_not_automatically_a_building", () => {
            var detection = Call("Detect", new object[] { new[] { Row("IfcBuilding"), Row("IfcBuildingStorey") } });
            Assert((string)Value(detection, "Kind") == "待确认");
        });
        check("mixed_site_and_building_evidence_requires_resolution", () => {
            var detection = Call("Detect", new object[] { new[] { Row("IfcSlab", "规划净用地"), Row("IfcSlab", "建筑区域") } });
            Assert((string)Value(detection, "Kind") == "混合模型");
        });
        check("generic_road_name_cannot_assign_a_specific_road_role", () => {
            var roles = (string[])Call("SuggestSlots", Row("IfcSlab", null, "道路楼板"), "总图");
            Assert(roles.Length == 0);
        });
        check("explicit_conflicting_role_is_not_silently_reclassified", () => {
            var roles = (string[])Call("SuggestSlots", Row("IfcSlab", "绿地", "道路红线"), "总图");
            Assert(roles.SequenceEqual(new[] { "绿地" }));
        });
        check("ordinary_walls_are_not_required_special_business_models", () => {
            var result = (ModelSummary)Call("Summarize", "报规", "单体建筑", new[] { Row("IfcWall") });
            Assert(result.ClassifiedCount == 0 && result.FieldCount == 0);
            Assert(result.Roles.All(r => r.Slot != "墙"));
        });
        check("new_snapshot_does_not_reuse_deleted_role_coverage", () => {
            var first = (ModelSummary)Call("Summarize", "报规", "总图", new[] { Row("IfcSlab", "规划净用地") });
            var next = (ModelSummary)Call("Summarize", "报规", "总图", new ModelElementCheck[0]);
            Assert(first.Roles.Single(r => r.Slot == "规划净用地").Count == 1);
            Assert(next.Roles.Single(r => r.Slot == "规划净用地").Count == 0);
        });
        check("dedicated_source_evidence_survives_erased_classification", () => {
            var row = Row("IfcSlab", null, "普通楼板"); row.SuggestedSlots = new[] { "规划净用地" };
            Assert((string)Value(Call("Detect", new object[] { new[] { row } }), "Kind") == "总图");
        });
        check("shared_type_fields_count_once_without_duplicate_elements", () => {
            var first = Row("IfcWindow", "窗"); first.TypeId = 42;
            var second = Row("IfcWindow", "窗"); second.TypeId = 42;
            first.Fields = new[] { new FieldCheck { OwnerUniqueId = "type-42", Binding = "类型", PropertySet = "Pset_窗信息属性集", Property = "高度", State = "有效", Value = "1.5" } };
            second.Fields = first.Fields;
            var result = (ModelSummary)Call("Summarize", "报规", "单体建筑", new[] { first, second });
            Assert(result.ElementCount == 2 && result.FieldCount == 1 && result.Roles.Single(r => r.Slot == "窗").Count == 1);
        });
        check("case_empty_value_does_not_hide_a_missing_parameter", () => {
            Assert(new FieldCheck { AllowedEmpty = true, State = "未填写" }.Acceptable);
            Assert(!new FieldCheck { AllowedEmpty = true, State = "缺参数" }.Acceptable);
        });
        check("technical_fields_support_optional_underground_count_and_calculated_area", () => {
            var rules = ((Array)Call("ForModel", "报规", "单体建筑")).Cast<object>();
            var technical = rules.Single(r => (string)Value(r, "Slot") == "建筑技术");
            var names = ((ReferenceProperty[])Value(technical, "Properties")).Select(p => p.Property).ToArray();
            Assert(names.Length == 30 && names.Contains("地下建筑层数") && names.Contains("建筑面积"));
            Assert(((string[])Value(technical,"EmptyAllowed")).Contains("地下建筑层数"));
        });
        check("site_inventory_uses_site_and_road_content_without_psets", () => {
            var rows = new[] { Row("IfcSlab", null, "道路"), Row("IfcSlab", null, "场地"), Row("IfcSlab", null, "绿化") };
            Assert((string)Value(Call("Detect", new object[] { rows }), "Kind") == "总图");
            Assert(CaseWorkflow.GetCandidates(rows[0], "总图").All(c=>!c.CanPrepare));
        });
    }
}
