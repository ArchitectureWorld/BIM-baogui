using System;using System.Linq;using Hifc.Reference;
internal static class FloorAreaToleranceTests {
static void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
static FieldCheck Field(string owner,string set,string name,string value)=>new FieldCheck{OwnerUniqueId=owner,PropertySet=set,Property=name,Source=name=="投影面积"?"建筑区域投影面积":name,Binding="实例",RawType="Real",State=value==null?"未填写":"有效",Value=value,Editable=true};
static ModelElementCheck Level(string value)=>new ModelElementCheck{Id=311,UniqueId="F1",Name="1F",Entity="IfcBuildingStorey",LevelUniqueId="F1",LevelElevationM=0,Fields=new[]{Field("F1",FloorAreaPolicy.FloorSet,"楼层面积",value)}};
static ModelElementCheck Region(string uid,string value,double geometry)=>new ModelElementCheck{Id=int.Parse(uid),UniqueId=uid,Name=uid,Entity="IfcSlab",Slot="建筑区域",LevelUniqueId="F1",ProjectedAreaM2=geometry,ProjectionKey=uid,Fields=new[]{Field(uid,FloorAreaPolicy.RegionSet,"投影面积",value)}};
static ModelElementCheck Ignored(){var r=Region("662248",null,26.24);r.Slot=null;r.Candidates=new[]{new ClassificationCandidate{Slot="建筑区域",RequiresReview=true,CanPrepare=false}};return r;}
static PlanningSettings Settings()=>new PlanningSettings{RuleProfileId=PlatformRules.ProfileId,LevelsConfirmed=true,BuildingLevelUniqueIds=new[]{"F1"}};
static InputReviewItem[] Notices(FloorAreaPlan p){var prop=p.GetType().GetProperty("Notices");Assert(prop!=null,"Tolerance notice contract missing");return (InputReviewItem[])prop.GetValue(p,null);}
public static void Run(Action<string,Action> check){
check("county_manual_area_within_five_percent_preserves_values_and_ignores_unclassified_geometry",()=>{
var rows=new[]{Level("2951.65"),Region("662005","1012.84",997.01),Region("662201","145.14",140.8),Region("662209","11.25",10.915),Region("662219","56.17",54.49),Region("662302","393.33",388.055),Region("681485","1264.12",1247.175),Ignored()};
var before=rows.SelectMany(r=>r.Fields.Select(f=>f.Value)).ToArray();var p=FloorAreaPolicy.Build(rows,Settings());
Assert(p.Issues.Length==0&&p.Allocations.Length==0,"2.39% difference was blocked or silently redistributed");var n=Notices(p);Assert(n.Length==1&&!n[0].BlocksExport&&n[0].ElementId==311,"Difference is not a nonblocking level notice");Assert(n[0].Detail.Contains("2951.65")&&n[0].Detail.Contains("2882.85")&&n[0].Detail.Contains("2.39%"),"Notice lacks exact totals and ratio");
Assert(before.SequenceEqual(rows.SelectMany(r=>r.Fields.Select(f=>f.Value)))&&rows.Last().Slot==null,"Review modified values or classified the ignored floor");
});
check("county_five_percent_boundary_is_inclusive",()=>{foreach(var value in new[]{"95","105"}){var p=FloorAreaPolicy.Build(new[]{Level(value),Region("1","100",100),Ignored()},Settings());Assert(p.Issues.Length==0&&p.Allocations.Length==0&&Notices(p).Length==1,"Exactly 5% was not tolerated");}});
check("county_over_five_percent_does_not_bypass_pending_scope",()=>{var p=FloorAreaPolicy.Build(new[]{Level("105.01"),Region("1","100",100),Ignored()},Settings());Assert(p.Issues.Length>0&&p.Allocations.Length==0,"Over 5% bypassed scope review");});
check("county_tolerance_does_not_hide_real_overlap",()=>{var r=Region("1","100",100);r.AreaAggregationIssue="已确认区域重叠";var p=FloorAreaPolicy.Build(new[]{Level("102"),r,Ignored()},Settings());Assert(p.Issues.Length>0&&p.Allocations.Length==0,"Overlap bypassed by tolerance");});
check("county_tolerance_requires_valid_persisted_region_values",()=>{foreach(var state in new[]{"未填写","值非法"}){var r=Region("1",state=="未填写"?null:"100",100);r.Fields[0].State=state;var p=FloorAreaPolicy.Build(new[]{Level("102"),r,Ignored()},Settings());Assert(p.Issues.Length>0&&p.Allocations.Length==0,"Missing or invalid area bypassed by geometry fallback");}});
check("county_tolerance_uses_metric_values",()=>{var l=Level("1097.919");l.Fields[0].MetricValue=102;var r=Region("1","1076.391",100);r.Fields[0].MetricValue=100;var p=FloorAreaPolicy.Build(new[]{l,r,Ignored()},Settings());Assert(p.Issues.Length==0&&p.Allocations.Length==0&&Notices(p)[0].Detail.Contains("2.00%"),"Internal units leaked into tolerance");});
check("legacy_area_redistribution_is_unchanged",()=>{var p=FloorAreaPolicy.Build(new[]{Level("102"),Region("1","100",100)},new PlanningSettings{LevelsConfirmed=true,BuildingLevelUniqueIds=new[]{"F1"}});Assert(p.Allocations.Length==1&&p.Allocations[0].ValueM2=="102.00","Other profiles changed silently");});
}
}
