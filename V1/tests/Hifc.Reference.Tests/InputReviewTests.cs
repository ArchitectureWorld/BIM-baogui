using System;
using System.Linq;
using Hifc.Reference;

internal static class InputReviewTests
{
    const string Profile = PlatformRules.ProfileId;
    static void Assert(bool ok,string reason){if(!ok)throw new Exception(reason);}
    internal static FieldCheck Field(string slot,string property,string value=null,string raw="Label") => new FieldCheck {
        PropertySet="Pset_"+slot+"信息属性集",Property=property,Source=property,RawType=raw,
        Value=value,State=value==null?"未填写":"有效",Binding="实例",RuleProfileId=Profile,AllowedEmpty=true };
    internal static ModelElementCheck Row(string slot,params FieldCheck[] fields) => new ModelElementCheck {
        Id=718110,UniqueId=slot,Name=slot+"测试",Entity="IfcSlab",Slot=slot,Classified=true,Fields=fields };
    public static void Run(Action<string,Action> check)
    {
        check("missing_building_usage_requires_attention_until_a_written_value_is_read_back",()=>{
            foreach(var profile in new[]{"",Profile}) {
                var usage=Field("建筑技术","建筑用途名称");usage.RuleProfileId=profile;
                var civil=Field("建筑技术","民用建筑","0","Boolean");civil.RuleProfileId=profile;
                var row=Row("建筑技术",usage,civil);row.Entity="IfcBuilding";
                Func<RoleCoverage> role=()=>CaseWorkflow.Summarize("报规","单体建筑",new[]{row},profile).Roles.Single(r=>r.Slot=="建筑技术");
                Assert(role().NeedsAttention&&role().MissingManualFields==1&&role().State.Contains("建筑用途名称"),"Missing building usage is not highlighted");
                Assert(!usage.AllowedEmpty&&!usage.Acceptable,"Missing usage is allowed empty");
                usage.SuggestedValue="中学";
                Assert(role().NeedsAttention&&usage.Value==null,"Suggestion was counted as a saved usage value");
                var review=PlatformInputReview.Build(new[]{row},profile).Single(i=>i.Property=="建筑用途名称");
                Assert(review.Code=="BUILDING_USAGE_REQUIRED"&&review.Detail.Contains("住宅")&&!review.Detail.Contains("消防道路"),"Missing-usage report uses the wrong cause");
                usage.Value="中学";usage.State="有效";
                Assert(!role().NeedsAttention&&civil.Value=="0","Readback did not resolve attention or changed civil participation");
                usage.Value=null;usage.State="未填写";
                Assert(role().NeedsAttention,"Later manual clearing was not detected on full check");
                Assert(CaseWorkflow.ForModel("报规","单体建筑",profile).Single(r=>r.Slot=="建筑技术").Properties.Any(p=>p.Property=="建筑用途名称"),"Usage is absent from the full-scan property contract");
            }
        });
        check("mapped_building_inputs_absent_from_visible_platform_fields_are_still_reported",()=>{
            var missing=new[]{"人数","建筑体积","室内外高差最大值"}.Select(p=>Field("建筑技术",p,null,"Real")).ToArray();
            var row=Row("建筑技术",missing);row.Entity="IfcBuilding";
            var items=PlatformInputReview.Build(new[]{row},Profile);
            Assert(missing.All(f=>items.Any(i=>i.Property==f.Property&&i.Code=="MAPPED_INPUT_MISSING")),"mapped technical gaps disappear from export review");
            Assert(missing.All(f=>f.Value==null&&f.SuggestedValue==null&&!f.ManualInputRequired),"gap review invented values or mandatory manual fields");
        });
        check("supplementary_input_review_keeps_explicit_zero_and_unrelated_fields",()=>{
            var rows=new[]{Row("建筑技术",Field("建筑技术","建筑体积","0","Real")),Row("墙",Field("墙","建筑体积"))};
            Assert(!PlatformInputReview.Build(rows,Profile).Any(i=>i.Property=="建筑体积"),"zero or unrelated carrier reported as a missing building input");
        });
        check("ordinary_and_unknown_roads_do_not_require_manual_width",()=>{
            foreach(var kind in new[]{"小区干道","楼间交通",(string)null}) {
                var row=Row("道路边界线",Field("道路边界线","区内道路类型",kind),Field("道路边界线","道路宽度",null,"Real"));
                var summary=CaseWorkflow.Summarize("报规","总图",new[]{row},Profile);
                Assert(!summary.Roles.Single(r=>r.Slot=="道路边界线").NeedsAttention,"Non-fire road was forced to fill width: "+kind);
            }
        });
        check("fire_width_requirement_recomputed_when_road_use_changes",()=>{
            var use=Field("道路边界线","区内道路类型","消防道路");var width=Field("道路边界线","道路宽度",null,"Real");
            var row=Row("道路边界线",use,width);
            Func<bool> red=()=>CaseWorkflow.Summarize("报规","总图",new[]{row},Profile).Roles.Single(r=>r.Slot=="道路边界线").NeedsAttention;
            Assert(red(),"Fire width gap missing");use.Value="小区干道";Assert(!red(),"Old fire requirement persisted");
            use.Value="消防道路";width.Value="4";width.State="有效";Assert(!red(),"Written width still incomplete");
        });
        check("nullable_redline_width_reports_platform_default_in_check",()=>{
            var row=Row("道路红线",Field("道路红线","道路类型","支路"),Field("道路红线","红线宽度",null,"Real"));
            var issues=PlanningInputs.Issues(new[]{row},"总图",Profile);
            Assert(issues.Any(i=>i.Contains("红线宽度")&&i.Contains("15")&&i.Contains("默认")),"Nullable 15m fallback hidden");
            Assert(!row.Fields[1].ManualInputRequired,"Redline width became mandatory manual entry");
        });
        check("missing_green_class_reports_default_without_filling_model",()=>{
            var f=Field("绿地","绿地类型");var row=Row("绿地",f);
            Assert(PlanningInputs.Issues(new[]{row},"总图",Profile).Any(i=>i.Contains("集中绿地")&&i.Contains("默认")),"Missing classification consequences hidden");
            Assert(f.Value==null&&f.SuggestedValue==null,"Template default became a model value");
        });
        check("public_parking_and_redline_width_available_for_fill_empty_only",()=>{
            var fields=PlanningInputs.CommonFields("总图",Profile);
            Assert(fields.Any(f=>f.PropertySet=="Pset_停车位信息属性集"&&f.Property=="公建车位"),"Public parking absent from common inputs");
            Assert(fields.Any(f=>f.PropertySet=="Pset_道路红线信息属性集"&&f.Property=="红线宽度"),"Redline width absent from common inputs");
            var yes=Field("停车位","公建车位",null,"Boolean");var no=Field("停车位","公建车位","0","Boolean");
            var rows=new[]{Row("停车位",yes),Row("停车位",no)};
            var settings=new PlanningSettings{RuleProfileId=Profile};settings.CommonValues["停车位|公建车位"]="1";
            PlanningInputs.Populate(rows,settings,null);
            Assert(yes.SuggestedValue=="1"&&no.Value=="0"&&no.SuggestedValue==null,"Fill-empty overwrote explicit false or failed to suggest true");
        });
        check("input_review_distinguishes_defaults_explicit_values_and_disabled_groups",()=>{
            var row=Row("建筑区域",Field("建筑区域","主功能类别"),Field("建筑区域","子功能类别"));
            var missing=PlatformInputReview.Build(new[]{row},Profile);
            Assert(missing.Any(i=>i.Property=="主功能类别"&&i.DefaultValue=="住宅"),"Missing parent default hidden");
            row.Fields[0].Value="其它设施";
            Assert(PlatformInputReview.Build(new[]{row},Profile).Single(i=>i.Property=="子功能类别").DefaultValue.Contains("取决于主功能"),"Provided parent was replaced by residential default");
            Assert(PlatformInputReview.Build(new[]{row},"").Length==0,"Reference profile assumed platform defaults");
            var parking=Row("停车位",Field("停车位","公建车位","0","Boolean"),Field("停车位","折算系数","0","Real"));
            Assert(!PlatformInputReview.Build(new[]{parking},Profile).Any(i=>i.Property=="公建车位"||i.Property=="折算系数"),"Explicit zero/false treated as missing");
            var disabled=Row("墙",Field("墙","是否为女儿墙","0","Boolean"));
            Assert(PlatformInputReview.Build(new[]{disabled},Profile).Length==0,"Excluded calculation group raised missing inputs");
        });
        check("parking_review_uses_selected_branch_and_does_not_default_optional_labels",()=>{
            foreach(var outdoor in new[]{"1","0"}) {
                var row=Row("停车位",Field("停车位","是否为室外停车场",outdoor,"Boolean"));
                var review=PlatformInputReview.Build(new[]{row},Profile);
                var expected=PlatformRules.Rules("停车位").Single(r=>r.EntityCode==(outdoor=="1"?"203":"104")).Fields.Single(f=>f.Name=="折算系数").DefaultValue;
                Assert(review.Single(i=>i.Property=="折算系数").DefaultValue==expected,"Parking branch default differs");
                Assert(!review.Any(i=>i.Property=="名称"||i.Property=="版本"),"Optional labels became review requirements");
            }
        });
        check("source_conflicts_are_not_presented_as_missing_with_default",()=>{
            var field=Field("绿地","绿地类型");field.State="有歧义";field.Detail="同名参数值不一致";
            var item=PlatformInputReview.Build(new[]{Row("绿地",field)},Profile).Single(i=>i.Property==field.Property);
            Assert(item.Code=="HIFC_PLATFORM_SOURCE_CONFLICT"&&item.DefaultValue==null&&item.Detail.Contains("同名参数"),"Ambiguous source hidden behind default advice");
        });
    }
}
