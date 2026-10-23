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
using Autodesk.Revit.DB.IFC;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

// Controlled test inputs only. Creates its own source RVT, then exercises a copy.
// No production project or platform data is opened or changed by this host.
public sealed class PlatformAcceptanceHost : IExternalApplication, IExternalEventHandler
{
    const string Profile = "hubei-420626-planning-20261020";
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    readonly Dictionary<string, bool> checks = new Dictionary<string, bool>();
    UIControlledApplication application;
    ExternalEvent api;
    Assembly plugin, core;
    string run, phase = "startup";
    static double Ft(double m) => m / .3048;
    static object Get(object value, string name) => value.GetType().GetProperty(name, F).GetValue(value);
    static void Set(object value, string name, object item) => value.GetType().GetProperty(name, F).SetValue(value, item);
    static object[] Items(object value) => ((IEnumerable)value).Cast<object>().ToArray();
    static MethodInfo Method(Type type, string name, object[] args) => type.GetMethods(F).Single(method =>
        method.Name == name && method.GetParameters().Length == args.Length && method.GetParameters().Select((p, i) =>
            args[i] == null ? !p.ParameterType.IsValueType || Nullable.GetUnderlyingType(p.ParameterType) != null : p.ParameterType.IsInstanceOfType(args[i])).All(v => v));
    static object Call(object target, string name, params object[] args) => Method(target.GetType(), name, args).Invoke(target, args);
    object Static(string type, string name, params object[] args) => Method(plugin.GetType(type, true), name, args).Invoke(null, args);
    object New(string type, params object[] args) => Activator.CreateInstance(plugin.GetType(type, true), F, null, args, null);
    object CoreJson(string type, object value) => new JavaScriptSerializer().Deserialize(Json(value), core.GetType("Hifc.Reference." + type, true));
    static string Json(object value) => new JavaScriptSerializer { MaxJsonLength = 128 * 1024 * 1024 }.Serialize(value);
    void Write(string name, object value) => File.WriteAllText(Path.Combine(run, name), Json(value));
    static string Hash(string path) { using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    static Exception Unwrap(Exception ex) { while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException; return ex; }
    void Check(string name, bool value)
    {
        checks[name] = value;
        File.AppendAllText(Path.Combine(run, "host.log"), DateTime.Now.ToString("O") + " " + name + " " + value + "\n");
        if (!value) throw new InvalidOperationException("Platform acceptance failed: " + name);
    }
    void Phase(string value)
    {
        phase = value;
        File.AppendAllText(Path.Combine(run, "host.log"), DateTime.Now.ToString("O") + " phase " + value + "\n");
    }
    public string GetName() => "HIFC scoped platform fixture acceptance";
    public Result OnStartup(UIControlledApplication app)
    {
        run = Environment.GetEnvironmentVariable("HIFC_V1_PLATFORM_TEST_RUN");
        if (string.IsNullOrWhiteSpace(run)) return Result.Succeeded;
        if (!Path.IsPathRooted(run) || !File.Exists(Path.Combine(run, "launch.json"))) return Result.Failed;
        application = app; app.Idling += Start; return Result.Succeeded;
    }
    public Result OnShutdown(UIControlledApplication app)
    {
        if (application != null) { app.Idling -= Start; app.Idling -= Exit; }
        api?.Dispose(); return Result.Succeeded;
    }
    void Start(object sender, IdlingEventArgs args) { application.Idling -= Start; api = ExternalEvent.Create(this); api.Raise(); }
    void Exit(object sender, IdlingEventArgs args)
    {
        var ui = (UIApplication)sender; var command = RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);
        if (ui.CanPostCommand(command)) { application.Idling -= Exit; ui.PostCommand(command); }
    }
    static void StartTransaction(Transaction tx)
    {
        tx.Start(); tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new NativeTestWarnings()));
    }
    Guid BindFixtureText(Document doc, string name, Category category, bool typeSource)
    {
        var path = Path.Combine(run, typeSource ? "fixture-type-source.txt" : "fixture-window-conflict-source.txt");
        var guid = Guid.NewGuid();
        File.WriteAllText(path, "# Revit shared parameters\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\nGROUP\t1\tPlatformFixture\r\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\r\n", System.Text.Encoding.Unicode);
        var previous = doc.Application.SharedParametersFilename;
        try
        {
            doc.Application.SharedParametersFilename = path;
            var definition = doc.Application.OpenSharedParameterFile().Groups.get_Item("PlatformFixture").Definitions.Create(
                new ExternalDefinitionCreationOptions(name, ParameterType.Text) { GUID = guid, Visible = true, UserModifiable = true });
            var categories = doc.Application.Create.NewCategorySet(); categories.Insert(category);
            var binding = typeSource ? (Binding)doc.Application.Create.NewTypeBinding(categories) : doc.Application.Create.NewInstanceBinding(categories);
            if (!doc.ParameterBindings.Insert(definition, binding, BuiltInParameterGroup.PG_DATA))
                throw new InvalidOperationException("Could not bind controlled fixture source.");
        }
        finally { doc.Application.SharedParametersFilename = previous; }
        return guid;
    }
    static Floor Floor(Document doc, FloorType type, Level level, double x, double width, double depth)
    {
        var z = level.Elevation;
        var points = new[] { new XYZ(Ft(x), 0, z), new XYZ(Ft(x + width), 0, z), new XYZ(Ft(x + width), Ft(depth), z), new XYZ(Ft(x), Ft(depth), z) };
        var boundary = new CurveArray();
        for (var i = 0; i < points.Length; i++) boundary.Append(Line.CreateBound(points[i], points[(i + 1) % 4]));
        return doc.Create.NewFloor(boundary, type, level, false);
    }
    static Area Area(Document doc, Level level, double x, double width, double depth)
    {
        var scheme = new FilteredElementCollector(doc).OfClass(typeof(AreaScheme)).Cast<AreaScheme>().First();
        var view = ViewPlan.CreateAreaPlan(doc, scheme.Id, level.Id);
        var z = level.Elevation;
        var sketch = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, z)));
        var points = new[] { new XYZ(Ft(x), 0, z), new XYZ(Ft(x + width), 0, z), new XYZ(Ft(x + width), Ft(depth), z), new XYZ(Ft(x), Ft(depth), z) };
        for (var i = 0; i < points.Length; i++) doc.Create.NewAreaBoundaryLine(sketch, Line.CreateBound(points[i], points[(i + 1) % 4]), view);
        doc.Regenerate(); return doc.Create.NewArea(view, new UV(Ft(x + width / 2), Ft(depth / 2)));
    }
    static object ParameterEvidence(Parameter p)
    {
        object value = null;
        if (p.HasValue)
        {
            if (p.StorageType == StorageType.String) value = p.AsString();
            else if (p.StorageType == StorageType.ElementId) value = p.AsElementId().IntegerValue;
            else if (p.StorageType == StorageType.Integer) value = p.AsInteger();
            else if (p.StorageType == StorageType.Double) value = p.AsDouble();
        }
        return new { name = p.Definition.Name, parameter_id = p.Id.IntegerValue,
            builtin = p.Id.IntegerValue < 0 ? Enum.GetName(typeof(BuiltInParameter), p.Id.IntegerValue) : null,
            shared = p.IsShared, guid = p.IsShared ? p.GUID.ToString("D") : null,
            storage_type = p.StorageType.ToString(), definition_type = p.Definition.ParameterType.ToString(),
            has_value = p.HasValue, read_only = p.IsReadOnly, value,
            display = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString() };
    }
    void WindowSourceEvidence(Document doc, Dictionary<string, int> ids, string filename)
    {
        var names = new[] { "标高", "底高度", "高度", "宽度", "类目" };
        Write(filename, new { expected_level_id = ids["second_level"], expected_level_m = 5.7,
            elements = new[] { "window", "unclassified_window" }.Select(role =>
            {
                var element = doc.GetElement(new ElementId(ids[role])); var type = doc.GetElement(element.GetTypeId());
                return new { role, id = element.Id.IntegerValue, level_id = element.LevelId.IntegerValue,
                    instance_parameters = names.SelectMany(name => element.GetParameters(name)).Select(ParameterEvidence).ToArray(),
                    type_parameters = names.SelectMany(name => type.GetParameters(name)).Select(ParameterEvidence).ToArray(),
                    production_reads = names.Select(name => new { name, reads = Static("Hifc.Revit.RevitMappingService", "Reads", doc, element, name) }).ToArray() };
            }).ToArray() });
    }
    static string Raw(Element owner, string name)
    {
        var candidates = owner.GetParameters(name).ToArray();
        if (candidates.Length > 1) throw new InvalidOperationException("Ambiguous fixture read: " + owner.Id.IntegerValue + "/" + name + " " + Json(candidates.Select(ParameterEvidence).ToArray()));
        var p = candidates.SingleOrDefault();
        if (p == null || !p.HasValue) return null;
        return p.StorageType == StorageType.String ? p.AsString() : p.StorageType == StorageType.ElementId ? p.AsElementId().IntegerValue.ToString(CultureInfo.InvariantCulture)
            : p.StorageType == StorageType.Integer ? p.AsInteger().ToString(CultureInfo.InvariantCulture) : p.AsDouble().ToString("R", CultureInfo.InvariantCulture);
    }
    static void Text(Element owner, string name, string value)
    {
        var candidates = owner.GetParameters(name).ToArray();
        if (candidates.Length != 1) throw new InvalidOperationException("Ambiguous fixture write: " + owner.Id.IntegerValue + "/" + name + " " + Json(candidates.Select(ParameterEvidence).ToArray()));
        var p = candidates[0];
        if (p.StorageType != StorageType.String || p.IsReadOnly || !p.Set(value) || p.AsString() != value) throw new Exception("Fixture text write failed: " + name);
    }
    void Assign(Document doc, Element element, string slot, Dictionary<string, string> values)
    {
        Phase("assign " + element.Id.IntegerValue + " " + slot);
        Static("Hifc.Revit.RevitMappingService", "Prepare", doc, new[] { element }, slot, Path.Combine(run, "bindings"));
        var properties = Items(Static("Hifc.Revit.RevitMappingService", "Properties", doc, element));
        foreach (var pair in values)
        {
            var property = properties.First(p => (string)Get(p, "Property") == pair.Key);
            var source = (string)Get(property, "SourceParameter");
            var owner = element.GetParameters(source).Count > 0 ? element : doc.GetElement(element.GetTypeId());
            Text(owner, source, pair.Value);
        }
    }
    object Scan(Document doc, object planning)
    {
        var job = New("Hifc.Revit.Management.CaseScanJob", doc, "报规", "单体建筑", null, planning);
        while (!(bool)Get(job, "Complete")) Call(job, "ReadNext", doc, 100);
        return Call(job, "Snapshot", doc);
    }
    object Store(Document doc) => New("Hifc.Revit.Management.ManagementStateStore",
        Path.Combine((string)Static("Hifc.Revit.RuntimePaths", "OutputDirectory"), "management-profiles"), Static("Hifc.Revit.Management.ModelReader", "Key", doc));
    void PersistPlanning(Document doc, object planning)
    {
        var store = Store(doc); var state = Call(store, "Load"); Set(state, "Stage", "报规"); Set(state, "Kind", "单体建筑"); Set(state, "Planning", planning); Call(store, "Save", state);
        Static("Hifc.Revit.Management.PlatformContext", "Set", doc, planning);
        Static("Hifc.Revit.Management.PlatformContext", "SetStage", doc, "报规");
        Check("profile_stored_in_management_state", (string)Get(Get(Call(store, "Load"), "Planning"), "RuleProfileId") == Profile);
    }
    void VerifyWallRecognition(Document doc, object planning, Dictionary<string, int> ids)
    {
        Phase("wall dedicated source positive and negative cases");
        var explicitWall = doc.GetElement(new ElementId(ids["disabled_wall"]));
        var sources = new[] { "类目", "是否为女儿墙", "高度" };
        var before = Json(sources.Select(name => new { name, value = Raw(explicitWall, name) }).ToArray());
        var allElements = new LogicalOrFilter(new ElementIsElementTypeFilter(false), new ElementIsElementTypeFilter(true));
        var allIds = new FilteredElementCollector(doc).WherePasses(allElements).ToElementIds().Select(id => id.IntegerValue).OrderBy(id => id).ToArray();
        using (var group = new TransactionGroup(doc, "Controlled wall recognition inputs; always roll back"))
        {
            group.Start();
            try
            {
                var walls = new Dictionary<string, Element>();
                var gates = new Dictionary<string, string> { { "gate_zero", "0" }, { "gate_one", "1" }, { "gate_false", "false" } };
                using (var tx = new Transaction(doc, "Controlled unclassified wall source values"))
                {
                    StartTransaction(tx);
                    var type = (WallType)doc.GetElement(explicitWall.GetTypeId());
                    var sourceType = (WallType)type.Duplicate("PLATFORM-SOURCE-EVIDENCE");
                    var ordinaryType = (WallType)type.Duplicate("受控普通墙");
                    var level = (Level)doc.GetElement(new ElementId(ids["second_level"]));
                    var roles = gates.Keys.Concat(new[] { "height_only", "empty_sources", "ordinary_name" }).ToArray();
                    for (var i = 0; i < roles.Length; i++)
                        walls[roles[i]] = Wall.Create(doc, Line.CreateBound(new XYZ(Ft(60), Ft(30 + i * 4), level.Elevation), new XYZ(Ft(64), Ft(30 + i * 4), level.Elevation)),
                            roles[i] == "ordinary_name" ? ordinaryType.Id : sourceType.Id, level.Id, Ft(3.3), 0, false, false);
                    doc.Regenerate();
                    // Reuse the existing production category bindings without assigning a class.
                    foreach (var gate in gates) Text(walls[gate.Key], "是否为女儿墙", gate.Value);
                    Text(walls["height_only"], "高度", "2.4");
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Wall fixture transaction did not commit.");
                }
                Check("wall_fixture_inputs_are_unclassified_and_exact", walls.Values.All(w => string.IsNullOrEmpty(Raw(w, "类目")))
                    && gates.All(g => Raw(walls[g.Key], "是否为女儿墙") == g.Value && string.IsNullOrEmpty(Raw(walls[g.Key], "高度")))
                    && Raw(walls["height_only"], "高度") == "2.4"
                    && new[] { "height_only", "empty_sources", "ordinary_name" }.All(role => string.IsNullOrEmpty(Raw(walls[role], "是否为女儿墙")))
                    && new[] { "empty_sources", "ordinary_name" }.All(role => string.IsNullOrEmpty(Raw(walls[role], "高度")))
                    && walls["ordinary_name"].Name.Contains("墙"));
                var scan = Scan(doc, planning); Write("scan-wall-recognition.json", scan);
                var rows = Items(Get(scan, "Elements"));
                var preview = Static("Hifc.Revit.Management.CaseEdits", "Preview", doc, scan, null); Write("wall-recognition-preview.json", preview);
                var changes = Items(Get(preview, "Changes"));
                foreach (var gate in gates)
                {
                    var wall = walls[gate.Key]; var row = rows.Single(r => (int)Get(r, "Id") == wall.Id.IntegerValue);
                    var candidates = Items(Get(row, "Candidates"));
                    var fields = Items(Get(row, "Fields")).Where(f => (string)Get(f, "PropertySet") == "Pset_墙信息属性集").ToArray();
                    Check("wall_" + gate.Key + "_dedicated_source_is_recognized", candidates.Length == 1 && (string)Get(candidates[0], "Slot") == "墙"
                        && (bool)Get(candidates[0], "CanPrepare") && !(bool)Get(candidates[0], "RequiresReview")
                        && changes.Any(c => (int)Get(c, "Id") == wall.Id.IntegerValue && (string)Get(c, "Action") == "分类" && (string)Get(c, "ExpectedRaw") == "墙"));
                    var gateField = fields.Single(f => (string)Get(f, "Property") == "是否为女儿墙");
                    var heightField = fields.Single(f => (string)Get(f, "Property") == "高度");
                    Check("wall_" + gate.Key + "_value_and_calculation_gate_preserved", (string)Get(gateField, "Value") == gate.Value
                        && (bool)Get(gateField, "Valid") && string.IsNullOrEmpty((string)Get(heightField, "Value"))
                        && (bool)Get(heightField, "Acceptable") == (gate.Value != "1"));
                }
                foreach (var role in new[] { "height_only", "empty_sources", "ordinary_name" })
                {
                    var wall = walls[role]; var row = rows.Single(r => (int)Get(r, "Id") == wall.Id.IntegerValue);
                    Check("wall_" + role + "_does_not_trigger_business_mapping", Items(Get(row, "Candidates")).Length == 0
                        && Items(Get(row, "Fields")).Length == 0 && (bool)Get(row, "InventoryOnly")
                        && !changes.Any(c => (int)Get(c, "Id") == wall.Id.IntegerValue));
                }
                var explicitRow = rows.Single(r => (int)Get(r, "Id") == explicitWall.Id.IntegerValue);
                Check("wall_existing_explicit_classification_is_preserved", (string)Get(explicitRow, "Slot") == "墙"
                    && Items(Get(explicitRow, "Fields")).Any(f => (string)Get(f, "Property") == "是否为女儿墙" && (string)Get(f, "Value") == "0")
                    && !changes.Any(c => (int)Get(c, "Id") == explicitWall.Id.IntegerValue && (string)Get(c, "Action") == "分类"));
                Check("wall_recognition_preview_does_not_write_sources", walls.Values.All(w => string.IsNullOrEmpty(Raw(w, "类目")))
                    && gates.All(g => Raw(walls[g.Key], "是否为女儿墙") == g.Value) && Raw(walls["height_only"], "高度") == "2.4");
                Write("wall-recognition-inputs.json", walls.Select(w => new { role = w.Key, id = w.Value.Id.IntegerValue, name = w.Value.Name,
                    sources = sources.Select(name => new { name, value = Raw(w.Value, name) }).ToArray() }).ToArray());
            }
            finally { group.RollBack(); }
        }
        Check("wall_recognition_fixture_rolled_back", before == Json(sources.Select(name => new { name, value = Raw(explicitWall, name) }).ToArray())
            && allIds.SequenceEqual(new FilteredElementCollector(doc).WherePasses(allElements).ToElementIds().Select(id => id.IntegerValue).OrderBy(id => id)));
    }
    void VerifyWindowSourceConflict(Document doc, object planning, Dictionary<string, int> ids)
    {
        Phase("custom window elevation conflict negative case");
        var windows = new[] { doc.GetElement(new ElementId(ids["window"])), doc.GetElement(new ElementId(ids["unclassified_window"])) };
        var before = Json(Static("Hifc.Revit.RevitMappingService", "Capture", doc, windows));
        var allIds = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds().Select(id => id.IntegerValue).OrderBy(id => id).ToArray();
        var rejectedPath = Path.Combine(run, "window-source-conflict-must-not-export.ifc");
        using (var group = new TransactionGroup(doc, "Controlled same-name window source conflict; always roll back"))
        {
            group.Start();
            try
            {
                Guid guid;
                using (var tx = new Transaction(doc, "Controlled custom elevation source"))
                {
                    StartTransaction(tx);
                    guid = BindFixtureText(doc, "标高", Category.GetCategory(doc, BuiltInCategory.OST_Windows), false);
                    doc.Regenerate();
                    var custom = windows[0].get_Parameter(guid);
                    if (custom == null || custom.StorageType != StorageType.String || !custom.Set("999.0") || custom.AsString() != "999.0")
                        throw new InvalidOperationException("Could not establish the explicit nonempty conflict input.");
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Conflict fixture transaction did not commit.");
                }
                var candidates = windows[0].GetParameters("标高").ToArray();
                Check("custom_window_elevation_and_both_builtins_preserved", candidates.Any(p => p.IsShared && p.GUID == guid && p.AsString() == "999.0")
                    && candidates.Any(p => p.Id.IntegerValue == (int)BuiltInParameter.FAMILY_LEVEL_PARAM)
                    && candidates.Any(p => p.Id.IntegerValue == (int)BuiltInParameter.SCHEDULE_LEVEL_PARAM));
                WindowSourceEvidence(doc, ids, "window-sources-custom-conflict.json");
                var scan = Scan(doc, planning); Write("scan-window-source-conflict.json", scan);
                var row = Items(Get(scan, "Elements")).Single(r => (int)Get(r, "Id") == ids["window"]);
                var fields = Items(Get(row, "Fields")); Write("window-conflict-production-fields.json", fields);
                var elevation = fields.Where(f => (string)Get(f, "PropertySet") == "Pset_窗信息属性集" && (string)Get(f, "Property") == "标高").ToArray();
                Check("custom_window_elevation_conflict_is_not_valid", elevation.Length == 1 && !(bool)Get(elevation[0], "Valid")
                    && (string)Get(elevation[0], "State") == "类型待核验" && ((string)Get(elevation[0], "Detail")).Contains("歧义"));
                bool blocked = false; string error = null;
                try { Static("Hifc.Revit.NativeExportService", "ExportProject", doc, rejectedPath); }
                catch (Exception ex) { error = Unwrap(ex).ToString(); blocked = error.Contains("标高") && error.Contains("歧义"); }
                Write("window-source-conflict-export.json", new { blocked, error, ifc_exists = File.Exists(rejectedPath) });
                Check("custom_window_elevation_conflict_blocks_export", blocked && !File.Exists(rejectedPath));
            }
            finally { group.RollBack(); }
        }
        Check("custom_window_elevation_fixture_rolled_back", before == Json(Static("Hifc.Revit.RevitMappingService", "Capture", doc, windows))
            && allIds.SequenceEqual(new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds().Select(id => id.IntegerValue).OrderBy(id => id)));
        WindowSourceEvidence(doc, ids, "window-sources-after-conflict-rollback.json");
    }
    void VerifyExport(Document doc, string basename, Dictionary<string, int> ids)
    {
        var elements = ids.Values.Distinct().Select(id => doc.GetElement(new ElementId(id))).ToArray();
        var before = Json(Static("Hifc.Revit.RevitMappingService", "Capture", doc, elements));
        var allIds = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds().Select(id => id.IntegerValue).OrderBy(id => id).ToArray();
        var scope = Static("Hifc.Revit.ProjectExportScope", "Read", doc);
        var mapped = Items(Get(scope, "MappedElements")).Cast<Element>().Select(e => e.Id.IntegerValue).ToArray();
        Check(basename + "_auxiliary_level_in_native_scope", Items(Get(scope, "Elements")).Cast<Element>().Any(e => e.Id.IntegerValue == ids["auxiliary_level"]));
        Check(basename + "_auxiliary_level_has_no_business_mapping", !mapped.Contains(ids["auxiliary_level"]) && mapped.Contains(ids["first_level"]) && mapped.Contains(ids["second_level"]));
        Check(basename + "_unclassified_shared_window_is_not_mapped", !mapped.Contains(ids["unclassified_window"]));
        Static("Hifc.Revit.NativeExportService", "ExportProject", doc, Path.Combine(run, basename + ".ifc"));
        var receipt = new JavaScriptSerializer { MaxJsonLength = 128 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(run, basename + ".receipt.json")));
        Check(basename + "_native_transaction_rolled_back", (string)receipt["model_transaction"] == "RolledBack" && (bool)receipt["temporary_type_schedules_rolled_back"]);
        Check(basename + "_scoped_profile_recorded", (string)receipt["platform_profile_id"] == Profile);
        Check(basename + "_native_owner_routing_restored", (bool)receipt["type_routing_callback_restored"] && (bool)receipt["original_export_server_restored"]);
        var routes = Items(receipt["native_type_routing"]).Cast<Dictionary<string, object>>().Where(route => (string)route["property_set"] == "Pset_窗信息属性集").ToArray();
        Check(basename + "_window_routing_has_exact_instance_owner", routes.Length == 1 && (string)routes[0]["binding"] == "实例"
            && Items(routes[0]["allowed_instance_ids"]).Select(Convert.ToInt32).SequenceEqual(new[] { ids["window"] })
            && Items(routes[0]["allowed_type_ids"]).Length == 0);
        Check(basename + "_source_values_and_elements_restored", before == Json(Static("Hifc.Revit.RevitMappingService", "Capture", doc, elements)) && allIds.SequenceEqual(new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds().Select(id => id.IntegerValue).OrderBy(id => id)));
        Check(basename + "_old_type_250_preserved", Raw(doc.GetElement(doc.GetElement(new ElementId(ids["region"])).GetTypeId()), "构件类型") == "250");
        var issues = Items(receipt["mapping_issues"]).Cast<Dictionary<string, object>>().ToArray();
        Check(basename + "_missing_business_inputs_in_receipt", new[] { "绿地类型", "折算系数" }.All(name => issues.Any(issue => Convert.ToInt32(issue["element_id"]) == ids["incomplete_green"] && (string)issue["code"] == "HIFC_PLATFORM_INPUT_MISSING" && ((string)issue["detail"]).Contains(name))));
        Check(basename + "_missing_whole_project_metadata_in_receipt", issues.Any(issue => Convert.ToInt32(issue["element_id"]) == doc.ProjectInformation.Id.IntegerValue && (string)issue["code"] == "HIFC_PLATFORM_INPUT_MISSING"));
        Check(basename + "_disabled_parapet_calculation_has_no_height_gap", !issues.Any(issue => Convert.ToInt32(issue["element_id"]) == ids["disabled_wall"] && (string)issue["code"] == "HIFC_PLATFORM_INPUT_MISSING"));
        Check(basename + "_defaults_not_written", string.IsNullOrEmpty(Raw(doc.GetElement(new ElementId(ids["incomplete_green"])), "绿地类型")) && string.IsNullOrEmpty(Raw(doc.GetElement(new ElementId(ids["incomplete_green"])), "折算系数")));
        var writes = Items(receipt["materialization"]).Cast<Dictionary<string, object>>().ToArray();
        var expected = new Dictionary<string, double> { { "标高", 5.7 }, { "底高度", .9 }, { "高度", 1.5 }, { "宽度", 1.2 } };
        foreach (var pair in expected)
        {
            var write = writes.Single(w => Convert.ToInt32(w["element_id"]) == ids["window"] && (string)w["property_set"] == "Pset_窗信息属性集" && (string)w["property"] == pair.Key);
            Check(basename + "_window_metric_" + pair.Key, (string)write["binding"] == "实例" && Math.Abs(double.Parse((string)write["value"], CultureInfo.InvariantCulture) - pair.Value) < .000001);
        }
    }
    public void Execute(UIApplication ui)
    {
        Document doc = null;
        string source = null, sourceHash = null;
        try
        {
            Check("isolated_host_has_no_open_documents", !ui.Application.Documents.Cast<Document>().Any());
            var launch = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(run, "launch.json")));
            plugin = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Hifc.V1.Revit");
            core = Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(plugin.Location), "Hifc.Reference.Core.dll"));
            Check("expected_plugin_and_core_loaded", Hash(plugin.Location) == (string)launch["plugin_sha256"] && Hash(core.Location) == (string)launch["core_sha256"]);
            Check("matching_native_harness_version", plugin.GetName().Version == GetType().Assembly.GetName().Version);
            Write("loaded-assemblies.json", new[] { plugin, core, GetType().Assembly }.Select(a => new { name = a.GetName().Name, version = a.GetName().Version.ToString(), path = a.Location, sha256 = Hash(a.Location) }).ToArray());
            doc = ui.Application.NewProjectDocument((string)launch["template"]);
            var ids = new Dictionary<string, int>();
            using (var tx = new Transaction(doc, "Controlled platform fixture - explicit test inputs"))
            {
                StartTransaction(tx);
                var first = Level.Create(doc, Ft(1.2)); first.Name = "PLATFORM-F1";
                var second = Level.Create(doc, Ft(5.7)); second.Name = "PLATFORM-F2";
                var auxiliary = Level.Create(doc, Ft(9)); auxiliary.Name = "PLATFORM-AUXILIARY";
                foreach (var level in new[] { first, second, auxiliary }) level.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY)?.Set(1);
                ids["first_level"] = first.Id.IntegerValue; ids["second_level"] = second.Id.IntegerValue; ids["auxiliary_level"] = auxiliary.Id.IntegerValue;
                var baseType = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().First(t => !t.IsFoundationSlab);
                var legacyType = (FloorType)baseType.Duplicate("PLATFORM-LEGACY-250");
                var legalType = (FloorType)baseType.Duplicate("PLATFORM-GENERAL-SITE");
                BindFixtureText(doc, "构件类型", Category.GetCategory(doc, BuiltInCategory.OST_Floors), true); doc.Regenerate();
                Text(legacyType, "构件类型", "250"); Text(legalType, "构件类型", "风雨连廊");
                ids["region"] = Floor(doc, legacyType, first, 0, 8, 5).Id.IntegerValue;
                ids["canopy"] = Floor(doc, legacyType, first, 12, 3, 2).Id.IntegerValue;
                ids["general_site"] = Floor(doc, legalType, first, 20, 4, 3).Id.IntegerValue;
                ids["incomplete_green"] = Floor(doc, legacyType, first, 30, 4, 3).Id.IntegerValue;
                ids["unclassified_aux_floor"] = Floor(doc, legacyType, auxiliary, 40, 3, 2).Id.IntegerValue;
                var parking = Area(doc, first, 50, 8, 5); parking.Name = "Controlled indoor parking"; ids["indoor_parking"] = parking.Id.IntegerValue;
                Family family;
                if (!doc.LoadFamily((string)launch["window_family"], out family)) throw new InvalidOperationException("Could not load controlled window family.");
                var symbol = (FamilySymbol)doc.GetElement(family.GetFamilySymbolIds().First());
                symbol.get_Parameter(BuiltInParameter.WINDOW_HEIGHT).Set(Ft(1.5)); symbol.get_Parameter(BuiltInParameter.WINDOW_WIDTH).Set(Ft(1.2));
                if (!symbol.IsActive) symbol.Activate(); doc.Regenerate();
                var wallType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().First(t => t.Kind == WallKind.Basic);
                var wall = Wall.Create(doc, Line.CreateBound(new XYZ(0, Ft(12), second.Elevation), new XYZ(Ft(12), Ft(12), second.Elevation)), wallType.Id, second.Id, Ft(3.3), 0, false, false);
                ids["disabled_wall"] = wall.Id.IntegerValue;
                var window = doc.Create.NewFamilyInstance(new XYZ(Ft(3), Ft(12), second.Elevation + Ft(.9)), symbol, wall, second, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                var peer = doc.Create.NewFamilyInstance(new XYZ(Ft(8), Ft(12), second.Elevation + Ft(1.1)), symbol, wall, second, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                window.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM).Set(Ft(.9)); peer.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM).Set(Ft(1.1));
                ids["window"] = window.Id.IntegerValue; ids["unclassified_window"] = peer.Id.IntegerValue;
                tx.Commit();
            }
            source = Path.Combine(run, "models", "platform-source.rvt"); doc.SaveAs(source, new SaveAsOptions { OverwriteExistingFile = false }); doc.Close(false); doc = null; sourceHash = Hash(source);
            var copy = Path.Combine(run, "models", "platform-test.rvt"); File.Copy(source, copy); doc = ui.Application.OpenDocumentFile(copy);
            var planning = CoreJson("PlanningSettings", new { RuleProfileId = Profile, LevelsConfirmed = true, BuildingLevelUniqueIds = new[] { doc.GetElement(new ElementId(ids["first_level"])).UniqueId, doc.GetElement(new ElementId(ids["second_level"])).UniqueId } });
            PersistPlanning(doc, planning);
            WindowSourceEvidence(doc, ids, "window-sources-before-prepare.json");
            using (var tx = new Transaction(doc, "Controlled test classifications and input facts"))
            {
                StartTransaction(tx);
                Assign(doc, doc.GetElement(new ElementId(ids["region"])), "建筑区域", new Dictionary<string, string> { { "主功能类别", "学校" }, { "子功能类别", "教室" }, { "计算系数", "1" }, { "计容系数", "1" }, { "投影面积", "40" } });
                Assign(doc, doc.GetElement(new ElementId(ids["canopy"])), "雨篷", new Dictionary<string, string> { { "公建大类", "学校" }, { "公建小类", "教室" }, { "计算系数", "0.5" }, { "计容系数", "0" }, { "投影面积", "6" }, { "间距审查", "0" }, { "退让审查", "0" } });
                Assign(doc, doc.GetElement(new ElementId(ids["general_site"])), "总平构件", new Dictionary<string, string> { { "投影面积", "12" }, { "计算系数", "1" }, { "计容系数", "0" }, { "建筑密度", "0" } });
                Assign(doc, doc.GetElement(new ElementId(ids["incomplete_green"])), "绿地", new Dictionary<string, string> { { "投影面积", "12" } });
                Assign(doc, doc.GetElement(new ElementId(ids["indoor_parking"])), "室内停车场", new Dictionary<string, string> { { "投影面积", "40" }, { "公建大类", "其它设施" }, { "公建小类", "机动车库" }, { "停车类型", "标准车位" }, { "层数", "1" } });
                Assign(doc, doc.GetElement(new ElementId(ids["window"])), "窗", new Dictionary<string, string>());
                Assign(doc, doc.GetElement(new ElementId(ids["disabled_wall"])), "墙", new Dictionary<string, string> { { "是否为女儿墙", "0" } });
                foreach (var key in new[] { "first_level", "second_level", "auxiliary_level" })
                {
                    var level = doc.GetElement(new ElementId(ids[key]));
                    Assign(doc, level, "建筑楼层", new Dictionary<string, string> { { "楼层名称", level.Name }, { "建筑层高", key == "first_level" ? "4.5" : "3.3" }, { "建筑基底", key == "first_level" ? "1" : "0" } });
                }
                tx.Commit();
            }
            var windows = new[] { doc.GetElement(new ElementId(ids["window"])), doc.GetElement(new ElementId(ids["unclassified_window"])) };
            Phase("window source assertions");
            WindowSourceEvidence(doc, ids, "window-sources-after-prepare.json");
            // A localized label is not a unique Revit parameter identity. Keep
            // every built-in/shared candidate; production resolution is audited below.
            var familyLevel = windows[0].get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
            Check("window_level_is_an_actual_element_reference", familyLevel != null && familyLevel.HasValue
                && familyLevel.StorageType == StorageType.ElementId && familyLevel.AsElementId().IntegerValue == ids["second_level"]
                && windows[0].LevelId.IntegerValue == ids["second_level"]);
            Check("windows_share_one_type_but_only_one_is_classified", windows[0].GetTypeId() == windows[1].GetTypeId() && Raw(windows[0], "类目") == "窗" && string.IsNullOrEmpty(Raw(windows[1], "类目")));
            Phase("production scan");
            var scan = Scan(doc, planning); Write("scan-before-export.json", scan);
            var rows = Items(Get(scan, "Elements"));
            Check("active_profile_reaches_scan", (string)Get(Get(scan, "Planning"), "RuleProfileId") == Profile);
            var windowFields = Items(Get(rows.Single(r => (int)Get(r, "Id") == ids["window"]), "Fields"));
            Write("window-production-fields.json", windowFields);
            foreach (var pair in new Dictionary<string, double> { { "标高", 5.7 }, { "底高度", .9 }, { "高度", 1.5 }, { "宽度", 1.2 } })
            {
                var fields = windowFields.Where(f => (string)Get(f, "PropertySet") == "Pset_窗信息属性集" && (string)Get(f, "Property") == pair.Key).ToArray();
                Check("window_production_scan_metric_" + pair.Key, fields.Length == 1 && (string)Get(fields[0], "State") == "有效"
                    && Get(fields[0], "MetricValue") != null && Math.Abs(Convert.ToDouble(Get(fields[0], "MetricValue"), CultureInfo.InvariantCulture) - pair.Value) < .000001);
            }
            Check("auxiliary_level_inventory_has_no_business_fields", Items(Get(rows.Single(r => (int)Get(r, "Id") == ids["auxiliary_level"]), "Fields")).Length == 0);
            Check("indoor_parking_area_is_checked_as_indoor_parking", Items(Get(rows.Single(r => (int)Get(r, "Id") == ids["indoor_parking"]), "Fields")).Any(f => (string)Get(f, "PropertySet") == "Pset_室内停车场信息属性集" && (string)Get(f, "Property") == "公建小类"));
            Write("test-inputs.json", new { scope = "CONTROLLED_FIXTURE_INPUTS_NOT_CUSTOMER_FACTS", profile_id = Profile, ids, source, model = copy,
                note = "Explicit fixture dimensions and business selections exercise the adapter. Incomplete green fields intentionally remain empty; no server calculation acceptance is claimed.",
                expected_window_m = new Dictionary<string, double> { { "标高", 5.7 }, { "底高度", .9 }, { "高度", 1.5 }, { "宽度", 1.2 } },
                elements = ids.Select(pair => { var element = doc.GetElement(new ElementId(pair.Value)); return new { role = pair.Key, id = pair.Value, unique_id = element.UniqueId, name = element.Name,
                    native_global_id = element is Level ? ExporterIFCUtils.CreateAlternateGUID(element) : null, export_id = ExportUtils.GetExportId(doc, element.Id).ToString("N") }; }).ToArray() });
            VerifyWallRecognition(doc, planning, ids);
            VerifyWindowSourceConflict(doc, planning, ids);
            VerifyExport(doc, "platform", ids);
            Static("Hifc.Revit.Management.PlatformContext", "Set", doc, CoreJson("PlanningSettings", new { RuleProfileId = "" }));
            try
            {
                bool blocked = false;
                try { Static("Hifc.Revit.NativeExportService", "ExportProject", doc, Path.Combine(run, "legacy-partial-must-not-export.ifc")); }
                catch (Exception ex) { blocked = Unwrap(ex).Message.Contains("同类型"); }
                Check("legacy_type_window_still_requires_whole_group", blocked && !File.Exists(Path.Combine(run, "legacy-partial-must-not-export.ifc")));
            }
            finally { Static("Hifc.Revit.Management.PlatformContext", "Set", doc, planning); }
            doc.Save(); doc.Close(false); doc = null;
            // This isolated Revit process has only the controlled fixture. Drop
            // its document cache so reopening must load the persisted profile.
            var cache = (IList)plugin.GetType("Hifc.Revit.Management.PlatformContext", true).GetField("Current", F).GetValue(null); cache.Clear();
            doc = ui.Application.OpenDocumentFile(copy);
            Check("saved_profile_reloads_without_in_memory_cache", (string)Static("Hifc.Revit.Management.PlatformContext", "ProfileId", doc) == Profile);
            Check("classified_sources_survive_save_reopen", Raw(doc.GetElement(new ElementId(ids["window"])), "类目") == "窗" && Raw(doc.GetElement(new ElementId(ids["canopy"])), "类目") == "雨篷" && Raw(doc.GetElement(new ElementId(ids["indoor_parking"])), "室内停车场公建小类") == "机动车库");
            VerifyExport(doc, "platform-reopened", ids);
            Check("controlled_source_rvt_unchanged", Hash(source) == sourceHash);
            Write("platform-host-result.json", new { all_passed = true, checks, source, source_sha256 = sourceHash, model = copy, warnings = NativeTestWarnings.Observed.ToArray(),
                scope = "Real Revit controlled fixture services, native exports, rollback and reopen; IFC semantic audit is a separate required step; not platform acceptance." });
        }
        catch (Exception ex) { Write("platform-host-result.json", new { all_passed = false, phase, error = Unwrap(ex).ToString(), checks, source, source_sha256 = sourceHash }); }
        finally { if (doc?.IsValidObject == true) doc.Close(false); application.Idling += Exit; }
    }
}
