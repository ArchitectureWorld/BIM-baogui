using System;
using System.Linq;
using Hifc.Reference;

internal static class PlanningInputTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static FieldCheck Field(string slot,string name,string value=null) => new FieldCheck {
        PropertySet="Pset_"+slot+"信息属性集", Property=name, Source=name, RawType="Real",
        Value=value, State=value==null?"未填写":"有效", Binding="实例" };
    static ModelElementCheck Region(string id,string level,double area) => new ModelElementCheck {
        UniqueId=id,Name="楼面",Entity="IfcSlab",Category="楼板",Slot="建筑区域",LevelUniqueId=level,
        ProjectionKey=id,ProjectedAreaM2=area,Fields=new[]{Field("建筑区域","计算系数","1")} };
    static ModelElementCheck Level(string id,double elevation) => new ModelElementCheck {
        UniqueId=id,Name=id,Entity="IfcBuildingStorey",LevelUniqueId=id,LevelElevationM=elevation,
        Fields=new[]{Field("建筑楼层","建筑层高"),Field("建筑楼层","计算标高"),Field("建筑楼层","楼层面积")} };
    static string Suggested(ModelElementCheck row,string name)=>row.Fields.Single(f=>f.Property==name).SuggestedValue;
    public static void Run(Action<string, Action> check)
    {
        check("building_without_separate_doors_is_detected_from_walls_floors_and_levels", () => {
            var rows = Enumerable.Range(0, 12).Select(i => new ModelElementCheck { Entity="IfcWall" })
                .Concat(Enumerable.Range(0, 3).Select(i => new ModelElementCheck { Entity="IfcSlab", Category="楼板", Name="楼面_100", Level="F"+i }))
                .Concat(Enumerable.Range(0, 3).Select(i => new ModelElementCheck { Entity="IfcBuildingStorey" })).ToArray();
            Assert(CaseWorkflow.Detect(rows).Kind=="单体建筑", "curtain-wall building still depends on separate door/window instances");
        });
        check("ordinary_building_floor_is_a_review_candidate_not_silently_classified", () => {
            var row=new ModelElementCheck { Entity="IfcSlab", Category="楼板", Name="楼面_100" };
            var candidate=CaseWorkflow.GetCandidates(row,"单体建筑").SingleOrDefault(c=>c.Slot=="建筑区域");
            Assert(candidate!=null && !candidate.CanPrepare && candidate.RequiresReview, "floor is invisible or unconditionally classified");
            Assert(CaseWorkflow.GetCandidates(row,"总图").All(c=>c.Slot!="建筑区域"), "building inference leaked into site");
        });
        check("land_boundary_and_generic_road_are_visible_business_candidates", () => {
            foreach(var pair in new[]{new[]{"用地红线","规划净用地"},new[]{"道路","道路边界线"}}) {
                var row=new ModelElementCheck { Entity="IfcSlab", Name=pair[0], Category="楼板" };
                var candidate=CaseWorkflow.GetCandidates(row,"总图").SingleOrDefault(c=>c.Slot==pair[1]);
                Assert(candidate!=null && !candidate.CanPrepare, pair[0]+" must await business confirmation");
            }
        });
        check("sparse_generic_slabs_and_auxiliary_levels_do_not_prove_building", () => {
            var rows=new[]{new ModelElementCheck{Entity="IfcWall"},new ModelElementCheck{Entity="IfcSlab",Name="板"},new ModelElementCheck{Entity="IfcBuildingStorey"}};
            Assert(CaseWorkflow.Detect(rows).Kind=="待确认", "insufficient evidence became a building");
        });
        check("common_values_only_fill_matching_empty_fields_and_preserve_zero", () => {
            var first=Region("a","F1",30); first.Fields=new[]{Field("建筑区域","计算系数"),Field("建筑区域","计容系数","0")};
            var second=Region("b","F2",40); second.Fields=new[]{Field("绿地","折算系数")};
            var settings=new PlanningSettings(); settings.CommonValues["建筑区域|计算系数"]="1";
            settings.CommonValues["建筑区域|计容系数"]="1";
            PlanningInputs.Populate(new[]{first,second},settings,null);
            Assert(Suggested(first,"计算系数")=="1" && Suggested(first,"计容系数")==null && second.Fields[0].SuggestedValue==null,"shared settings overwrote or crossed business scopes");
        });
        check("business_confirmation_is_bound_to_current_identity_not_numeric_id", () => {
            var row=Region("a","F1",30);row.Slot=null;
            var settings=new PlanningSettings {Roles=new[]{new PlanningRoleChoice{UniqueId="a",Slot="建筑区域",Identity=PlanningInputs.Identity(row)}}};
            Assert(PlanningInputs.Candidates(row,"单体建筑",settings).Single().CanPrepare,"confirmed business choice ignored");
            row.ProjectionKey="changed-shape";
            Assert(PlanningInputs.Candidates(row,"单体建筑",settings).All(c=>!c.CanPrepare),"stale geometry confirmation reused");
            row.UniqueId="replaced";
            Assert(PlanningInputs.Candidates(row,"单体建筑",settings).All(c=>!c.CanPrepare),"replacement inherited confirmation");
        });
        check("levels_derive_height_and_area_from_confirmed_regions_not_unused_basement", () => {
            var f1=Level("F1",0);var f2=Level("F2",4.2);var unused=Level("-1F",-4.2);
            var project=new ModelElementCheck{Entity="IfcBuilding",Fields=new[]{Field("建筑技术","地上建筑层数"),Field("建筑技术","建筑面积"),Field("建筑技术","投影面积")}};
            PlanningInputs.Populate(new[]{f1,f2,unused,Region("a","F1",30),Region("b","F1",20),Region("c","F2",40),project},
                new PlanningSettings(),new UndergroundSettings{Confirmed=true,GroundElevationM=-0.6,BaseLevelUniqueId="F1"});
            Assert(Suggested(f1,"建筑层高")=="4.2" && Suggested(f1,"计算标高")=="0" && Suggested(f1,"楼层面积")=="50","floor inputs incorrect");
            Assert(Suggested(f2,"建筑层高")==null && Suggested(unused,"建筑层高")==null,"top height or unused basement invented");
            Assert(Suggested(project,"地上建筑层数")=="2" && Suggested(project,"建筑面积")=="90" && Suggested(project,"投影面积")=="50","whole-building totals incorrect");
        });
        check("overlapping_or_incomplete_regions_block_area_totals_but_not_elevations", () => {
            var f1=Level("F1",0);var a=Region("a","F1",30);var b=Region("b","F1",20);
            a.AreaAggregationIssue="与另一建筑区域投影重叠";
            PlanningInputs.Populate(new[]{f1,a,b},new PlanningSettings(),null);
            Assert(Suggested(f1,"楼层面积")==null && Suggested(f1,"计算标高")=="0","overlapping regions were double counted");
            a.AreaAggregationIssue=null;b.ProjectedAreaM2=null;
            PlanningInputs.Populate(new[]{f1,a,b},new PlanningSettings(),null);
            Assert(Suggested(f1,"楼层面积")==null,"partial geometry silently became full floor area");
        });
        check("deleted_confirmed_floor_does_not_fall_back_to_guessed_levels", () => {
            var level=Level("F1",0);
            PlanningInputs.Populate(new[]{level,Region("a","F1",30)},new PlanningSettings{
                LevelsConfirmed=true,BuildingLevelUniqueIds=new[]{"deleted"}},null);
            Assert(Suggested(level,"建筑层高")==null && Suggested(level,"楼层面积")==null,"invalid saved levels still produced totals");
        });
        check("duplicate_net_land_is_reported_only_for_two_active_carriers", () => {
            var a=Region("a","F1",30);a.Slot="规划净用地";a.ProjectionKey="same-xy";
            var b=Region("b","F2",30);b.Slot="规划净用地";b.ProjectionKey="same-xy";
            Assert(PlanningInputs.DuplicateLandIssues(new[]{a,b}).Length==1,"duplicate net land was not detected");
            b.Slot=null;
            Assert(PlanningInputs.DuplicateLandIssues(new[]{a,b}).Length==0,"unclassified copy blocked a single confirmed carrier");
        });
        check("calculation_gap_does_not_make_other_fields_mandatory_manual", () => {
            var region=Region("a","F1",30);region.Fields=new[]{Field("建筑区域","计容系数")};
            Assert(PlanningInputs.Issues(new[]{region},"单体建筑").Length>0,"missing calculation inputs hidden");
            Assert(!region.Fields[0].ManualInputRequired,"calculation gap became mandatory manual entry");
            Assert(PlanningInputs.CommonFields("单体建筑").Any(p=>p.Property=="主功能类别"),"no project-wide business values");
        });
        check("building_area_is_available_as_numeric_runtime_field_without_mutating_frozen_typo", () => {
            var field=CaseWorkflow.ForModel("报规","单体建筑").Single(r=>r.Slot=="建筑技术").Properties.SingleOrDefault(p=>p.Property=="建筑面积");
            Assert(field!=null && field.ExportType=="Real","whole-building area cannot reach the exporter");
            Assert(ReferenceCatalog.FromEmbeddedBaseline().ForSlot("建筑技术").Single(p=>p.Property=="建筑面积").RawType=="Reall","original evidence changed");
        });
        check("calculation_gap_list_does_not_require_unrelated_technical_fields",()=>{
            var project=new ModelElementCheck{Entity="IfcBuilding",Fields=new[]{Field("建筑技术","人数"),Field("建筑技术","建筑体积")}};
            Assert(!PlanningInputs.Issues(new[]{project},"单体建筑").Any(s=>s.Contains("人数")||s.Contains("建筑体积")),
                "optional technical values were generalized into calculation requirements");
        });
        check("totals_use_pending_factor_edits_without_mutating_scanned_values",()=>{
            var region=Region("a","F1",40);region.Fields[0].OwnerUniqueId="a";
            var project=new ModelElementCheck{Entity="IfcBuilding",Fields=new[]{Field("建筑技术","建筑面积")}};
            var rows=new[]{Level("F1",0),region,project};
            var proposed=PlanningInputs.WithDraftValues(rows,new[]{new PlanningValueOverride{OwnerUniqueId="a",
                PropertySet="Pset_建筑区域信息属性集",Source="计算系数",Binding="实例",Value="0.5"}});
            PlanningInputs.Populate(proposed,new PlanningSettings(),null);
            Assert(Suggested(proposed[2],"建筑面积")=="20","new factor was not used in same-write total");
            Assert(region.Fields[0].Value=="1"&&Suggested(project,"建筑面积")==null,"preview modified the source scan");
        });
    }
}
