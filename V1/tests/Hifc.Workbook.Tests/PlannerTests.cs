using System;
using System.Linq;
using Hifc.Reference;

internal static class PlannerTests
{
    internal const string BuildingSet = "Pset_建筑技术信息属性集";
    internal static FieldCheck Field(string name, string value = "", string type = "Label", string set = BuildingSet)
        => new FieldCheck { PropertySet = set, Property = name, Source = name, Value = value, RawType = type, State = value == "" ? "未填写" : "有效", Editable = true, OwnerUniqueId = "building-1", Binding = "实例", SourceSignature = "baseline-" + name };
    internal static ModelElementCheck[] Model()
        => new[] {
            new ModelElementCheck { Id=1,UniqueId="building-1",Name="综合楼",Building="综合楼",Entity="IfcBuilding",Fields=new[] {
                Field("建筑名称","综合楼"),Field("建筑面积","70","Real"),Field("民用建筑","","Boolean"),Field("人数","","Integer"),Field("建筑体积","12","Real"),Field("建筑编号","保留编号") } },
            new ModelElementCheck { Id=2,UniqueId="level-1",Name="一层",Building="综合楼",Entity="IfcBuildingStorey",Fields=new[] { Field("楼层面积","","Real",FloorAreaPolicy.FloorSet) } },
            new ModelElementCheck { Id=3,UniqueId="region-1",Name="区域1",Building="综合楼",Entity="IfcSpace",LevelUniqueId="level-1",Fields=new[] { Field("投影面积","20","Real",FloorAreaPolicy.RegionSet) } }
        };
    internal static ReportingWorkbookTable Table()
        => ReportingWorkbookReader.Read(Program.Package("planner", Program.Fixture("normalized.xml"))).Tables.Single();
    public static void Run()
    {
        Program.Check("planner_keeps_zero_false_and_requires_explicit_conflict_selection", () => {
            var rows=Model();var table=Table();var plan=ReportingWorkbookPlanner.Build(table,rows,"单体建筑");
            Program.Assert(plan.Changes.Any(c=>c.Property=="民用建筑"&&c.Value=="0"&&c.Selected&&c.CanSelect),"false source did not fill empty Boolean");
            Program.Assert(plan.Changes.Any(c=>c.Property=="人数"&&c.Value=="0"&&c.Selected),"zero source did not fill empty numeric field");
            var conflict=plan.Changes.Single(c=>c.Property=="建筑面积");
            Program.Assert(conflict.IsConflict&&!conflict.Selected&&conflict.CanSelect&&conflict.CurrentValue=="70"&&conflict.Value=="60","existing human area was preselected or lost");
            Program.Assert(rows[0].Fields.Single(f=>f.Property=="民用建筑").Value==""&&rows[0].Fields.Single(f=>f.Property=="建筑面积").Value=="70","pure planner mutated model snapshot");
            var decision=ReportingWorkbookPlanner.Decide(plan);
            Program.Assert(decision.Changes.All(c=>c.Property!="建筑面积")&&decision.References.Any(r=>r.Field=="设计说明"),"default decision overwrote conflict or discarded unknown reference");
            conflict.Selected=true;decision=ReportingWorkbookPlanner.Decide(plan);
            Program.Assert(decision.Changes.Single(c=>c.Property=="建筑面积").SourceSignature=="baseline-建筑面积","explicit conflict selection did not preserve edit baseline");
        });
        Program.Check("planner_stages_manual_floor_total_in_existing_pipeline",()=>{
            var plan=ReportingWorkbookPlanner.Build(Table(),Model(),"单体建筑");var floor=plan.Changes.Single(c=>c.Property=="楼层面积");
            Program.Assert(floor.ElementUniqueId=="level-1"&&floor.PropertySet==FloorAreaPolicy.FloorSet&&floor.Value=="60"&&floor.Selected,"floor total did not target storey");
            Program.Assert(plan.Changes.All(c=>c.PropertySet!=FloorAreaPolicy.RegionSet),"workbook bypassed existing region redistribution pipeline");
        });
        Program.Check("blank_excel_never_erases_current_value_and_formula_without_cache_is_blocked",()=>{
            var table=Table();table.Items=table.Items.Concat(new[]{new ReportingWorkbookItem{Field="建筑编号",Value="",Provenance=table.Items[0].Provenance}}).ToArray();
            var plan=ReportingWorkbookPlanner.Build(table,Model(),"单体建筑");
            Program.Assert(plan.Changes.Single(c=>c.Property=="建筑编号").CanSelect==false&&!plan.Changes.Single(c=>c.Property=="建筑编号").IsConflict,"blank Excel can clear saved source or is falsely shown as a new value conflict");
            Program.Assert(plan.Changes.Single(c=>c.Property=="建筑体积").CanSelect==false&&!plan.Changes.Single(c=>c.Property=="建筑体积").IsConflict,"formula without cache was inferred or compared as a known value");
            Program.Assert(plan.References.Single(r=>r.Field=="设计说明").Status.Length>0,"unmapped source not retained with status");
        });
    }
}
