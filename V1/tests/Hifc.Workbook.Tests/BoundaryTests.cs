using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Hifc.Reference;

internal static class BoundaryTests
{
    static bool Blocked(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }
    public static void Run()
    {
        Program.Check("legacy_mixed_blocks_keep_duplicates_and_merged_floor_names",()=>{
            var book=ReportingWorkbookReader.Read(Program.Package("legacy",Program.Fixture("legacy-mixed.xml")));
            Program.Assert(book.Tables.Length==2&&book.Tables.Select(t=>t.Kind).Distinct().Count()==2,"left building and right site block merged");
            var table=book.Tables.Single(t=>t.Kind=="单体建筑");
            Program.Assert(table.AmbiguousBuilding&&table.Issues.Any(i=>i.Contains("重复")),"mixed/duplicate evidence not exposed");
            Program.Assert(table.Items.Count(i=>i.Field=="耐火等级")==2&&table.Items.Any(i=>i.LevelName=="二层"&&i.Value=="50"),"duplicate evidence or merged floor label discarded");
            var rows=PlannerTests.Model();rows[0].Fields=rows[0].Fields.Concat(new[]{PlannerTests.Field("耐火等级"),PlannerTests.Field("建筑类型名称")}).ToArray();
            var plan=ReportingWorkbookPlanner.Build(table,rows,"单体建筑");
            Program.Assert(plan.Changes.All(c=>!c.Selected)&&plan.Changes.Single(c=>c.Property=="建筑面积").CanSelect==false,"mixed building table silently selected or slash value split");
            var fire=plan.Changes.Where(c=>c.Property=="耐火等级").ToArray();fire[0].Selected=true;fire[1].Selected=true;
            Program.Assert(Blocked(()=>ReportingWorkbookPlanner.Decide(plan)),"duplicate sources can overwrite same target twice");
            fire[1].Selected=false;Program.Assert(ReportingWorkbookPlanner.Decide(plan).Changes.Length==1,"single explicit duplicate source cannot be chosen");
        });
        Program.Check("multi_sheet_and_multi_file_tables_stay_independent",()=>{
            var sheet=Program.Fixture("normalized.xml");var first=ReportingWorkbookReader.Read(Program.Package("two-buildings",sheet,sheet.Replace("<v>0</v>","<v>0</v>")));
            var second=ReportingWorkbookReader.Read(Program.Package("separate-building",sheet));
            Program.Assert(first.Tables.Length==2&&first.Tables[0].Id!=first.Tables[1].Id&&first.Tables[0].Id!=second.Tables[0].Id,"table provenance identities collided across sheet/file");
        });
        Program.Check("different_building_names_in_one_sheet_require_explicit_scope_review",()=>{
            var xml=Program.Fixture("normalized.xml").Replace("</sheetData>","<row r='30'><c r='B30' t='inlineStr'><is><t>建筑名称</t></is></c><c r='C30' t='inlineStr'><is><t>大门</t></is></c></row></sheetData>");
            var table=ReportingWorkbookReader.Read(Program.Package("two-identities-one-sheet",xml)).Tables.Single();
            var plan=ReportingWorkbookPlanner.Build(table,PlannerTests.Model(),"单体建筑");
            Program.Assert(table.AmbiguousBuilding&&plan.Changes.All(c=>!c.Selected),"a single sheet containing different building identities automatically filled first building");
        });
        Program.Check("duplicate_model_building_and_floor_names_never_guess_targets",()=>{
            var model=PlannerTests.Model();var extra=new ModelElementCheck{Id=4,UniqueId="other",Name="综合楼",Building="综合楼",Entity="IfcBuilding",Fields=model[0].Fields};
            var plan=ReportingWorkbookPlanner.Build(PlannerTests.Table(),model.Concat(new[]{extra}).ToArray(),"单体建筑");
            Program.Assert(plan.Changes.Length==0&&plan.Issues.Length>0,"ambiguous building matched first model row");
            model=model.Concat(new[]{new ModelElementCheck{Id=5,UniqueId="level-duplicate",Name="一层",Building="综合楼",Entity="IfcBuildingStorey",Fields=model[1].Fields}}).ToArray();
            plan=ReportingWorkbookPlanner.Build(PlannerTests.Table(),model,"单体建筑");
            Program.Assert(plan.Changes.All(c=>c.Property!="楼层面积")&&plan.References.Single(r=>r.Field=="楼层面积").Status.Contains("不唯一"),"same-name floors guessed by list order");
        });
        Program.Check("existing_zero_and_false_are_real_values_not_empty_defaults",()=>{
            var table=PlannerTests.Table();var rows=PlannerTests.Model();rows[0].Fields.Single(f=>f.Property=="民用建筑").Value="0";rows[0].Fields.Single(f=>f.Property=="人数").Value="0";
            table.Items.Single(i=>i.Field=="民用建筑").Value="1";table.Items.Single(i=>i.Field=="人数").Value="3";
            var plan=ReportingWorkbookPlanner.Build(table,rows,"单体建筑");
            Program.Assert(plan.Changes.Where(c=>c.Property=="民用建筑"||c.Property=="人数").All(c=>c.IsConflict&&!c.Selected),"valid zero/false treated as blank and overwritten");
        });
        Program.Check("source_units_and_custom_date_formats_are_not_guessed",()=>{
            var table=PlannerTests.Table();table.Items.Single(i=>i.Field=="人数").Provenance.NumberFormat="yyyy-mm-dd";
            table.Items.Single(i=>i.Field=="建筑面积").Unit="cm²";
            var plan=ReportingWorkbookPlanner.Build(table,PlannerTests.Model(),"单体建筑");
            Program.Assert(plan.Changes.Single(c=>c.Property=="人数").CanSelect==false,"Excel custom date serial imported as number");
            Program.Assert(plan.Changes.Single(c=>c.Property=="建筑面积").CanSelect==false,"unconfirmed area unit silently converted");
        });
        Program.Check("date_serials_cannot_become_boolean_or_building_codes",()=>{
            var table=PlannerTests.Table();table.Items.Single(i=>i.Field=="民用建筑").Provenance.NumberFormat="yyyy-mm-dd";
            table.Items.Single(i=>i.Field=="建筑名称").Value="1";table.Items.Single(i=>i.Field=="建筑名称").Provenance.NumberFormat="yyyy-mm-dd";
            var plan=ReportingWorkbookPlanner.Build(table,PlannerTests.Model(),"单体建筑");
            Program.Assert(plan.Changes.Single(c=>c.Property=="民用建筑").CanSelect==false&&plan.Changes.Single(c=>c.Property=="建筑名称").CanSelect==false,"formatted date serial interpreted as business Boolean or code");
        });
        Program.Check("xlsx_dtd_and_external_sheet_relationships_are_rejected",()=>{
            var dtd="<!DOCTYPE worksheet [<!ENTITY xxe SYSTEM 'file:///C:/does-not-exist'>]>"+Program.Fixture("normalized.xml");
            Program.Assert(Blocked(()=>ReportingWorkbookReader.Read(Program.Package("dtd",dtd))),"DTD was accepted");
            var path=Program.Package("external",Program.Fixture("normalized.xml"));
            using(var zip=ZipFile.Open(path,ZipArchiveMode.Update)) {zip.GetEntry("xl/_rels/workbook.xml.rels").Delete();Program.Add(zip,"xl/_rels/workbook.xml.rels","<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet' Target='https://example.invalid/sheet.xml' TargetMode='External'/></Relationships>");}
            Program.Assert(Blocked(()=>ReportingWorkbookReader.Read(path)),"external workbook relationship was read");
        });
        Program.Check("site_source_metric_disagreements_are_visible_without_rewriting_values",()=>{
            var book=ReportingWorkbookReader.Read(Program.Package("site-metrics",Program.Fixture("legacy-mixed.xml")));var table=book.Tables.Single(t=>t.Kind=="总图");
            Program.Assert(table.Issues.Any(i=>i.Contains("建筑密度"))&&table.Issues.Any(i=>i.Contains("绿地率"))&&table.Issues.Any(i=>i.Contains("容积率")),"source metric inconsistencies hidden");
            var plan=ReportingWorkbookPlanner.Build(table,PlannerTests.Model(),"总图");
            Program.Assert(plan.Changes.Length==0&&plan.References.Single(r=>r.Field=="建筑密度").Value=="0.2217","site metrics invented IFC mapping or replaced original value");
        });
    }
}
