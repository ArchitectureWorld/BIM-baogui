using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Hifc.Reference;

namespace Hifc.Revit
{
    internal static class Smoke
    {
        internal static void Register(UIControlledApplication application)
        {
            var path = Environment.GetEnvironmentVariable("HIFC_V1_SMOKE_OUTPUT");
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || File.Exists(path)) return;
            EventHandler<Autodesk.Revit.UI.Events.IdlingEventArgs> handler = null;
            handler = (sender, args) =>
            {
                application.Idling -= handler;
                Run(sender as UIApplication, application, path);
            };
            application.Idling += handler;
        }

        private static void Run(UIApplication ui, UIControlledApplication controlled, string path)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            var result = new Dictionary<string, object>
            {
                ["schema_version"] = "hifc.revit-smoke/1.0", ["allPassed"] = false,
                ["assembly_path"] = typeof(App).Assembly.Location, ["assembly_sha256"] = Evidence.FileHash(typeof(App).Assembly.Location),
                ["reference_sha256"] = ReferenceCatalog.BaselineSha256, ["utc"] = DateTime.UtcNow.ToString("O"),
                ["scope"] = "fresh unsaved isolated model; not original software A/B"
            };
            Document doc = null;
            try
            {
                if (ui == null) throw new InvalidOperationException("No UIApplication from Idling.");
                if (ui.ActiveUIDocument != null || ui.Application.Documents.Cast<Document>().Any())
                    throw new InvalidOperationException("Smoke refuses to run while another model is open.");
                result["ribbonButtons"] = controlled.GetRibbonPanels("H-IFC V1").SelectMany(p => p.GetItems()).Select(i => i.Name).ToArray();
                doc = ui.Application.NewProjectDocument(UnitSystem.Metric);
                Floor land, balcony;
                using (var tx = new Transaction(doc, "HIFC V1 isolated specimens"))
                {
                    tx.Start();
                    // Blank addresses in the stock 2020 exporter create invalid empty lists.
                    // This address belongs only to this unsaved test fixture.
                    doc.ProjectInformation.Address = "H-IFC V1 isolated smoke specimen";
                    var type = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().First();
                    var level = Level.Create(doc, 0);
                    land = Floor(doc, type, level, 0); balcony = Floor(doc, type, level, 14);
                    RevitMappingService.Prepare(doc, new Element[] { land }, "规划净用地", directory);
                    RevitMappingService.Prepare(doc, new Element[] { balcony }, "阳台", directory);
                    foreach (var element in new Element[] { land, balcony })
                        foreach (var property in RevitMappingService.Properties(doc, element))
                        {
                            var value = property.RawType == "Boolean" ? "0" : property.RawType == "Real" ? "26.25" : "probe-" + property.Property;
                            if (element.Id == land.Id && property.Property == "投影面积") value = "40.5";
                            var parameter = element.GetParameters(property.SourceParameter).Single();
                            if (!parameter.Set(value)) throw new InvalidOperationException("Specimen value write failed.");
                        }
                    tx.Commit();
                }
                var beforeCount = new FilteredElementCollector(doc).OfClass(typeof(SharedParameterElement)).GetElementCount();
                using (var tx = new Transaction(doc, "HIFC V1 idempotence"))
                {
                    tx.Start();
                    RevitMappingService.Prepare(doc, new Element[] { land }, "规划净用地", directory);
                    RevitMappingService.Prepare(doc, new Element[] { balcony }, "阳台", directory);
                    tx.Commit();
                }
                var afterCount = new FilteredElementCollector(doc).OfClass(typeof(SharedParameterElement)).GetElementCount();
                result["prepare_idempotent"] = beforeCount == afterCount;
                var elements = new Element[] { land, balcony };
                Evidence.Write(Path.Combine(directory, "observations.json"), RevitMappingService.Capture(doc, elements));
                var beforeExportCount = new FilteredElementCollector(doc).OfClass(typeof(SharedParameterElement)).GetElementCount();
                result["export"] = NativeExportService.Export(doc, elements, Path.Combine(directory, "reproduction.ifc"));
                var afterExportCount = new FilteredElementCollector(doc).OfClass(typeof(SharedParameterElement)).GetElementCount();
                result["temporary_parameters_rolled_back"] = beforeExportCount == afterExportCount;
                result["sample_element_ids"] = elements.Select(e => e.Id.IntegerValue).ToArray();
                bool blocked = false;
                using (var tx = new Transaction(doc, "HIFC V1 missing slot negative check"))
                {
                    tx.Start(); land.GetParameters("类目").Single().Set("");
                    try { RevitMappingService.Properties(doc, land); } catch (InvalidOperationException) { blocked = true; }
                    tx.RollBack();
                }
                result["missing_slot_blocked"] = blocked;
                Floor unclassified;
                using (var tx = new Transaction(doc, "HIFC management unselected specimen"))
                {
                    tx.Start(); var type = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().First();
                    var level = doc.GetElement(land.LevelId) as Level;
                    unclassified = Floor(doc, type, level, 28); tx.Commit();
                }
                result["management"] = Management.ManagementSmoke.Run(doc, land, balcony, unclassified);
                result["allPassed"] = beforeCount == afterCount && beforeExportCount == afterExportCount && blocked;
            }
            catch (Exception ex) { result["error"] = ex.ToString(); }
            finally
            {
                try { if (doc != null && doc.IsValidObject) doc.Close(false); }
                catch (Exception ex) { result["cleanup_error"] = ex.ToString(); result["allPassed"] = false; }
                Evidence.Write(path, result);
                if (ui != null && Environment.GetEnvironmentVariable("HIFC_V1_SMOKE_EXIT") == "1")
                {
                    // Exiting from the initial Idling callback is rejected during startup.
                    // Queue it on a later idle, only while no other document is open.
                    EventHandler<Autodesk.Revit.UI.Events.IdlingEventArgs> exit = null;
                    exit = (sender, args) =>
                    {
                        controlled.Idling -= exit;
                        var idleUi = sender as UIApplication;
                        if (idleUi == null || idleUi.ActiveUIDocument != null || idleUi.Application.Documents.Cast<Document>().Any()) return;
                        var command = RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);
                        if (idleUi.CanPostCommand(command)) idleUi.PostCommand(command);
                    };
                    controlled.Idling += exit;
                }
            }
        }

        private static Floor Floor(Document doc, FloorType type, Level level, double offset)
        {
            var boundary = new CurveArray();
            XYZ[] points = { Point(offset, 0), Point(offset + 8, 0), Point(offset + 8, 5), Point(offset, 5) };
            for (int index = 0; index < points.Length; index++) boundary.Append(Line.CreateBound(points[index], points[(index + 1) % points.Length]));
            return doc.Create.NewFloor(boundary, type, level, false);
        }
        private static XYZ Point(double x, double y) { return new XYZ(x / 0.3048, y / 0.3048, 0); }
    }
}
