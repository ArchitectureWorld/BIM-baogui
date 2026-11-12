using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Hifc.Reference;
using Hifc.Revit;
using Hifc.Revit.Management;

public static class BuildingAreaStatisticsProof
{
    private sealed class FixtureWarnings : IFailuresPreprocessor
    {
        internal readonly List<string> Messages = new List<string>();
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var failure in accessor.GetFailureMessages())
            {
                Messages.Add(failure.GetDescriptionText());
                if (failure.GetSeverity() != FailureSeverity.Warning) return FailureProcessingResult.ProceedWithRollBack;
                accessor.DeleteWarning(failure);
            }
            return FailureProcessingResult.Continue;
        }
    }
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 128 * 1024 * 1024 };
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
    public static string Run(Document host, string folder)
    {
        var app = host.Application;
        var activePath = host.PathName; var modified = host.IsModified;
        var reports = new List<object>();
        foreach (var name in new[] { "b1.rvt", "b2.rvt" })
        {
            var doc = app.OpenDocumentFile(Path.Combine(folder, name));
            try
            {
                Configure(doc);
                var before = doc.IsModified;
                var candidates = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                    .Where(e => e is Floor || e is RoofBase || e is Area || e is Level || e is ProjectInfo).ToArray();
                var fingerprint = Evidence.Hash(Json.Serialize(RevitMappingService.Capture(doc, candidates)));
                var report = BuildingAreaStatisticsReader.Read(doc, name);
                Require(report.Components.Length > 0 && report.Total.CalculatedArea > 0, name + " has no participating areas");
                Require(report.Components.All(c => c.CalculatedArea == c.DeclaredProjectionArea * c.CalculationFactor), "Calculated formula differs");
                Require(report.Components.All(c => c.FloorRatioArea == c.DeclaredProjectionArea * c.FloorRatioFactor), "FAR formula differs");
                Require(doc.IsModified == before, "Read changed modified state");
                Require(fingerprint == Evidence.Hash(Json.Serialize(RevitMappingService.Capture(doc, candidates))), "Read changed source parameters");
                File.WriteAllText(Path.Combine(folder, name + ".statistics.json"), Json.Serialize(report));
                BuildingAreaStatisticsWorkbook.Write(Path.Combine(folder, name + ".statistics.xlsx"), report);
                reports.Add(new { model = name, included = report.Components.Length, pending = report.Pending.Length,
                    calculated = report.Total.CalculatedArea, far = report.Total.FloorRatioArea, geometry = report.Total.GeometricArea,
                    complete = report.Complete, source_unchanged = true });
            }
            finally { doc.Close(false); }
        }
        var fixture = app.NewProjectDocument(@"C:\ProgramData\Autodesk\RVT 2020\Templates\China\DefaultCHSCHS.rte");
        try { reports.Add(Fixture(fixture, folder)); }
        finally { fixture.Close(false); }
        Require(host.PathName == activePath && host.IsModified == modified, "Unrelated host state changed");
        var result = new { success = true, reports, host_path = activePath, host_modified = modified, source_saved = false };
        File.WriteAllText(Path.Combine(folder, "native-result.json"), Json.Serialize(result));
        return Json.Serialize(result);
    }

    private static void Configure(Document doc)
    {
        PlatformContext.Set(doc, new PlanningSettings { RuleProfileId = PlatformRules.ProfileId });
        PlatformContext.SetStage(doc, "报规"); PlatformContext.SetKind(doc, "单体建筑");
    }

    private static object Fixture(Document doc, string folder)
    {
        Configure(doc);
        Floor region, open, unrelated, balcony, sloped;
        var warnings = new FixtureWarnings();
        using (var tx = new Transaction(doc, "统计验收夹具"))
        {
            tx.Start();
            tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(warnings).SetClearAfterRollback(true));
            var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).First();
            level.Name = "1F";
            var auxiliary = Level.Create(doc, level.Elevation); auxiliary.Name = "1F 容积率0";
            region = Slab(doc, level, 0); open = Slab(doc, auxiliary, 5); unrelated = Slab(doc, level, 30); balcony = Slab(doc, level, 50);
            open.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(1.0 / .3048);
            var slopeProfile = new CurveArray();
            var slopePoints = new[] { new XYZ(70/.3048,0,0), new XYZ(80/.3048,0,0), new XYZ(80/.3048,10/.3048,0), new XYZ(70/.3048,10/.3048,0) };
            for (var i = 0; i < 4; i++) slopeProfile.Append(Line.CreateBound(slopePoints[i], slopePoints[(i+1)%4]));
            sloped = doc.Create.NewSlab(slopeProfile, level, Line.CreateBound(slopePoints[0], slopePoints[1]), .1, false);
            Fill(doc, region, "建筑区域", "100", "1", "0", folder);
            Fill(doc, open, "架空", "100", "1", "1", folder);
            Fill(doc, balcony, "阳台", "20.36", "0.5", "0.5", folder);
            Fill(doc, sloped, "建筑区域", "100", "1", "1", folder);
            unrelated.GetParameters("架空投影面积").Single().Set("777");
            doc.Regenerate(); Require(tx.Commit() == TransactionStatus.Committed, "Fixture transaction failed " + string.Join("; ", warnings.Messages));
        }
        var baseline = BuildingAreaStatisticsReader.Read(doc, "fixture");
        Require(baseline.Floors.Length == 1 && baseline.Components.Length == 4, "Same-height grouping failed");
        Require(baseline.Pending.Single().ElementId == unrelated.Id.IntegerValue, "Residual unclassified area entered statistics");
        Require(Math.Abs(baseline.Total.GeometricArea.Value - 350m) < .000001m, "Projected union differs from analytic 350m2");
        Require(Math.Abs(baseline.Components.Single(c => c.ElementId == sloped.Id.IntegerValue).GeometricArea.Value - 100m) < .000001m, "Sloped area was not projected horizontally");
        Require(sloped.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED).AsDouble() * .09290304 > 100.1, "Fixture slab is not actually sloped");
        Require(baseline.Total.CalculatedArea == 310.18m && baseline.Total.FloorRatioArea == 210.18m, "Native coefficient reading differs");
        Require(baseline.Floors[0].Notes.Contains("重叠") && !baseline.Complete, "Overlap was not disclosed");
        using (var tx = new Transaction(doc, "刷新验收夹具"))
        {
            tx.Start(); region.GetParameters("建筑区域投影面积").Single().Set("110"); tx.Commit();
        }
        Require(BuildingAreaStatisticsReader.Read(doc, "fixture").Total.CalculatedArea == 320.18m, "Refresh reused old source data");
        Controller(doc, region);
        var modifiedBefore = doc.IsModified;
        var ifc = Path.Combine(folder, "open-owner-test.ifc");
        NativeExportService.Export(doc, new Element[] { region, open, unrelated, balcony }, ifc);
        Require(doc.IsModified == modifiedBefore, "Export did not restore modified state");
        var result = new { model = "fixture", union_m2 = baseline.Total.GeometricArea, calculated = baseline.Total.CalculatedArea,
            far = baseline.Total.FloorRatioArea, open_owner = open.Id.IntegerValue, balcony_owner = balcony.Id.IntegerValue,
            unrelated = unrelated.Id.IntegerValue, same_height_merged = true, refreshed = true, sloped_projection = true,
            controller_error_recovery = true, controller_stale_refresh = true, controller_disposed = true,
            ifc, native_export_rollback = true, warnings = warnings.Messages };
        File.WriteAllText(Path.Combine(folder, "fixture-result.json"), Json.Serialize(result));
        return result;
    }

    private static void Controller(Document doc, Floor changed)
    {
        var ui = new UIApplication(doc.Application);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(BuildingAreaStatisticsController);
        var controller = (BuildingAreaStatisticsController)Activator.CreateInstance(type, flags, null, new object[] { ui }, CultureInfo.InvariantCulture);
        var window = (BuildingAreaStatisticsWindow)type.GetField("window", flags).GetValue(controller);
        try
        {
            var entries = (System.Collections.IEnumerable)type.GetField("documents", flags).GetValue(controller);
            var entry = entries.Cast<object>().Single(e => ((Document)e.GetType().GetField("Document", flags).GetValue(e)).Equals(doc));
            var key = (string)entry.GetType().GetField("Key", flags).GetValue(entry);
            window.SetDocuments(new[] { new BuildingAreaDocumentOption { Key = key, Name = "fixture" } }, key);
            PlatformContext.SetKind(doc, "总图");
            type.GetMethod("BeginRead", flags).Invoke(controller, new object[] { key });
            Require(window.RefreshButton.IsEnabled && window.DocumentSelector.IsEnabled, "Read error left controls busy");
            PlatformContext.SetKind(doc, "单体建筑");
            RefreshController(controller, window, ui, type, flags);
            var before = window.CurrentReport.Total.CalculatedArea;
            using (var tx = new Transaction(doc, "控制器过期验证"))
            {
                tx.Start(); changed.GetParameters("建筑区域投影面积").Single().Set("115"); tx.Commit();
            }
            Require(!window.ExportButton.IsEnabled, "Changed model left stale Excel export enabled");
            RefreshController(controller, window, ui, type, flags);
            Require(window.CurrentReport.Total.CalculatedArea == before + 5 && window.ExportButton.IsEnabled, "Controller refresh did not renew values");
        }
        finally { window.Close(); controller.Execute(ui); }
        Require((bool)type.GetField("disposed", flags).GetValue(controller), "Closed controller retained subscriptions");
    }

    private static void RefreshController(BuildingAreaStatisticsController controller, BuildingAreaStatisticsWindow window, UIApplication ui, Type type, BindingFlags flags)
    {
        window.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); controller.Execute(ui);
        var reader = (BuildingAreaStatisticsReader)type.GetField("reader", flags).GetValue(controller);
        Require(reader != null, "Refresh request did not start reader");
        while (!reader.Complete) reader.ReadNext();
        type.GetMethod("Idle", flags).Invoke(controller, new object[] { null, null });
        Require(window.CurrentReport != null && window.ExportButton.IsEnabled, "Controller did not publish current report");
    }

    private static Floor Slab(Document doc, Level level, double x)
    {
        var points = new[] { new XYZ(x/.3048,0,0), new XYZ((x+10)/.3048,0,0), new XYZ((x+10)/.3048,10/.3048,0), new XYZ(x/.3048,10/.3048,0) };
        var curves = new CurveArray();
        for (var i = 0; i < 4; i++) curves.Append(Line.CreateBound(points[i], points[(i+1)%4]));
        var type = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().First(t => !t.IsFoundationSlab);
        return doc.Create.NewFloor(curves, type, level, false);
    }

    private static void Fill(Document doc, Floor floor, string slot, string area, string calculation, string ratio, string folder)
    {
        RevitMappingService.Prepare(doc, new Element[] { floor }, slot, folder);
        foreach (var property in CaseExportPolicy.PropertiesForSlot(slot, PlatformRules.ProfileId))
        {
            var p = floor.GetParameters(property.SourceParameter).Single();
            if (p.StorageType != StorageType.String) continue;
            var value = property.Property == "投影面积" ? area : property.Property == "计算系数" ? calculation : property.Property == "计容系数" ? ratio
                : property.Property == "主功能类别" || property.Property == "公建大类" ? "办公" : property.Property == "子功能类别" || property.Property == "公建小类" ? "行政办公" : null;
            if (value != null) p.Set(value);
        }
    }
}
