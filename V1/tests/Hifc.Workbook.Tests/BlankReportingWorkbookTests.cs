using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Hifc.Reference;

internal static class BlankReportingWorkbookTests
{
    static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    internal static void Run()
    {
        Program.Check("static_reporting_template_keeps_every_business_value_blank", () => {
            var path = Destination("static-blank");
            ReportingWorkbookTemplate.Write(path, new[] { "综合楼", "大门" });
            var tables = ReportingWorkbookReader.Read(path).Tables;
            Program.Assert(tables.Length == 3, "Static template sheet scope changed");
            Program.Assert(tables.SelectMany(t => t.Items).All(i => i.Value == ""), "A business value, including building name, was prefilled");
            Program.Assert(tables.Single(t => t.BuildingName == "综合楼").Items.Any(i => i.Field == "建筑名称"), "Building identity or name field lost");
            AssertBlankValueCells(path);
        });
        Program.Check("model_reporting_template_keeps_actual_floor_labels_and_blank_values", () => {
            var path = Destination("model-blank");
            WriteForModel(path, "单体建筑", "1#养护楼", new[] { "B1", "1F", "1F 容积率0", "2F" });
            var table = ReportingWorkbookReader.Read(path).Tables.Single();
            Program.Assert(table.Kind == "单体建筑" && table.BuildingName == "1#养护楼", "Model identity was lost");
            Program.Assert(table.Items.Where(i => i.Field == "楼层面积").Select(i => i.LevelName)
                .SequenceEqual(new[] { "B1", "1F", "1F 容积率0", "2F" }), "Actual floor order or same-height auxiliary names changed");
            Program.Assert(table.Items.All(i => i.Value == ""), "Model-derived template contains business values");
            Program.Assert(table.Items.Any(i => i.Field == "建筑面积") && table.Items.Any(i => i.Field == "建筑名称"), "Fixed import fields missing");
            Program.Assert(!table.AmbiguousBuilding && table.Issues.Length == 0, "Blank model template cannot be read cleanly");
            AssertBlankValueCells(path);
        });
        Program.Check("site_reporting_template_exports_only_the_site_contract", () => {
            var path = Destination("site-blank");
            WriteForModel(path, "总图", "报规总图", new[] { "不应输出的楼层" });
            var table = ReportingWorkbookReader.Read(path).Tables.Single();
            Program.Assert(table.Kind == "总图" && table.Items.Length == 16, "Site template added a building sheet or changed fixed fields");
            Program.Assert(table.Items.All(i => i.Value == "" && i.LevelName == ""), "Site structure contains model data or storeys");
            Program.Assert(table.Items.Single(i => i.Field == "绿地率").Unit == "%", "Site percent unit lost");
            AssertBlankValueCells(path);
        });
        Program.Check("filled_model_template_matches_exact_auxiliary_floor_and_preserves_conflicts", () => {
            var path = Destination("model-filled");
            WriteForModel(path, "单体建筑", "综合楼", new[] { "一层", "一层 容积率0" });
            Fill(path, "建筑面积", "", "82.75");
            Fill(path, "楼层面积", "一层", "60.50");
            Fill(path, "楼层面积", "一层 容积率0", "22.25");
            var auxiliary = new ModelElementCheck {
                Id = 4, UniqueId = "level-auxiliary", Name = "一层 容积率0", Building = "综合楼", Entity = "IfcBuildingStorey",
                Fields = new[] { PlannerTests.Field("楼层面积", "", "Real", FloorAreaPolicy.FloorSet) }
            };
            auxiliary.Fields[0].OwnerUniqueId = auxiliary.UniqueId;
            var rows = PlannerTests.Model().Concat(new[] { auxiliary }).ToArray();
            rows[1].Fields[0].OwnerUniqueId = rows[1].UniqueId;
            var plan = ReportingWorkbookPlanner.Build(ReportingWorkbookReader.Read(path).Tables.Single(), rows, "单体建筑");
            var area = plan.Changes.Single(c => c.Property == "建筑面积");
            Program.Assert(area.IsConflict && !area.Selected && area.Value == "82.75", "Existing area conflict bypassed review");
            Program.Assert(plan.Changes.Single(c => c.ElementUniqueId == "level-1" && c.Property == "楼层面积").Value == "60.5", "Main floor value targeted incorrectly");
            Program.Assert(plan.Changes.Single(c => c.ElementUniqueId == "level-auxiliary" && c.Property == "楼层面积").Value == "22.25", "Auxiliary floor collapsed into the main floor");
            Program.Assert(plan.Changes.Single(c => c.Property == "建筑名称").CanSelect == false, "Blank business building name can overwrite current identity");
            var decision = ReportingWorkbookPlanner.Decide(plan);
            Program.Assert(decision.Changes.Length == 2 && decision.Changes.All(c => c.Property == "楼层面积"), "Filled template bypassed existing staging contract");
        });
        Program.Check("model_template_has_no_invented_storeys_and_protects_existing_files", () => {
            var path = Destination("no-storeys");
            WriteForModel(path, "单体建筑", "=1+1", new string[0]);
            var table = ReportingWorkbookReader.Read(path).Tables.Single();
            Program.Assert(table.Items.All(i => i.Field != "楼层面积"), "Missing level structure was replaced by a fictitious storey");
            Program.Assert(table.BuildingName == "=1+1", "Literal identity text changed");
            var before = File.ReadAllBytes(path);
            bool blocked = false;
            try { WriteForModel(path, "单体建筑", "改名", new[] { "一层" }); }
            catch (IOException) { blocked = true; }
            Program.Assert(blocked && before.SequenceEqual(File.ReadAllBytes(path)), "Existing workbook was overwritten");
            using (var zip = ZipFile.OpenRead(path))
                Program.Assert(!XDocument.Load(zip.GetEntry("xl/worksheets/sheet1.xml").Open()).Descendants(Main + "f").Any(), "Model name became a formula");
        });
        Program.Check("model_template_rejects_unknown_kind_before_creating_a_file", () => {
            var path = Destination("invalid-kind");
            bool blocked = false;
            try { WriteForModel(path, "待确认", "模型", new[] { "一层" }); }
            catch (InvalidOperationException) { blocked = true; }
            Program.Assert(blocked && !File.Exists(path), "Unknown model type produced a guessed template");
        });
        Program.Check("model_structure_unites_confirmed_levels_and_measurement_sources_without_merging_height", () => {
            var rows = LevelRows();
            var levels = ActualLevelNames(rows, new PlanningSettings {
                LevelsConfirmed = true, BuildingLevelUniqueIds = new[] { "main", "upper", "deleted" }
            });
            Program.Assert(levels.SequenceEqual(new[] { "一层", "一层 容积率0", "二层" }), "Confirmed and measured level identities were replaced by elevation groups or auxiliary datums");
            Program.Assert(rows.Single(r => r.UniqueId == "auxiliary").Name == "一层 容积率0", "Structure selection changed model labels");
        });
        Program.Check("model_structure_ignores_unconfirmed_levels_and_unclassified_measurement_candidates", () => {
            var rows = LevelRows();
            var levels = ActualLevelNames(rows, new PlanningSettings {
                LevelsConfirmed = false, BuildingLevelUniqueIds = new[] { "main", "upper", "datum" }
            });
            Program.Assert(levels.SequenceEqual(new[] { "一层 容积率0" }), "Unconfirmed settings or source candidates created actual floors");
            rows.Single(r => r.UniqueId == "carrier").Issue = "分类来源冲突";
            Program.Assert(ActualLevelNames(rows, new PlanningSettings()).Length == 0, "An ambiguous carrier silently created a floor");
        });
    }

