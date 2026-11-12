using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Hifc.Web.Revit;

internal static class NativeStatisticsFlow
{
    internal static async Task RunAsync(Func<Func<UIApplication, object>, Task<object>> native, Uri server, string run, Action<string> log)
    {
        var phone = "139" + new Random().Next(10000000, 99999999);
        var state = Path.Combine(run, "ss");
        using (var session = (WebModelSession)await native(ui => new WebModelSession(new NativeModelAccess(ui), server, state)))
        {
            await session.ConnectAsync(phone, "统计流程验收", true);
            await session.ScanAsync();
            foreach (var field in session.Result.Fields.Where(f => f.PropertySet == "Pset_建筑区域信息属性集" && (f.Property == "计算系数" || f.Property == "计容系数")))
                session.SetDraft(field, "0.5");
            await session.PreviewAsync(); await session.WriteAsync(); await session.SaveModelAsync();
            var report = await Capture(native, session, "building", run, "building", log);
            Require(report.Building.Total.DeclaredProjectionArea == 250m && report.Building.Total.GeometricArea == 200m, "Building statistics lost saved/native area units.");
            Require(report.Building.Total.CalculatedArea == 125m && report.Building.Total.FloorRatioArea == 125m, "Native 0.5 coefficients do not independently yield 125m².");
            await RenderNormalWindow(native, session, "building", run, log);
        }

        var configuration = JsonWire.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(run, "window-route.json")));
        var linkedPath = Path.Combine(run, "models", "statistics-linked.rvt");
        var hostPath = Path.Combine(run, "models", "statistics-site.rvt");
        await native(ui => { CreateSite(ui, configuration["template"], linkedPath, false); return true; });
        using (var session = (WebModelSession)await native(ui => new WebModelSession(new NativeModelAccess(ui), server, state)))
        {
            await session.ConnectAsync(phone, "统计流程验收", false);
            await PrepareSite(session, false); await session.SaveModelAsync();
        }
        await native(ui => {
            var previous = ui.ActiveUIDocument.Document;
            CreateSite(ui, configuration["template"], hostPath, true); previous.Close(false);
            return true;
        });
        using (var session = (WebModelSession)await native(ui => new WebModelSession(new NativeModelAccess(ui), server, state)))
        {
            await session.ConnectAsync(phone, "统计流程验收", false);
            await PrepareSite(session, true); await session.SaveModelAsync();
            await native(ui => {
                var document = ui.ActiveUIDocument.Document;
                using (var transaction = new Transaction(document, "Prepare owned linked statistics fixture"))
                {
                    transaction.Start();
                    var loaded = RevitLinkType.Create(document, ModelPathUtils.ConvertUserVisiblePathToModelPath(linkedPath), new RevitLinkOptions(false));
                    RevitLinkInstance.Create(document, loaded.ElementId); RevitLinkInstance.Create(document, loaded.ElementId);
                    transaction.Commit();
                }
                document.Save(); return true;
            });
            var report = await Capture(native, session, "site", run, "site-linked", log);
            Require(report.Site.Sources.Count(s => s.Selected) == 2, "Duplicate link placement was counted twice or a selected link disappeared.");
            var green = report.Site.Rows.Where(r => r.Kind == "绿地").OrderByDescending(r => r.IsSubtotal).First();
            Require(green.DeclaredArea == 800m && green.GeometricArea == 600m, "Host/link geometric and declared areas do not match 500+300 and400+200.");
            await RenderNormalWindow(native, session, "site", run, log);
        }
        File.WriteAllText(Path.Combine(run, "statistics-native-results.json"), JsonWire.Serialize(new { passed = true, buildingArea = 250, buildingGeometry = 200,
            calculatedArea = 125, floorRatioArea = 125, siteDeclaredGreen = 800, siteGeometricGreen = 600, duplicateLinkCountedOnce = true,
            nativeSnapshotUnchanged = true, actualHttpAndExcel = true, normalWindowEntry = true }));
    }

    private static async Task<StatisticsResponse> Capture(Func<Func<UIApplication, object>, Task<object>> native, WebModelSession session,
        string kind, string run, string name, Action<string> log)
    {
        var rule = await session.StatisticsRuleVersionAsync();
        var before = (string)await native(ui => Snapshot(ui.ActiveUIDocument.Document, run, name + "-before"));
        var capture = (NativeStatisticsCapture)await native(ui => {
            var sources = NativeStatisticsCapture.ListSources(ui.ActiveUIDocument.Document, "statistics-" + name, kind == "site");
            return new NativeStatisticsCapture(kind, rule, sources, sources.Select(s => s.Info.Key).ToArray(), session.Workspace.Settings.RuleProfileId);
        });
        try
        {
            foreach (var source in capture.Request.Sources.Where(s => s.Source.Selected && s.Source.Available))
                await native(ui => { capture.ReadInventory(source.Source.Key); return true; });
            var plan = await session.StatisticsCaptureAsync(capture.Request);
            foreach (var source in plan.Sources) await native(ui => { capture.ReadValues(source); return true; });
            var geometry = await session.StatisticsGeometryAsync(capture.Request);
            var measured = new List<StatisticsGeometryResult>();
            foreach (var group in geometry.Groups) measured.Add((StatisticsGeometryResult)await native(ui => capture.Measure(group)));
            capture.Request.CaptureHash = geometry.CaptureHash; capture.Request.Geometry = measured.ToArray();
            var report = await session.StatisticsReportAsync(capture.Request);
            var workbook = await session.StatisticsWorkbookAsync(capture.Request);
            Require(report.CaptureHash == workbook.CaptureHash && report.CaptureHash == StatisticsIdentity.Capture(capture.Request), "Statistics report/workbook is stale.");
            File.WriteAllBytes(Path.Combine(run, name + ".xlsx"), Convert.FromBase64String(workbook.ContentBase64));
            File.WriteAllText(Path.Combine(run, name + "-report.json"), JsonWire.Serialize(report));
            var after = (string)await native(ui => Snapshot(ui.ActiveUIDocument.Document, run, name + "-after"));
            Require(before == after, "Read-only statistics changed native model parameters or geometry.");
            log("PASS native statistics HTTP, geometry, Excel and read-only snapshot " + name);
            return report;
        }
        finally { await native(ui => { capture.Dispose(); return true; }); }
    }

    private static async Task RenderNormalWindow(Func<Func<UIApplication, object>, Task<object>> native, WebModelSession session, string kind, string run, Action<string> log)
    {
        var window = (Window)await native(ui => NativeStatisticsController.Open(ui, session.NewStatisticsSession(ui), kind));
        try
        {
            var report = window.GetType().GetProperty("CurrentReport", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (window.IsVisible && report.GetValue(window) == null && DateTime.UtcNow < deadline) await Task.Delay(200);
            Require(window.IsVisible && report.GetValue(window) != null, "Normal statistics window did not complete its own refresh.");
            window.UpdateLayout();
            var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var output = File.Create(Path.Combine(run, kind + "-statistics-window.png"))) encoder.Save(output);
            log("PASS actual Web statistics window refresh " + kind);
        }
        finally { window.Close(); }
    }

    private static async Task PrepareSite(WebModelSession session, bool host)
    {
        session.Workspace.ModelKind = "总图";
        session.SetSettings(new ModelSettings { RuleProfileId = "hubei-420626-planning-20261020" });
        await session.ScanAsync();
        foreach (var element in session.Result.Elements.Where(e => e.Element.Name.StartsWith("WEB_STAT_")))
            session.Assign(element.Element.UniqueId, element.Element.Name.Contains("LAND") ? "规划净用地" : "绿地");
        await session.ScanAsync();
        foreach (var field in session.Result.Fields)
        {
            if (field.Slot == "规划净用地" && field.Property == "投影面积") session.SetDraft(field, "1000");
            if (field.Slot == "绿地" && field.Property == "投影面积") session.SetDraft(field, host ? "500" : "300");
            if (field.Slot == "绿地" && field.Property == "折算系数") session.SetDraft(field, "0.5");
            if (field.Slot == "绿地" && field.Property == "绿地类型") session.SetDraft(field, field.Options.First());
        }
        await session.PreviewAsync(); await session.WriteAsync();
        Require(session.Workspace.LastReceipt?.ReadbackPassed == true, "Owned site fixture values were not written.");
    }

    private static void CreateSite(UIApplication ui, string template, string path, bool host)
    {
        var document = ui.Application.NewProjectDocument(template);
        try
        {
            using (var transaction = new Transaction(document, "Create owned Web statistics geometry"))
            {
                transaction.Start();
                var level = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).First();
                var type = new FilteredElementCollector(document).OfClass(typeof(FloorType)).Cast<FloorType>().First();
                var green = Rectangle(document, type, level, host ? 0 : 80, 20, host ? 20 : 10);
                green.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set("WEB_STAT_GREEN");
                var greenType = (FloorType)type.Duplicate("WEB_STAT_GREEN"); green.ChangeTypeId(greenType.Id);
                if (host) { var land = Rectangle(document, type, level, 0, 40, 25); var landType = (FloorType)type.Duplicate("WEB_STAT_LAND"); land.ChangeTypeId(landType.Id); }
                transaction.Commit();
            }
            document.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = false });
        }
        finally { document.Close(false); }
        ui.OpenAndActivateDocument(path);
    }
    private static Floor Rectangle(Document document, FloorType type, Level level, double x, double width, double depth)
    {
        var vertices = new[] { new XYZ(x / .3048, 0, level.Elevation), new XYZ((x + width) / .3048, 0, level.Elevation),
            new XYZ((x + width) / .3048, depth / .3048, level.Elevation), new XYZ(x / .3048, depth / .3048, level.Elevation) };
        var curves = new CurveArray();
        for (var index = 0; index < vertices.Length; index++) curves.Append(Line.CreateBound(vertices[index], vertices[(index + 1) % vertices.Length]));
        return document.Create.NewFloor(curves, type, level, false);
    }
    private static string Snapshot(Document document, string run, string label)
    {
        return (string)typeof(NativeExportExecutor).GetMethod("Snapshot", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { document, Path.Combine(run, label + ".json"), label });
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
