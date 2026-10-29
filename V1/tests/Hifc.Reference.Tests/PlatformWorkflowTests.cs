using System;
using System.Linq;
using Hifc.Reference;

internal static class PlatformWorkflowTests
{
    const string Profile = "hubei-420626-planning-20261020";
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    static FieldCheck Field(string slot, string name, string value = null, string type = "Text")
        => new FieldCheck { PropertySet = "Pset_" + slot + "信息属性集", Property = name, Source = name,
            RawType = type, Value = value, State = value == null ? "未填写" : "有效", RuleProfileId = Profile,
            Binding = "实例", OwnerUniqueId = slot };
    static ModelElementCheck Row(string slot, params FieldCheck[] fields) => new ModelElementCheck {
        Id = 1, UniqueId = slot, Entity = "IfcSlab", Slot = slot, Slots = new[]{slot}, Fields = fields,
        Classified = true, Name = slot };
    static ModelElementCheck Land() => Row("规划净用地", Field("规划净用地", "投影面积", "1000", "Real"));

    public static void Run(Action<string, Action> check)
    {
        check("net_land_control_line_name_and_type_survive_draft_validation_and_export", () => {
            const string value = "净用地控制红线";
            var name = Field("规划控制线", "名称", "用地红线", "Label");
            name.Source = "规划控制线名称";
            var type = Field("规划控制线", "红线类型", null, "Label");
            var row = Row("规划控制线", name, type, Field("规划控制线", "编号", "3", "Label"));
            var edited = PlanningInputs.WithDraftValues(new[] { row }, new[] { name, type }.Select(field => new PlanningValueOverride {
                OwnerUniqueId = "规划控制线", PropertySet = field.PropertySet, Source = field.Source, Binding = "实例", Value = value }).ToArray());
            Assert(edited[0].Fields.Take(2).All(f => f.Valid && f.Value == value), "confirmed control-line draft is rejected or rewritten");
            Assert(edited[0].Slot == "规划控制线" && edited[0].Fields[2].Value == "3", "two-field correction changed class or number");
            Assert(row.Fields[0].Value == "用地红线" && row.Fields[1].Value == null, "draft mutated source snapshot");
            var properties = CaseExportPolicy.PropertiesForSlot("规划控制线", Profile);
            foreach (var property in new[] { "名称", "红线类型" }) {
                var definitions = properties.Where(p => p.Property == property).ToArray();
                Assert(definitions.Length > 0 && definitions.All(p => CaseExportPolicy.ValueForExport(p, value, Profile) == value), "export rejected or remapped " + property);
            }
            Assert(properties.Where(p => p.Property == "名称").All(p => p.SourceParameter == "规划控制线名称"), "IFC name lost its dedicated Revit source");
        });
        check("verified_school_usage_writes_and_exports_without_changing_civil_participation", () => {
            foreach (var civil in new[] { "0", "1" }) {
                var usage = Field("建筑技术", "建筑用途名称", null, "Label");
                var row = Row("建筑技术", usage, Field("建筑技术", "民用建筑", civil, "Boolean"));
                row.Entity = "IfcBuilding";
                var edited = PlanningInputs.WithDraftValues(new[] { row }, new[] { new PlanningValueOverride {
                    OwnerUniqueId = "建筑技术", PropertySet = usage.PropertySet, Source = usage.Source, Binding = "实例", Value = "中学" } });
                Assert(edited[0].Fields[0].Valid && edited[0].Fields[0].Value == "中学", "school usage is rejected in the draft validation path");
                Assert(edited[0].Fields[1].Value == civil && row.Fields[0].Value == null, "usage edit changed civil participation or the source scan");
                var definition = CaseExportPolicy.PropertiesForSlot("建筑技术", Profile).Single(p => p.Property == "建筑用途名称");
                Assert(CaseExportPolicy.ValueForExport(definition, edited[0].Fields[0].Value, Profile) == "中学", "export remapped or blocked the verified label");
            }
        });
        check("platform_rejects_nonempty_out_of_enum_without_replacing_source", () => {
            var p = new ReferenceProperty { PropertySet="Pset_绿地信息属性集", Property="绿地类型", SourceParameter="绿地类型", RawType="Text", Binding="实例" };
            var result = ModelChecks.Evaluate(p, new[]{new ParameterRead{Name="绿地类型", Origin="instance", HasValue=true, Value="模板随意分类", StorageType="String"}}, Profile);
            Assert(!result.Valid && result.Value=="模板随意分类", "invalid enum must be reported and original retained");
        });
        check("platform_missing_green_fields_are_in_calculation_issues", () => {
            var green=Row("绿地",Field("绿地","投影面积","20","Real"));
            var issues=PlanningInputs.Issues(new[]{Land(),green},"总图",Profile);
            Assert(issues.Any(x=>x.Contains("绿地类型")&&x.Contains("折算系数")),"absent field definitions must not disappear from completeness check");
        });
        check("platform_net_land_optional_labels_do_not_block_calculation", () => {
            var land=Land();land.Fields=land.Fields.Concat(new[]{Field("规划净用地","名称"),Field("规划净用地","版本")}).ToArray();
            var issues=PlanningInputs.Issues(new[]{land},"总图",Profile);
            Assert(!issues.Any(x=>x.Contains("名称")||x.Contains("版本")),"optional labels cannot become platform calculation blockers");
        });
        check("platform_draft_enum_is_not_marked_valid_unconditionally", () => {
            var row=Row("绿地",Field("绿地","绿地类型","附属绿地"));
            var copy=PlanningInputs.WithDraftValues(new[]{row},new[]{new PlanningValueOverride{OwnerUniqueId="绿地",PropertySet="Pset_绿地信息属性集",Source="绿地类型",Binding="实例",Value="错误枚举"}});
            Assert(!copy[0].Fields[0].Valid && row.Fields[0].Value=="附属绿地","draft invalidity and original snapshot isolation");
        });
        check("platform_active_rules_include_explicit_optional_capabilities", () => {
            var rules=CaseWorkflow.ForModel("报规","单体建筑",Profile);
            foreach(var slot in new[]{"雨篷","室内停车场","窗"})
                Assert(rules.Any(r=>r.Slot==slot&&!r.Required&&r.Binding=="实例"),"missing optional instance capability: "+slot);
            Assert(rules.Single(r=>r.Slot=="窗").Properties.Any(p=>p.Property=="底高度"),"window instance bottom height lost");
        });
        check("platform_rules_do_not_apply_to_construction_stage",()=>
            Assert(CaseWorkflow.ForModel("施工图","单体建筑",Profile).Length==0,"planning snapshot applied to construction"));
        check("platform_drafts_validate_merged_parent_and_child_and_preserve_scan",()=>{
            var row=Row("建筑区域",Field("建筑区域","主功能类别","住宅"),Field("建筑区域","子功能类别","旧子类"));
            var edits=new[]{"主功能类别","子功能类别"}.Select((name,i)=>new PlanningValueOverride {
                OwnerUniqueId="建筑区域",PropertySet="Pset_建筑区域信息属性集",Source=name,Binding="实例",Value=i==0?"学校":"办公室"}).ToArray();
            var copy=PlanningInputs.WithDraftValues(new[]{row},edits);
            Assert(copy[0].Fields.All(f=>f.Valid)&&row.Fields[0].Value=="住宅","merged legitimate school function rejected or scan mutated");
            var changed=PlanningInputs.WithDraftValues(copy,new[]{new PlanningValueOverride {
                OwnerUniqueId="建筑区域",PropertySet="Pset_建筑区域信息属性集",Source="主功能类别",Binding="实例",Value="住宅"}});
            Assert(changed[0].Fields[1].Valid&&changed[0].Fields[1].Value=="办公室","changing parent erased or blocked the retained child");
            Assert(PlatformInputReview.Build(changed,Profile).Any(i=>i.Property=="子功能类别"&&i.Detail.Contains("住宅")),"retained incompatible child lost its advisory review");
        });
        check("platform_false_compute_gate_skips_inapplicable_height_and_preserves_false",()=>{
            var row=Row("墙",Field("墙","是否为女儿墙","0","Boolean"),Field("墙","高度",null,"Real"));
            row.Entity="IfcWall";
            var issues=PlanningInputs.Issues(new[]{row},"单体建筑",Profile);
            Assert(!issues.Any(x=>x.StartsWith("墙：")&&x.Contains("高度")),"false gate still required parapet height");
            Assert(row.Fields[0].Value=="0","false gate value changed");
            Assert(CaseWorkflow.Summarize("报规","单体建筑",new[]{row},Profile).Roles.Single(r=>r.Slot=="墙").MissingFields==0,"summary still required disabled wall height");
            PlanningInputs.ValidateFields(new[]{row});
            Assert(row.Fields.All(f=>f.Acceptable),"display still reports irrelevant height missing");
            row.Fields[0].Value="1";PlanningInputs.ValidateFields(new[]{row});
            Assert(!row.Fields[1].Acceptable,"reenabling computation reused stale optional flag");
        });
        check("platform_generic_wall_names_do_not_confirm_business_use",()=>{
            foreach(var name in new[]{"基墙 200","幕墙","女儿墙"})
            {
                var row=new ModelElementCheck { Id=42, UniqueId="ordinary-wall", Entity="IfcWall", Category="墙", Name=name, TypeName=name };
                Assert(!CaseWorkflow.GetCandidates(row,"单体建筑",Profile).Any(c=>c.Slot=="墙"),"wall name alone created business classification: "+name);
                Assert(CaseWorkflow.Summarize("报规","单体建筑",new[]{row},Profile).Roles.Single(r=>r.Slot=="墙").Candidates==0,"wall name entered completeness scope: "+name);
            }
        });
        check("platform_wall_dedicated_evidence_and_manual_classification_remain_supported",()=>{
            var row=new ModelElementCheck { Id=42, UniqueId="evidenced-wall", Entity="IfcWall", Category="墙", Name="普通构件", SuggestedSlots=new[]{"墙"} };
            var candidate=CaseWorkflow.GetCandidates(row,"单体建筑",Profile).Single(c=>c.Slot=="墙");
            Assert(candidate.CanPrepare&&!candidate.RequiresReview,"dedicated original wall source no longer supports classification");
            row.SuggestedSlots=new string[0];row.Slot="墙";row.Classified=true;
            var rule=CaseWorkflow.ForModel("报规","单体建筑",Profile).Single(r=>r.Slot=="墙");
            Assert(CaseWorkflow.CanClassify(rule,row)&&CaseWorkflow.SuggestSlots(row,"单体建筑",Profile).Contains("墙"),"manual wall classification was removed");
        });
        check("platform_common_values_do_not_fill_unknown_or_incompatible_child",()=>{
            var row=Row("建筑区域",Field("建筑区域","主功能类别","住宅"),Field("建筑区域","子功能类别"),Field("建筑区域","计算系数",null,"Real"));
            var settings=new PlanningSettings{RuleProfileId=Profile};
            settings.CommonValues["建筑区域|主功能类别"]="学校";settings.CommonValues["建筑区域|子功能类别"]="办公室";
            PlanningInputs.Populate(new[]{row},settings,null);
            Assert(row.Fields[0].Value=="住宅"&&row.Fields[1].SuggestedValue==null&&row.Fields[2].SuggestedValue==null,"default or incompatible common function suggested");
            row.Fields[0].Value="学校";PlanningInputs.Populate(new[]{row},settings,null);
            Assert(row.Fields[1].SuggestedValue=="办公室","valid common child not suggested");
            var edited=PlanningInputs.WithDraftValues(new[]{row},new[]{new PlanningValueOverride {
                OwnerUniqueId="建筑区域",PropertySet="Pset_建筑区域信息属性集",Source="主功能类别",Binding="实例",Value="住宅"}});
            PlanningInputs.Populate(edited,settings,null);
            Assert(edited[0].Fields[1].SuggestedValue==null,"stale child suggestion survived changing parent in the same write");
        });
        check("platform_total_plan_supports_each_declared_carrier",()=>{
            var rule=CaseWorkflow.ForModel("报规","总图",Profile).Single(r=>r.Slot=="总平构件");
            Assert(rule.Entities.Contains("IfcSlab")&&rule.Entities.Contains("IfcBuildingElementProxy"),"grouping dropped an entity route");
            var road=CaseWorkflow.ForModel("报规","总图",Profile).Single(r=>r.Slot=="道路边界线");
            Assert(road.EmptyAllowed.Contains("道路宽度"),"unqualified road class requires all widths instead of only fire roads");
        });
        check("platform_indoor_parking_name_does_not_also_classify_outdoor_carpark",()=>{
            var row=new ModelElementCheck{Entity="IfcSpace",Name="室内停车场",AreaScheme="建筑面积"};
            Assert(CaseWorkflow.GetCandidates(row,"单体建筑",Profile).Single().Slot=="室内停车场","ambiguous generic parking match");
        });
        check("unclassified_slabs_do_not_get_required_total_plan_business",()=>
            Assert(!CaseWorkflow.ForModel("报规","单体建筑").Any(r=>r.Slot=="总平构件"&&r.Required),"type coincidence became required business classification"));
        check("reference_mode_total_plan_is_explicit_instance_business_too",()=>{
            var rule=CaseWorkflow.ForModel("报规","单体建筑").Single(r=>r.Slot=="总平构件");
            Assert(rule.Binding=="实例"&&CaseWorkflow.CanClassify(rule,new ModelElementCheck{Entity="IfcSlab"}),"legacy case reading still requires shared type business");
            Assert(rule.Properties.Select(p=>p.Property).Distinct().Count()==6,"explicit total-plan capability lost fields");
        });
    }
}
