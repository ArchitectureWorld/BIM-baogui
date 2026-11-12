using System;
using System.Linq;
using Hifc.Reference;

internal static class SiteBoundaryTests
{
    const string Set="Pset_边界分段线信息属性集";
    static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
    static ModelElementCheck Segment(int id,string direction,string number=null)
    {
        var row=new ModelElementCheck{Id=id,UniqueId="boundary-"+id,Classified=true,Slot="边界分段线",Name="边界分段线",Entity="IfcBuildingElementProxy"};
        row.Fields=new[]{new FieldCheck{Property="边界线编号",Value=number??id.ToString()},new FieldCheck{Property="边界类型",Value=direction},new FieldCheck{Property="界外类型",Value="空地"}};
        foreach(var field in row.Fields){field.PropertySet=Set;field.State=string.IsNullOrWhiteSpace(field.Value)?"未填写":"有效";field.RawType="Label";field.RuleProfileId=PlatformRules.ProfileId;field.Binding="实例";}
        return row;
    }
    static ModelElementCheck[] Four()=>new[]{Segment(1,"东边界"),Segment(2,"南边界"),Segment(3,"西边界"),Segment(4,"北边界")};
    static InputReviewItem[] Review(ModelElementCheck[] rows,string kind="总图",string profile=PlatformRules.ProfileId)
        =>PlatformInputReview.Build(rows,profile,kind).Where(i=>i.Code.StartsWith("SITE_BOUNDARY_",StringComparison.Ordinal)).ToArray();
    public static void Run(Action<string,Action> check)
    {
        check("site_boundary_role_is_required_only_for_active_site_profile",()=>{
            Assert(CaseWorkflow.ForModel("报规","总图",PlatformRules.ProfileId).Single(r=>r.Slot=="边界分段线").Required,"Site boundary role remains optional");
            Assert(!CaseWorkflow.ForModel("报规","单体建筑",PlatformRules.ProfileId).Any(r=>r.Slot=="边界分段线"),"Building acquired site boundary requirement");
        });
        check("site_boundary_zero_objects_reports_all_directions_without_blocking_export",()=>{
            var issues=Review(new ModelElementCheck[0]);
            Assert(issues.Any(i=>i.Code=="SITE_BOUNDARY_COUNT"&&i.Value=="0"),"Zero objects escaped project validation");
            Assert(issues.Any(i=>i.Code=="SITE_BOUNDARY_DIRECTIONS"&&new[]{"东边界","南边界","西边界","北边界"}.All(s=>i.Detail.Contains(s))),"Missing direction details incomplete");
            Assert(issues.All(i=>!i.BlocksExport),"Boundary completeness introduced an export gate");
        });
        check("site_boundary_exact_four_unique_directions_and_numbers_pass",()=>Assert(Review(Four()).Length==0,"Valid four-direction set rejected"));
        check("site_boundary_quantity_is_exact_not_at_least_four",()=>{
            Assert(Review(Four().Take(3).ToArray()).Any(i=>i.Code=="SITE_BOUNDARY_COUNT"),"Three segments passed");
            Assert(Review(Four().Concat(new[]{Segment(5,"东边界")}).ToArray()).Any(i=>i.Code=="SITE_BOUNDARY_COUNT"),"Five segments passed");
        });
        check("site_boundary_four_same_direction_does_not_pass_by_count",()=>{
            var rows=Enumerable.Range(1,4).Select(i=>Segment(i,"东边界")).ToArray();
            var issue=Review(rows).Single(i=>i.Code=="SITE_BOUNDARY_DIRECTIONS");
            Assert(issue.Detail.Contains("重复")&&issue.Detail.Contains("缺少")&&issue.Detail.Contains("东边界"),"Duplicate and missing direction diagnostics lost");
        });
        check("site_boundary_direction_comes_from_property_not_native_name",()=>{
            var rows=Four();rows[0].Name="东边界";rows[0].Fields[1].Value="";rows[0].Fields[1].State="未填写";
            Assert(Review(rows).Any(i=>i.Code=="SITE_BOUNDARY_DIRECTIONS"&&i.Detail.Contains("东边界")),"Native name concealed a missing direction property");
        });
        check("site_boundary_number_missing_or_repeated_is_reported_without_rewriting",()=>{
            var rows=Four();rows[0].Fields[0].Value="";rows[0].Fields[0].State="未填写";rows[2].Fields[0].Value="2";
            var issues=Review(rows);
            Assert(issues.Any(i=>i.Code=="SITE_BOUNDARY_NUMBER_MISSING"&&i.ElementId==1)&&issues.Any(i=>i.Code=="SITE_BOUNDARY_NUMBER_DUPLICATE"),"Number coverage missing");
            Assert(rows[0].Fields[0].Value==""&&rows[2].Fields[0].Value=="2","Check mutated model values");
        });
        check("site_boundary_sources_are_counted_once_and_not_guessed_from_names",()=>{
            var rows=Four();Assert(Review(rows.Concat(new[]{rows[0]}).ToArray()).Length==0,"Same Revit identity double counted");
            var namedOnly=Segment(5,"东边界");namedOnly.Slot=null;namedOnly.Classified=false;namedOnly.Fields=new FieldCheck[0];
            Assert(Review(rows.Concat(new[]{namedOnly}).ToArray()).Length==0,"An unclassified name became a boundary object");
        });
        check("site_boundary_check_does_not_apply_to_buildings_or_inactive_profiles",()=>{
            Assert(Review(new ModelElementCheck[0],"单体建筑").Length==0&&Review(new ModelElementCheck[0],"总图","").Length==0,"Boundary scope widened");
        });
        check("site_boundary_findings_reach_management_calculation_issues",()=>{
            Assert(PlanningInputs.Issues(new ModelElementCheck[0],"总图",PlatformRules.ProfileId).Any(s=>s.Contains("东边界")&&s.Contains("北边界")),"Management omitted direction failures");
        });
    }
}
