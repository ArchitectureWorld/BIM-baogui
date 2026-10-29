using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Hifc.Web.Revit;

internal static class NativeUnitFullFlow
{
    private sealed class Fixture { public string Path, Level, Top, First, Second; }
    internal static async Task RunAsync(Func<Func<UIApplication, object>, Task<object>> native, Uri server, string run, Action<string> log)
    {
        var fixture = (Fixture)await native(ui => Create(ui, run));
        var access = (NativeModelAccess)await native(ui => new NativeModelAccess(ui));
        ModelSettings committedSettings = null;
        using (var session = new WebModelSession(access, server, Path.Combine(run, "unit-flow-state")))
        {
            var phone = "139" + (Math.Abs((long)Guid.NewGuid().GetHashCode()) % 90000000 + 10000000);
            await session.ConnectAsync(phone, "单位链路验收", true); session.SelectModelKind("单体建筑"); await session.ScanAsync();
            var profile = session.Result.RuleProfiles.FirstOrDefault(p => !string.IsNullOrEmpty(p.Id)) ?? throw new Exception("Server did not advertise the platform rule profile.");
            session.SetSettings(new ModelSettings { RuleProfileId = profile.Id, LevelsConfirmed = true, BuildingLevelUniqueIds = new[] { fixture.Level }, TopBoundaryLevelUniqueId = fixture.Top,
                Underground = new UndergroundSelection { Confirmed = true, GroundElevationM = 0, LevelUniqueIds = new string[0], BaseLevelUniqueId = fixture.Level } });
            session.Assign(fixture.First, "建筑区域"); session.Assign(fixture.Second, "建筑区域"); await session.ScanAsync(); session.IncludeSuggestions = false;
            var first = Area(session.Result, fixture.First); var second = Area(session.Result, fixture.Second);
            var length = session.Result.Fields.FirstOrDefault(f => f.Editable && f.InputUnit == "m" && f.Property == "建筑高度")
                ?? session.Result.Fields.FirstOrDefault(f => f.Editable && f.InputUnit == "m" && f.ElementUniqueId == fixture.Level)
                ?? throw new Exception("No advertised canonical length field is available.");
            await session.PreviewAsync(); var classifications = session.Plan.Changes.Where(c => c.Source == "类目").ToArray();
            await native(ui => {
                var doc = ui.ActiveUIDocument.Document; using (var tx = new Transaction(doc, "Prepare typed native unit fixtures")) {
                    tx.Start(); Ensure(doc, classifications); doc.Regenerate(); foreach (var change in classifications) doc.GetElement(change.OwnerUniqueId).LookupParameter(change.Source).Set(change.Value);
                    Install(doc, first, "Area", 100, true); Install(doc, second, "Area", 100); Install(doc, length, "Length", 10, true);
                    var units = doc.GetUnits(); units.SetFormatOptions(UnitType.UT_Length, new FormatOptions(DisplayUnitType.DUT_DECIMAL_FEET)); units.SetFormatOptions(UnitType.UT_Area, new FormatOptions(DisplayUnitType.DUT_SQUARE_FEET)); doc.SetUnits(units); tx.Commit();
                } return true;
            });
            await session.ScanAsync(); first = session.Result.Fields.Single(f => f.Key == first.Key); length = session.Result.Fields.Single(f => f.Key == length.Key);
            Require(first.DefinitionType == "Area" && length.DefinitionType == "Length", "Fixture fields are not real native Area and Length parameters.");
            var beforeArea = (await access.ValuesAsync(new[] { Target(first) })).Single(); var beforeLength = (await access.ValuesAsync(new[] { Target(length) })).Single();
            Require(beforeArea.RawValue == "100" && beforeLength.RawValue == "10", "The numeric-collision fixture must start at exactly 100 ft² and 10 ft.");
            // Bare input is explicitly labelled m²/m by FieldResult.InputUnit.
            // Keep exact numeric string equality with the raw ft²/ft baselines.
            session.SetDraft(first, "100"); session.SetDraft(length, "10");
            // This is the production pipeline: PreviewAsync awaits
            // NativeModelAccess.NormalizeDraftsAsync, then the real HTTP server plan.
            await session.PreviewAsync(); CheckPlan(session.Plan, first, "100", "m²"); CheckPlan(session.Plan, length, "10", "m");
            await session.WriteAsync(); Require(session.Workspace.LastReceipt?.ReadbackPassed == true && session.Workspace.LastReceipt.RecheckCompleted, "Real write/recheck did not finish.");
            await AssertMetric(access, first, 100); await AssertMetric(access, length, 10);
            Require(!session.Workspace.SettingsDirty && session.Workspace.LastModelSettingsHash != Canonical.Hash(""), "Writing attributes did not commit the signed Web settings record in the same transaction.");
            log("PASS numeric collisions: native 100 ft² -> typed 100 m² and 10 ft -> typed 10 m through production normalization/server/native writer");
            first = session.Result.Fields.Single(f => f.Key == first.Key); length = session.Result.Fields.Single(f => f.Key == length.Key);
            session.SetDraft(first, "100 m²"); session.SetDraft(length, "3250 mm"); await session.PreviewAsync();
            Require(!session.Plan.Changes.Any(c => c.OwnerUniqueId == first.OwnerUniqueId && c.Source == first.Source), "Explicit 100 m² should be a physical no-op after the collision write, not an internal-unit scaled area change.");
            CheckPlan(session.Plan, length, "3.25", "m"); await session.WriteAsync(); await AssertMetric(access, first, 100); await AssertMetric(access, length, 3.25);
            session.ResolveDraft(session.Result.Fields.Single(f => f.Key == first.Key), false);

            var floor = session.Result.Fields.Single(f => f.ElementUniqueId == fixture.Level && f.Property == "楼层面积" && f.InputUnit == "m²");
            await native(ui => { var doc = ui.ActiveUIDocument.Document; using (var tx = new Transaction(doc, "Prepare native floor total")) { tx.Start(); Install(doc, floor, "Area", 200); tx.Commit(); } return true; });
            await session.ScanAsync(); floor = session.Result.Fields.Single(f => f.Key == floor.Key); length = session.Result.Fields.Single(f => f.Key == length.Key);
            session.SetDraft(floor, "250 m²"); session.SetDraft(length, "3.5"); await session.PreviewAsync(); CheckPlan(session.Plan, floor, "250", "m²"); CheckPlan(session.Plan, length, "3.5", "m");
            var regionChanges = session.Plan.Changes.Where(c => c.Source == first.Source && (c.OwnerUniqueId == fixture.First || c.OwnerUniqueId == fixture.Second)).ToArray();
            Require(regionChanges.Length == 2 && regionChanges.All(c => c.Unit == "m²" && Math.Abs(double.Parse(c.Value, System.Globalization.CultureInfo.InvariantCulture) - 125) < 1e-6), "Floor allocation was not performed once in canonical square metres.");
            await session.WriteAsync(); await AssertMetric(access, floor, 250); await AssertMetric(access, first, 125); await AssertMetric(access, second, 125); await AssertMetric(access, length, 3.5); await session.SaveModelAsync();
            committedSettings = JsonWire.Deserialize<ModelSettings>(JsonWire.Serialize(session.Workspace.Settings));
            log("PASS explicit 250 m² and bare 3.5 m under feet -> real server floor allocation -> native region 125 + 125 and floor 250 m²");
            File.WriteAllText(Path.Combine(run, "unit-roundtrip-results.json"), JsonWire.Serialize(new { passed = true, fixture.Path, projectLengthUnit = "feet", projectAreaUnit = "square feet", collisionBeforeRawAreaFt2 = 100, collisionAfterAreaM2 = 100, collisionBeforeRawLengthFt = 10, collisionAfterLengthM = 10, explicitLengthM = 3.25, bareLengthM = 3.5, floorAreaM2 = 250, allocatedRegionAreaM2 = new[] { 125, 125 }, usedProductionNormalizeDraftsAsync = true, server = server.AbsoluteUri }));
        }
        await native(ui => {
            var current = ui.ActiveUIDocument.Document;
            var other = JsonWire.Deserialize<string[]>(File.ReadAllText(Path.Combine(run, "models.json"))).First(p => !string.Equals(p, fixture.Path, StringComparison.OrdinalIgnoreCase));
            ui.OpenAndActivateDocument(other); current.Close(false); ui.OpenAndActivateDocument(fixture.Path); return true;
        });
        var reopenedAccess = (NativeModelAccess)await native(ui => new NativeModelAccess(ui));
        using (var fresh = new WebModelSession(reopenedAccess, server, Path.Combine(run, "new-computer-settings-state-" + Guid.NewGuid().ToString("N")))) {
            await fresh.RestoreModelSettingsAsync();
            Require(!fresh.Workspace.SettingsDirty && NativeSettingsState.Same(fresh.Workspace.Settings, committedSettings), "A new Web state directory did not recover settings after saving and reopening the RVT.");
            File.WriteAllText(Path.Combine(run, "settings-reopen-results.json"), JsonWire.Serialize(new { passed = true, fixture.Path, fresh.Workspace.LastModelSettingsHash, recovered = fresh.Workspace.Settings, noLocalProfileRead = true }));
        }
        log("PASS signed Web model settings survive RVT save/reopen and restore into a fresh Web state directory before login");
    }
    private static FieldResult Area(CheckResult result, string id) => result.Fields.Single(f => f.ElementUniqueId == id && f.PropertySet == "Pset_建筑区域信息属性集" && f.Property == "投影面积");
    private static void CheckPlan(EditPlan plan, FieldResult field, string value, string unit)
    {
        var change = plan.Changes.Single(c => c.OwnerUniqueId == field.OwnerUniqueId && c.Source == field.Source);
        Require(change.Unit == unit && Math.Abs(double.Parse(change.Value, System.Globalization.CultureInfo.InvariantCulture) - double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)) < 1e-9, "Server plan contains a scaled or mislabeled unit value: " + field.Property);
    }
    private static async Task AssertMetric(NativeModelAccess access, FieldResult field, double expected)
    {
        var value = (await access.ValuesAsync(new[] { Target(field) })).Single();
        Require(value.StorageType == "Double" && value.MetricValue.HasValue && Math.Abs(value.MetricValue.Value - expected) < 1e-6, "Actual native metric value differs for " + field.Property + ": " + value.RawValue);
    }
    private static CaptureTarget Target(FieldResult field) => new CaptureTarget { OwnerId = field.OwnerId, OwnerUniqueId = field.OwnerUniqueId, Source = field.Source, Origin = field.Binding == "类型" ? "type" : "instance" };
    private static void Install(Document document, FieldResult field, string type, double canonical, bool rawInternal = false)
    {
        Ensure(document, new[] { new WriteChange { OwnerId = field.OwnerId, OwnerUniqueId = field.OwnerUniqueId, Source = field.Source, SourceGuid = field.SourceGuid, Origin = field.Binding == "类型" ? "type" : "instance", DefinitionType = type } }); document.Regenerate();
        var parameter = document.GetElement(field.OwnerUniqueId).GetParameters(field.Source).Single();
        if (!parameter.Set(rawInternal ? canonical : UnitUtils.ConvertToInternalUnits(canonical, type == "Area" ? DisplayUnitType.DUT_SQUARE_METERS : DisplayUnitType.DUT_METERS))) throw new Exception("Cannot initialize typed fixture.");
    }
    private static void Ensure(Document document, WriteChange[] changes)
    {
        foreach (var group in changes.GroupBy(c => c.Source)) typeof(NativeWriter).GetMethod("EnsureDefinition", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { document, group.ToArray(), null });
    }
    private static Fixture Create(UIApplication ui, string run)
    {
        var source = ui.ActiveUIDocument?.Document; var document = ui.Application.NewProjectDocument(UnitSystem.Metric); var fixture = new Fixture { Path = Path.Combine(run, "models", "native-unit-roundtrip-" + Guid.NewGuid().ToString("N") + ".rvt") };
        try {
            using (var transaction = new Transaction(document, "Native unit roundtrip geometry")) {
                transaction.Start();
                var type = new FilteredElementCollector(document).OfClass(typeof(FloorType)).Cast<FloorType>().FirstOrDefault();
                if (type == null) {
                    var sourceType = source == null ? null : new FilteredElementCollector(source).OfClass(typeof(FloorType)).Cast<FloorType>().FirstOrDefault();
                    if (sourceType == null) throw new Exception("Unit fixture needs one native FloorType from a verified model copy.");
                    var options = new CopyPasteOptions(); options.SetDuplicateTypeNamesHandler(new ReuseTypes());
                    ElementTransformUtils.CopyElements(source, new[] { sourceType.Id }, document, Transform.Identity, options);
                    type = new FilteredElementCollector(document).OfClass(typeof(FloorType)).Cast<FloorType>().First();
                }
                var level = Level.Create(document, 0); level.Name = "HIFC 单位验收首层"; fixture.Level = level.UniqueId;
                var top = Level.Create(document, 3.2 / .3048); top.Name = "HIFC 单位验收上边界"; fixture.Top = top.UniqueId;
                fixture.First = Floor(document, type, level, 0).UniqueId; fixture.Second = Floor(document, type, level, 20 / .3048).UniqueId;
                document.ProjectInformation.BuildingName = "单位链路验收楼"; transaction.Commit();
            }
            document.SaveAs(fixture.Path, new SaveAsOptions { OverwriteExistingFile = false });
        } finally { document.Close(false); }
        ui.OpenAndActivateDocument(fixture.Path); return fixture;
    }
    private static Floor Floor(Document document, FloorType type, Level level, double x)
    {
        var side = 10 / .3048; var points = new[] { new XYZ(x, 0, 0), new XYZ(x + side, 0, 0), new XYZ(x + side, side, 0), new XYZ(x, side, 0) }; var loop = new CurveArray();
        for (int i = 0; i < 4; i++) loop.Append(Line.CreateBound(points[i], points[(i + 1) % 4]));
        return document.Create.NewFloor(loop, type, level, false);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class ReuseTypes : IDuplicateTypeNamesHandler { public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args) => DuplicateTypeAction.UseDestinationTypes; }
}