    static string Destination(string name) => Path.Combine(Program.Output, name + "-" + Guid.NewGuid().ToString("N") + ".xlsx");

    static void WriteForModel(string path, string kind, string buildingName, IEnumerable<string> levels)
    {
        var method = typeof(ReportingWorkbookTemplate).GetMethod("WriteForModel", new[] { typeof(string), typeof(string), typeof(string), typeof(IEnumerable<string>) });
        Program.Assert(method != null, "Model-derived blank workbook API is missing");
        try { method.Invoke(null, new object[] { path, kind, buildingName, levels }); }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    }

    static string[] ActualLevelNames(ModelElementCheck[] rows, PlanningSettings settings)
    {
        var method = typeof(ReportingWorkbookTemplate).GetMethod("ActualLevelNames", new[] { typeof(ModelElementCheck[]), typeof(PlanningSettings) });
        Program.Assert(method != null, "Actual model level selection is missing");
        return (string[])method.Invoke(null, new object[] { rows, settings });
    }

    static ModelElementCheck[] LevelRows() => new[] {
        new ModelElementCheck { Id = 12, UniqueId = "upper", Entity = "IfcBuildingStorey", Name = "二层", LevelElevationM = 3.6 },
        new ModelElementCheck { Id = 11, UniqueId = "auxiliary", Entity = "IfcBuildingStorey", Name = "一层 容积率0", LevelElevationM = 0 },
        new ModelElementCheck { Id = 10, UniqueId = "main", Entity = "IfcBuildingStorey", Name = "一层", LevelElevationM = 0 },
        new ModelElementCheck { Id = 13, UniqueId = "datum", Entity = "IfcBuildingStorey", Name = "女儿墙顶", LevelElevationM = 4.8 },
        new ModelElementCheck { Id = 20, UniqueId = "carrier", Entity = "IfcSlab", Slot = "架空", Classified = true, LevelUniqueId = "auxiliary" },
        new ModelElementCheck { Id = 21, UniqueId = "candidate", Entity = "IfcSlab", SuggestedSlots = new[] { "建筑区域" }, LevelUniqueId = "datum" }
    };

    static void AssertBlankValueCells(string path)
    {
        using (var zip = ZipFile.OpenRead(path))
            foreach (var part in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)))
            {
                var sheet = XDocument.Load(part.Open());
                var rows = sheet.Descendants(Main + "row").Where(r => (int)r.Attribute("r") >= 6).ToArray();
                Program.Assert(rows.Length > 0, "No editable fields exported");
                foreach (var row in rows)
                {
                    var address = "C" + (string)row.Attribute("r");
                    var value = row.Elements(Main + "c").Single(c => (string)c.Attribute("r") == address);
                    Program.Assert(value.Value == "" && !value.Descendants(Main + "f").Any(), "Business cell " + address + " contains data or a formula");
                }
            }
    }

    static void Fill(string path, string property, string level, string value)
    {
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var part = zip.GetEntry("xl/worksheets/sheet1.xml");
            XDocument sheet;
            using (var stream = part.Open()) sheet = XDocument.Load(stream);
            var row = sheet.Descendants(Main + "row").Single(r =>
                r.Elements(Main + "c").Any(c => ((string)c.Attribute("r")).StartsWith("B", StringComparison.Ordinal) && c.Value == property) &&
                r.Elements(Main + "c").Any(c => ((string)c.Attribute("r")).StartsWith("A", StringComparison.Ordinal) && c.Value == level));
            row.Elements(Main + "c").Single(c => ((string)c.Attribute("r")).StartsWith("C", StringComparison.Ordinal)).Descendants(Main + "t").Single().Value = value;
            part.Delete();
            Program.Add(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
        }
    }
}
