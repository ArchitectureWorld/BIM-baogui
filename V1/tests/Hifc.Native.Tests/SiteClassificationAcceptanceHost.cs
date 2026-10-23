using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

// Exercises installed production services in a copy of a user-provided site model.
public sealed class SiteClassificationAcceptanceHost : IExternalApplication, IExternalEventHandler
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    readonly Dictionary<string, bool> checks = new Dictionary<string, bool>();
    UIControlledApplication application; ExternalEvent api; Assembly plugin, core; string run;
    static object Get(object value, string property) => value.GetType().GetProperty(property, F).GetValue(value);
    static void Set(object value, string property, object item) => value.GetType().GetProperty(property, F).SetValue(value, item);
    static object[] Items(object value) => ((IEnumerable)value).Cast<object>().ToArray();
    object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, F).Invoke(value, args);
    object Static(string type, string method, params object[] args) => plugin.GetType(type, true).GetMethod(method, F).Invoke(null, args);
    object New(string type, params object[] args) => Activator.CreateInstance(plugin.GetType(type, true), F, null, args, null);
    void Write(string name, object value) => File.WriteAllText(Path.Combine(run, name), new JavaScriptSerializer { MaxJsonLength = 67108864 }.Serialize(value));
    static string Hash(string path) { using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    void Check(string name, bool value) { checks[name] = value; File.AppendAllText(Path.Combine(run, "host.log"), DateTime.Now.ToString("O") + " " + name + " " + value + "\n"); if (!value) throw new Exception(name); }
    static string Raw(Element element, string name)
    {
        var parameter = element.GetParameters(name).SingleOrDefault();
        if (parameter == null || !parameter.HasValue) return null;
        if (parameter.StorageType == StorageType.Double)
        {
            var value = parameter.AsDouble();
            if (parameter.Definition.UnitType == UnitType.UT_Area) value = UnitUtils.ConvertFromInternalUnits(value, DisplayUnitType.DUT_SQUARE_METERS);
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
        return parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
    }
    public Result OnStartup(UIControlledApplication app) { run = Environment.GetEnvironmentVariable("HIFC_V1_SITE_TEST_RUN"); if (string.IsNullOrEmpty(run)) return Result.Succeeded; application = app; app.Idling += Start; return Result.Succeeded; }
    public Result OnShutdown(UIControlledApplication app) { if (application != null) { app.Idling -= Start; app.Idling -= Exit; } api?.Dispose(); return Result.Succeeded; }
    public string GetName() => "HIFC site classification acceptance";
    void Start(object sender, IdlingEventArgs args) { application.Idling -= Start; api = ExternalEvent.Create(this); api.Raise(); }
    void Exit(object sender, IdlingEventArgs args) { var ui = (UIApplication)sender; var id = RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit); if (ui.CanPostCommand(id)) { application.Idling -= Exit; ui.PostCommand(id); } }
    object Read(Document doc, Element[] elements) => Static("Hifc.Revit.Management.SelectionService", "Read", doc, elements.Select(e => e.Id.IntegerValue).ToArray(), "报规", "总图");
    void Assign(Document doc, Element element, string slot)
    {
        var result = Read(doc, new[] { element });
        Check(slot + "_offered_" + element.Id.IntegerValue, Items(Get(result, "Slots")).Cast<string>().Contains(slot));
        var snapshot = Get(result, "Snapshot");
        var preview = Static("Hifc.Revit.Management.SelectionService", "Classify", doc, snapshot, slot);
        Check(slot + "_preview_has_no_conflicts", Items(Get(preview, "Conflicts")).Length == 0);
        var draft = New("Hifc.Revit.Management.SelectionDraft", snapshot);
        Call(draft, "SetClassification", slot, Get(preview, "ProposedElements"));
        foreach (var field in Items(Get(draft, "Fields")))
        {
            var value = (string)Get(field, "Value"); var property = (string)Get(field, "Property");
            if (!string.IsNullOrWhiteSpace(value) && value != "未填写") continue;
            var type = (string)Get(field, "RawType");
            Set(field, "Value", type == "Boolean" ? "否" : type == "Integer" || type == "Real" ? "1"
                : property == "绿地类型" ? "公共绿地" : property == "红线类型" ? "建筑控制线" : "SITE-TEST-1");
        }
        Static("Hifc.Revit.Management.SelectionService", "Write", doc, snapshot, Call(draft, "BuildRequests"));
        Check(slot + "_written_" + element.Id.IntegerValue, Raw(element, "类目") == slot);
    }
    Area CreateArea(Document doc, ViewPlan view, SketchPlane sketch, double x, double y, double widthM, double heightM, string name)
    {
        var z = view.GenLevel.Elevation;
        var points = new[] { new XYZ(x, y, z), new XYZ(x + widthM / .3048, y, z),
            new XYZ(x + widthM / .3048, y + heightM / .3048, z), new XYZ(x, y + heightM / .3048, z) };
        for (var i = 0; i < 4; i++) doc.Create.NewAreaBoundaryLine(sketch, Line.CreateBound(points[i], points[(i + 1) % 4]), view);
        doc.Regenerate();
        var area = doc.Create.NewArea(view, new UV(x + widthM / .6096, y + heightM / .6096)); area.Name = name;
        return area;
    }
    void VerifyProjectExport(Document doc, Floor green, Element control)
    {
        Floor ordinary; Area parking, unclassified, invalid; View3D hiddenView;
        using (var transaction = new Transaction(doc, "Acceptance-only whole-project carriers"))
        {
            transaction.Start(); var level = (Level)doc.GetElement(green.LevelId); var box = green.get_BoundingBox(null);
            var x = box.Min.X - 150; var y = box.Min.Y - 150; var z = level.Elevation;
            var points = new[] { new XYZ(x, y, z), new XYZ(x + 3 / .3048, y, z), new XYZ(x + 3 / .3048, y + 2 / .3048, z), new XYZ(x, y + 2 / .3048, z) };
            var profile = new CurveArray(); for (var i = 0; i < 4; i++) profile.Append(Line.CreateBound(points[i], points[(i + 1) % 4]));
            ordinary = doc.Create.NewFloor(profile, (FloorType)doc.GetElement(green.GetTypeId()), level, false);
            var scheme = new FilteredElementCollector(doc).OfClass(typeof(AreaScheme)).Cast<AreaScheme>().First();
            var plan = ViewPlan.CreateAreaPlan(doc, scheme.Id, level.Id);
            var sketch = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, z)));
            parking = CreateArea(doc, plan, sketch, x - 200, y, 5, 2.5, "全项目验收-停车位");
            unclassified = CreateArea(doc, plan, sketch, x - 230, y, 3, 2, "全项目验收-未分类面积");
            invalid = doc.Create.NewArea(plan, new UV(x - 500, y - 500)); invalid.Name = "全项目验收-未闭合面积";
            var viewType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(v => v.ViewFamily == ViewFamily.ThreeDimensional);
            hiddenView = View3D.CreateIsometric(doc, viewType.Id); hiddenView.HideElements(new[] { ordinary.Id });
            transaction.Commit();
        }
        Check("whole_project_fixture_has_unclassified_geometry_and_two_valid_areas", string.IsNullOrEmpty(Raw(ordinary, "类目"))
            && parking.Area > 0 && unclassified.Area > 0 && invalid.Area == 0 && ordinary.IsHidden(hiddenView));
        Assign(doc, parking, "停车位");
        var scope = Static("Hifc.Revit.ProjectExportScope", "Read", doc);
        var scopedIds = Items(Get(scope, "Elements")).Cast<Element>().Select(e => e.Id.IntegerValue).ToArray();
        Check("project_scope_includes_unclassified_geometry_and_all_area_records", new[] { ordinary.Id, parking.Id, unclassified.Id, invalid.Id }.All(id => scopedIds.Contains(id.IntegerValue)));
        Check("invalid_area_has_explicit_report", Items(Get(scope, "Issues")).Any(issue => (int)Get(issue, "element_id") == invalid.Id.IntegerValue && (string)Get(issue, "code") == "AREA_NOT_ENCLOSED"));
        var protectedElements = new Element[] { green, control, ordinary, parking, unclassified };
        var before = new JavaScriptSerializer().Serialize(Static("Hifc.Revit.RevitMappingService", "Capture", doc, protectedElements));
        var modelIds = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds().Select(id => id.IntegerValue).OrderBy(id => id).ToArray();
        Static("Hifc.Revit.NativeExportService", "ExportProject", doc, Path.Combine(run, "project.ifc"));
        Check("whole_project_export_restores_mapped_and_unmapped_values", before == new JavaScriptSerializer().Serialize(Static("Hifc.Revit.RevitMappingService", "Capture", doc, protectedElements)));
        Check("whole_project_export_creates_no_persistent_view_or_binding", modelIds.SequenceEqual(new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds().Select(id => id.IntegerValue).OrderBy(id => id)));
        var receipt = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(run, "project.receipt.json")));
        Check("whole_project_receipt_confirms_no_selection_or_view_filter", (string)receipt["export_scope"] == "CURRENT_PROJECT" && receipt["selected_elements"] == null
            && !(bool)receipt["current_view_filter_applied"] && !(bool)receipt["linked_source_documents_exported"]);
        Write("project-export-expectations.json", new { ordinary_id = ordinary.Id.IntegerValue, ordinary_area_m2 = 6.0,
            green_id = green.Id.IntegerValue, green_area_m2 = 50.0, control_id = control.Id.IntegerValue,
            parking_id = parking.Id.IntegerValue, parking_area_m2 = 12.5, unclassified_area_id = unclassified.Id.IntegerValue,
            unclassified_area_m2 = 6.0, invalid_area_id = invalid.Id.IntegerValue,
            scope = "Production whole-project service without any selected-ID input; area plans and hidden unclassified slab included." });
    }
    public void Execute(UIApplication ui)
    {
        Document doc = null;
        try
        {
            var launch = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(run, "launch.json")));
            var source = (string)launch["source_path"]; var copy = (string)launch["model"];
            plugin = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Hifc.V1.Revit");
            core = Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(plugin.Location), "Hifc.Reference.Core.dll"));
            Check("current_build_loaded", plugin.GetName().Version == typeof(SiteClassificationAcceptanceHost).Assembly.GetName().Version);
            doc = ui.Application.OpenDocumentFile(copy);
            var control = doc.GetElement(new ElementId(717724)); var existingGreen = doc.GetElement(new ElementId(719249)); var peer = doc.GetElement(new ElementId(718055));
            Check("expected_customer_floor_carriers_exist", control is Floor && existingGreen is Floor && peer is Floor);
            Floor green;
            using (var transaction = new Transaction(doc, "Acceptance-only fresh greenery slab"))
            {
                transaction.Start(); var level = (Level)doc.GetElement(existingGreen.LevelId); var z = level.Elevation;
                var box = existingGreen.get_BoundingBox(null); var x = box.Min.X - 60; var y = box.Min.Y - 60;
                var points = new[] { new XYZ(x, y, z), new XYZ(x + 10 / .3048, y, z), new XYZ(x + 10 / .3048, y + 5 / .3048, z), new XYZ(x, y + 5 / .3048, z) };
                var boundary = new CurveArray(); for (var index = 0; index < points.Length; index++) boundary.Append(Line.CreateBound(points[index], points[(index + 1) % points.Length]));
                green = doc.Create.NewFloor(boundary, (FloorType)doc.GetElement(existingGreen.GetTypeId()), level, false);
                transaction.Commit();
            }
            var initial = Read(doc, new Element[] { green }); var initialSnapshot = Get(initial, "Snapshot"); var greenRow = Items(Get(initialSnapshot, "Elements")).Single();
            Check("new_customer_green_type_has_automatic_green_candidate", Items(Get(greenRow, "Candidates")).Any(c => (string)Get(c, "Slot") == "绿地" && (bool)Get(c, "CanPrepare")));
            var automatic = Static("Hifc.Revit.Management.CaseEdits", "Preview", doc, initialSnapshot, null);
            Check("automatic_preview_does_not_write_model", string.IsNullOrEmpty(Raw(green, "类目")));
            Check("automatic_green_preview_has_no_conflicts", Items(Get(automatic, "Conflicts")).Length == 0);
            Static("Hifc.Revit.Management.CaseEdits", "Apply", doc, automatic);
            Check("automatic_green_class_written", Raw(green, "类目") == "绿地");
            var areaText = Raw(green, "绿地投影面积");
            Check("automatic_green_area_uses_own_geometry", double.TryParse(areaText, NumberStyles.Float, CultureInfo.InvariantCulture, out var area) && Math.Abs(area - 50) < .001);
            Assign(doc, green, "绿地");
            var peerClass = Raw(peer, "类目"); var typeClass = Raw(doc.GetElement(control.GetTypeId()), "类目");
            var sourceExportAs = Raw(control, "IfcExportAs"); var typeExportAs = Raw(doc.GetElement(control.GetTypeId()), "IfcExportAs");
            var sourceExportType = Raw(control, "IfcExportType"); var typeExportType = Raw(doc.GetElement(control.GetTypeId()), "IfcExportType");
            Assign(doc, control, "规划控制线");
            Check("control_source_values_are_present_before_cleanup", !string.IsNullOrEmpty(Raw(control, "规划控制线名称")) && !string.IsNullOrEmpty(Raw(control, "红线类型")) && !string.IsNullOrEmpty(Raw(control, "编号")));
            Check("control_classification_preserves_physical_floor", control is Floor && (string)Static("Hifc.Revit.RevitMappingService", "SourceEntity", doc, control) == "IfcSlab");
            Check("control_mapping_uses_proxy", (string)Static("Hifc.Revit.RevitMappingService", "Entity", doc, control) == "IfcBuildingElementProxy");
            Check("unselected_instance_and_shared_type_class_unchanged", Raw(peer, "类目") == peerClass && Raw(doc.GetElement(control.GetTypeId()), "类目") == typeClass);
            var all = new Element[] { green, control, peer };
            var beforeExport = new JavaScriptSerializer().Serialize(Static("Hifc.Revit.RevitMappingService", "Capture", doc, all));
            Static("Hifc.Revit.NativeExportService", "Export", doc, new Element[] { green, control }, Path.Combine(run, "mixed.ifc"));
            Static("Hifc.Revit.NativeExportService", "Export", doc, new Element[] { control }, Path.Combine(run, "control-only.ifc"));
            Check("selected_and_unselected_values_restored_after_exports", beforeExport == new JavaScriptSerializer().Serialize(Static("Hifc.Revit.RevitMappingService", "Capture", doc, all)));
            Check("instance_and_type_export_overrides_do_not_persist", Raw(control, "IfcExportAs") == sourceExportAs && Raw(doc.GetElement(control.GetTypeId()), "IfcExportAs") == typeExportAs
                && Raw(control, "IfcExportType") == sourceExportType && Raw(doc.GetElement(control.GetTypeId()), "IfcExportType") == typeExportType);
            VerifyProjectExport(doc, green, control);
            var job = New("Hifc.Revit.Management.CaseScanJob", doc, "报规", "总图");
            while (!(bool)Get(job, "Complete")) Call(job, "ReadNext", doc, 500);
            var scan = Call(job, "Snapshot", doc); var scannedControl = Items(Get(scan, "Elements")).Single(row => (int)Get(row, "Id") == control.Id.IntegerValue);
            Check("full_recheck_retains_control_line_without_carrier_gap", (string)Get(scannedControl, "Slot") == "规划控制线" && string.IsNullOrEmpty((string)Get(scannedControl, "CoverageNote")));
            Assign(doc, control, "绿地");
            Check("old_control_values_cleared_after_switch_to_green", string.IsNullOrEmpty(Raw(control, "规划控制线名称")) && string.IsNullOrEmpty(Raw(control, "红线类型")) && string.IsNullOrEmpty(Raw(control, "编号")));
            Check("switch_back_restores_slab_mapping", (string)Static("Hifc.Revit.RevitMappingService", "Entity", doc, control) == "IfcSlab");
            Static("Hifc.Revit.NativeExportService", "Export", doc, new Element[] { control }, Path.Combine(run, "changed-to-green.ifc"));
            var greenId = green.Id; var controlId = control.Id; var peerId = peer.Id;
            doc.Save(); doc.Close(false); doc = ui.Application.OpenDocumentFile(copy);
            Check("classification_persists_after_save_reopen", Raw(doc.GetElement(greenId), "类目") == "绿地" && Raw(doc.GetElement(controlId), "类目") == "绿地");
            Check("source_file_unchanged", Hash(source) == (string)launch["source_sha256"]);
            Write("site-host-result.json", new { all_passed = true, checks, version = plugin.GetName().Version.ToString(), assembly_sha256 = Hash(plugin.Location),
                core_assembly_sha256 = Hash(core.Location), source_unchanged = true,
                green_id = greenId.IntegerValue, control_id = controlId.IntegerValue, peer_id = peerId.IntegerValue,
                scope = "Customer site model copy, plus one fresh slab using its greenery type; production services, full recheck, native IFC and save/reopen; not platform acceptance" });
        }
        catch (Exception exception)
        {
            while (exception is TargetInvocationException && exception.InnerException != null) exception = exception.InnerException;
            Write("site-host-result.json", new { all_passed = false, error = exception.ToString(), checks });
        }
        finally { if (doc?.IsValidObject == true) doc.Close(false); application.Idling += Exit; }
    }
}
