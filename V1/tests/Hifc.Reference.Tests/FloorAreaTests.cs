using System;
using System.Globalization;
using System.Linq;
using Hifc.Reference;

internal static class FloorAreaTests
{
    static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
    static FieldCheck Area(string owner,string slot,string property,string value)=>new FieldCheck{
        OwnerUniqueId=owner,PropertySet="Pset_"+slot+"信息属性集",Property=property,
        Source=slot=="建筑区域"?"建筑区域投影面积":property,Binding="实例",RawType="Real",
        Value=value,State=value==null?"未填写":"有效",Editable=true};
    static ModelElementCheck Level(string uid,string value,double elevation=0)=>new ModelElementCheck{
        UniqueId=uid,Name=uid,Entity="IfcBuildingStorey",LevelUniqueId=uid,LevelElevationM=elevation,
        Fields=new[]{Area(uid,"建筑楼层","楼层面积",value)}};
    static ModelElementCheck Region(string uid,string level,string value,double geometry)=>new ModelElementCheck{
        UniqueId=uid,Name=uid,Entity="IfcSlab",Slot="建筑区域",LevelUniqueId=level,ProjectedAreaM2=geometry,ProjectionKey=uid,
        Fields=new[]{Area(uid,"建筑区域","投影面积",value)}};
    static string Suggest(ModelElementCheck r)=>r.Fields[0].SuggestedValue;
    public static void Run(Action<string,Action> check)
    {
        check("reference04_explicit_zero_regions_stay_zero_on_later_manual_floor_edits",()=>{
            var main=Region("660521","F1","1784.58",1398.706884);
            var auxiliary=Region("663445","F1","0",257.159413);
            var small=Region("717044","F1","0",5.115);
            var rows=new[]{Level("F1","1800"),main,auxiliary,small};
            var plan=FloorAreaPolicy.Build(rows,new PlanningSettings());
            Assert(plan.Issues.Length==0&&plan.Allocations.Length==1,"explicit zero region was redistributed or blocked");
            Assert(plan.Allocations[0].UniqueId=="660521"&&plan.Allocations[0].ValueM2=="1800.00","manual total did not remain on the participating region");
            Assert(auxiliary.Fields[0].Value=="0"&&small.Fields[0].Value=="0","explicit zeros were overwritten");
        });
        check("approved_school_floor_areas_distribute_exactly_without_changing_values_or_geometry",()=>{
            var levels=new[]{Level("F1","1784.58"),Level("F2","1638.73",4.2),Level("F3","1181.5",8.1),Level("F4","1181.5",12),Level("F5","669.29",15.9)};
            var regions=new[]{Region("a","F1","1398.706884",1398.706884),Region("b","F1","257.159413",257.159413),Region("c","F1","5.115",5.115),
                Region("d","F2","1886.559436",1886.559436),Region("e","F3","1702.928315",1702.928315),Region("f","F4","1152.184815",1152.184815),Region("g","F5","1177.452315",1177.452315)};
            var old=regions.Select(r=>r.Fields[0].Value).ToArray();var geometry=regions.Select(r=>r.ProjectedAreaM2).ToArray();
            PlanningInputs.Populate(levels.Concat(regions).ToArray(),new PlanningSettings(),null);
            Assert(regions.Select(Suggest).SequenceEqual(new[]{"1502.78","276.30","5.50","1638.73","1181.50","1181.50","669.29"}),"approved 7-region allocation missing or different");
            Assert(regions.Select(r=>r.Fields[0].Value).SequenceEqual(old)&&regions.Select(r=>r.ProjectedAreaM2).SequenceEqual(geometry),"scan mutated model values/geometry");
            Assert(regions.Sum(r=>decimal.Parse(Suggest(r),CultureInfo.InvariantCulture))==6455.60m,"five-floor total differs");
        });
        check("pending_floor_edit_drives_same_write_allocation_without_mutating_original_snapshot",()=>{
            var level=Level("F1","50");var rows=new[]{level,Region("a","F1","30",30),Region("b","F1","20",20)};
            var proposed=PlanningInputs.WithDraftValues(rows,new[]{new PlanningValueOverride{OwnerUniqueId="F1",PropertySet="Pset_建筑楼层信息属性集",Source="楼层面积",Binding="实例",Value="60"}});
            PlanningInputs.Populate(proposed,new PlanningSettings(),null);
            Assert(Suggest(proposed[1])=="36.00"&&Suggest(proposed[2])=="24.00","pending floor value did not synchronize its regions");
            Assert(level.Fields[0].Value=="50"&&Suggest(rows[1])==null,"draft mutated original scan");
        });
        check("area_sync_is_idempotent_and_does_not_keep_stale_suggestions",()=>{
            var rows=new[]{Level("F1","60"),Region("a","F1","30",30),Region("b","F1","20",20)};
            PlanningInputs.Populate(rows,new PlanningSettings(),null);
            Assert(Suggest(rows[1])!=null,"first allocation missing");
            foreach(var r in rows.Skip(1))r.Fields[0].Value=Suggest(r);
            PlanningInputs.Populate(rows,new PlanningSettings(),null);
            Assert(rows.All(r=>Suggest(r)==null),"same data keeps generating area writes");
        });
        check("automatic_floor_rounding_does_not_trigger_area_rewrites",()=>{
            var rows=new[]{Level("F1","1660.981298"),Region("a","F1","1398.706884",1398.706884),Region("b","F1","257.159413",257.159413),Region("c","F1","5.115",5.115)};
            PlanningInputs.Populate(rows,new PlanningSettings(),null);
            Assert(rows.Skip(1).All(r=>Suggest(r)==null),"sub-cent automatic precision was treated as an override");
        });
        check("matching_gate_area_exports_without_allocating_unconfirmed_floor_candidates",()=>{
            var level=Level("F1","28.56");var region=Region("gate","F1","28.56",28.56000005133308);
            var candidate=Region("other-floor","F1",null,.6299999561731705);
            candidate.Slot=null;candidate.Candidates=new[]{new ClassificationCandidate{Slot="建筑区域",RequiresReview=true}};
            var rows=new[]{level,region,candidate};var plan=FloorAreaPolicy.Build(rows,new PlanningSettings());
            Assert(plan.Issues.Length==0&&plan.Allocations.Length==0,"matching saved area was blocked by an unrelated unconfirmed floor candidate");
            PlanningInputs.Populate(rows,new PlanningSettings(),null);
            Assert(level.Fields[0].Value=="28.56"&&region.Fields[0].Value=="28.56"&&candidate.Fields[0].Value==null,"existing or candidate area was changed");
            Assert(rows.All(r=>r.AreaSynchronizationIssue==null)&&candidate.Candidates[0].RequiresReview,"candidate review was lost or became an export synchronization error");
        });
        check("changed_gate_area_still_requires_complete_region_scope",()=>{
            var region=Region("gate","F1","28.56",28.56);var candidate=Region("other-floor","F1",null,.63);
            candidate.Slot=null;candidate.Candidates=new[]{new ClassificationCandidate{Slot="建筑区域",RequiresReview=true}};
            var plan=FloorAreaPolicy.Build(new[]{Level("F1","30"),region,candidate},new PlanningSettings());
            Assert(plan.Issues.Length==1&&plan.Allocations.Length==0,"changed floor total was distributed despite unconfirmed region scope");
        });
        check("matching_area_does_not_hide_confirmed_region_overlap",()=>{
            var a=Region("a","F1","20",20);var b=Region("b","F1","8.56",8.56);a.AreaAggregationIssue="重叠";
            var plan=FloorAreaPolicy.Build(new[]{Level("F1","28.56"),a,b},new PlanningSettings());
            Assert(plan.Issues.Length==1&&plan.Allocations.Length==0,"matching total hid a known overlap between confirmed regions");
        });
        check("area_sync_uses_metric_values_for_native_area_parameters",()=>{
            var level=Level("F1","215.278208334");level.Fields[0].MetricValue=20;
            var a=Region("a","F1","129.166925",12);a.Fields[0].MetricValue=12;
            var b=Region("b","F1","193.750388",18);b.Fields[0].MetricValue=18;
            PlanningInputs.Populate(new[]{level,a,b},new PlanningSettings(),null);
            Assert(Suggest(a)=="8.00"&&Suggest(b)=="12.00","native square feet leaked into synchronization");
        });
        check("area_sync_does_not_allocate_to_incomplete_or_overlapping_regions",()=>{
            var level=Level("F1","60");var a=Region("a","F1","30",30);var b=Region("b","F1","20",20);
            b.Slot=null;b.Candidates=new[]{new ClassificationCandidate{Slot="建筑区域",RequiresReview=true}};
            PlanningInputs.Populate(new[]{level,a,b},new PlanningSettings(),null);
            Assert(Suggest(a)==null,"unconfirmed region was silently excluded");
            b.Slot="建筑区域";b.Candidates=new ClassificationCandidate[0];a.AreaAggregationIssue="重叠";
            PlanningInputs.Populate(new[]{level,a,b},new PlanningSettings(),null);
            Assert(Suggest(a)==null&&Suggest(b)==null,"overlap was silently distributed");
        });
        check("area_sync_rounding_never_creates_negative_region_values",()=>{
            var regions=Enumerable.Range(0,6).Select(i=>Region("r"+i,"F1","1",1)).ToArray();
            PlanningInputs.Populate(new[]{Level("F1","0.02")}.Concat(regions).ToArray(),new PlanningSettings(),null);
            Assert(regions.All(r=>Suggest(r)!=null),"small total not allocated");
            var values=regions.Select(r=>decimal.Parse(Suggest(r),CultureInfo.InvariantCulture)).ToArray();
            Assert(values.All(v=>v>=0)&&values.Sum()==.02m,"rounding produced a negative value or wrong total");
        });
        check("missing_floor_area_still_uses_existing_automatic_geometry_policy",()=>{
            var level=Level("F1",null);var a=Region("a","F1",null,30);var b=Region("b","F1",null,20);
            PlanningInputs.Populate(new[]{level,a,b},new PlanningSettings(),null);
            Assert(Suggest(level)=="50"&&Suggest(a)==null&&Suggest(b)==null,"empty floor changed synchronization scope");
        });
    }
}
