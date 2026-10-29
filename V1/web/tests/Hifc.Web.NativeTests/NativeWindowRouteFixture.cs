using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Hifc.Web.Revit;

internal static class NativeWindowRouteFixture
{
    // Isolates the native IFC executor. Business authorization of a partially
    // classified shared type remains a separate real-server regression.
    internal static void Run(UIApplication ui, string run)
    {
        Document document = null;
        try {
            var config = JsonWire.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(run, "window-route.json")));
            foreach (var name in new[] { "template", "window_family" }) if (!File.Exists(config[name])) throw new InvalidOperationException("Missing fixture input: " + name);
            var inputHashes = new Dictionary<string, string> { [config["template"]] = FileHash(config["template"]), [config["window_family"]] = FileHash(config["window_family"]) };
            foreach (var name in new[] { "template", "window_family" }) if (config.TryGetValue(name + "_sha256", out var expected)) Require(string.Equals(inputHashes[config[name]], expected, StringComparison.OrdinalIgnoreCase), "Fixture input hash mismatch: " + name);
            var directory = Path.Combine(run, "window-routes"); Directory.CreateDirectory(directory);
            document = ui.Application.NewProjectDocument(config["template"]);
            FamilyInstance first, second, peer; FamilySymbol typeA, typeB;
            using (var transaction = new Transaction(document, "Native multi-window routing fixture")) {
                transaction.Start(); var level = Level.Create(document, 0); level.Name = "WEB-ROUTE-LEVEL";
                if (!document.LoadFamily(config["window_family"], out var family)) throw new InvalidOperationException("The controlled window family could not be loaded.");
                var original = (FamilySymbol)document.GetElement(family.GetFamilySymbolIds().First());
                typeA = (FamilySymbol)original.Duplicate("WEB-ROUTE-TYPE-A"); typeB = (FamilySymbol)original.Duplicate("WEB-ROUTE-TYPE-B");
                SetSize(typeA, 1.2, 1.5); SetSize(typeB, 1.8, 1.7); typeA.Activate(); typeB.Activate(); document.Regenerate();
                var wallType = new FilteredElementCollector(document).OfClass(typeof(WallType)).Cast<WallType>().First(t => t.Kind == WallKind.Basic);
                var wall = Wall.Create(document, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(18 / .3048, 0, 0)), wallType.Id, level.Id, 4 / .3048, 0, false, false);
                first = Place(document, typeA, wall, level, 3); peer = Place(document, typeA, wall, level, 8); second = Place(document, typeB, wall, level, 13);
                Ensure(document, new[] { first, second, peer }, "类目", Guid.NewGuid().ToString("D"), "instance"); document.Regenerate();
                first.LookupParameter("类目").Set("窗"); second.LookupParameter("类目").Set("窗"); peer.LookupParameter("类目").Set("");
                document.ProjectInformation.BuildingName = "Web 窗路由验收"; transaction.Commit();
            }
            document.SaveAs(Path.Combine(directory, "window-instance-source.rvt"), new SaveAsOptions { OverwriteExistingFile = false });
            using (var rsa = RSA.Create()) {
                rsa.KeySize = 2048; var keyParts = rsa.ExportParameters(false); var key = new PublicSigningKey { Modulus = Convert.ToBase64String(keyParts.Modulus), Exponent = Convert.ToBase64String(keyParts.Exponent) };
                var instance = Export(document, new Element[] { first, second }, "instance", "Pset_窗信息属性集", new[] { "INSTANCE_A", "INSTANCE_B" }, directory, "instance-exact.ifc", rsa, key);
                Require(string.IsNullOrEmpty(peer.LookupParameter("类目").AsString()), "Instance routing changed the unclassified peer.");
                using (var transaction = new Transaction(document, "Confirm peer for separate type-scope fixture")) { transaction.Start(); peer.LookupParameter("类目").Set("窗"); transaction.Commit(); }
                document.SaveAs(Path.Combine(directory, "window-type-source.rvt"), new SaveAsOptions { OverwriteExistingFile = false });
                var typed = Export(document, new Element[] { typeA, typeB }, "type", "Pset_HifcWebTypeRouteProbe", new[] { "TYPE_A", "TYPE_B" }, directory, "type-exact.ifc", rsa, key);
                foreach (var input in inputHashes) Require(FileHash(input.Key) == input.Value, "Fixture input file was changed.");
                File.WriteAllText(Path.Combine(run, "window-route-results.json"), JsonWire.Serialize(new {
                    nativeExportCompleted = true, independentIfcAuditRequired = true,
                    scope = "native exact-owner executor only; server shared-type authorization is verified separately",
                    first = new { elementId = first.Id.IntegerValue, uniqueId = first.UniqueId, typeId = typeA.Id.IntegerValue },
                    second = new { elementId = second.Id.IntegerValue, uniqueId = second.UniqueId, typeId = typeB.Id.IntegerValue },
                    peer = new { elementId = peer.Id.IntegerValue, uniqueId = peer.UniqueId, typeId = typeA.Id.IntegerValue },
                    instance = new { path = instance.IfcPath, sha256 = instance.IfcHash, instance.SourceRestored, expectedSet = "Pset_窗信息属性集", expectedMarkers = new[] { "INSTANCE_A", "INSTANCE_B" }, unclassifiedPeerMustHaveNoSet = true },
                    typed = new { path = typed.IfcPath, sha256 = typed.IfcHash, typed.SourceRestored, expectedSet = "Pset_HifcWebTypeRouteProbe", expectedMarkers = new[] { "TYPE_A", "TYPE_B" }, allInstancesClassifiedBeforeExport = true }, inputHashes
                }));
            }
        } catch (Exception ex) { File.WriteAllText(Path.Combine(run, "window-route-error.json"), JsonWire.Serialize(new { error = ex.ToString() })); }
        finally { if (document != null && document.IsValidObject) document.Close(false); if (ui.Application.Documents.Size == 0) ui.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit)); }
    }
    private static ExportReceipt Export(Document document, Element[] owners, string origin, string set, string[] markers, string directory, string fileName, RSA rsa, PublicSigningKey key)
    {
        var prefix = "HIFC_WEB_ROUTE_" + origin; var markerGuid = Guid.NewGuid().ToString("D"); var widthGuid = Guid.NewGuid().ToString("D");
        var targets = owners.SelectMany(owner => new[] { prefix + "_Marker", prefix + "_Width" }.Select(source => new CaptureTarget { OwnerId = owner.Id.IntegerValue, OwnerUniqueId = owner.UniqueId, Source = source, Origin = origin })).ToArray();
        var values = NativeInventory.ReadValues(document, targets); var definitions = NativeInventory.ReadDefinitions(document, targets.Select(t => t.Source).Distinct().ToArray());
        var data = NativeProjectData.Read(document); var settings = new ModelSettings(); var records = NativeInventory.Read(document);
        var context = new RequestContext { RuleVersion = "native-window-route-fixture", RequestId = Guid.NewGuid().ToString("N"), ScanId = Guid.NewGuid().ToString("N"), DocumentSessionId = "native-window-route",
            SnapshotHash = Canonical.InventoryHash(records), ConfigurationHash = Canonical.ConfigurationHash(settings, data) };
        var basis = new CheckResult { Context = context, Complete = true, CaptureHash = Canonical.CaptureHash(values, definitions), Settings = settings, ProjectData = data, Definitions = definitions, Elements = records.Select(r => new ElementResult { Element = r }).ToArray() };
        var route = new NativeExportRoute { PropertySet = set, Entity = "IfcWindow", Origin = origin, ExactOwnerFilter = true, OwnerUniqueIds = owners.Select(e => e.UniqueId).ToArray(),
            Properties = new[] { new NativeExportProperty { Property = "Marker", BridgeName = prefix + "_Marker", BridgeGuid = markerGuid, ExportType = "Text" }, new NativeExportProperty { Property = "ProbeWidth", BridgeName = prefix + "_Width", BridgeGuid = widthGuid, ExportType = "Real" } } };
        var plan = new ExportPlan { Context = context, Id = Guid.NewGuid().ToString("N"), CaptureHash = basis.CaptureHash, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5), FileName = fileName,
            ExpectedDataHash = Canonical.ProjectDataHash(data), MappingText = "# Native exact-owner fixture\r\n", Routes = new[] { route },
            TemporaryChanges = targets.Select(target => {
                var original = values.Single(v => v.OwnerUniqueId == target.OwnerUniqueId && v.Source == target.Source); var index = Array.FindIndex(owners, e => e.UniqueId == target.OwnerUniqueId); var marker = target.Source.EndsWith("_Marker", StringComparison.Ordinal);
                return new WriteChange { OwnerId = target.OwnerId, OwnerUniqueId = target.OwnerUniqueId, Source = target.Source, SourceGuid = marker ? markerGuid : widthGuid, Origin = origin, DefinitionType = "Text", Action = "Create",
                    ExpectedSignature = Canonical.ValuesHash(new[] { original }), Value = marker ? markers[index] : (index == 0 ? 1.2 : 1.8).ToString("R", CultureInfo.InvariantCulture) };
            }).ToArray() };
        plan.Hash = Canonical.ExportHash(plan); plan.Signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(plan.Hash), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return NativeExportExecutor.Execute(document, new NativeExportInput { DocumentSessionId = context.DocumentSessionId, OutputDirectory = directory, Basis = basis, Settings = settings, Plan = plan,
            Capture = new CapturePlan { Context = context, InventoryComplete = true, Targets = targets, DefinitionSources = targets.Select(t => t.Source).Distinct().ToArray() } }, key, DateTime.UtcNow);
    }
    private static FamilyInstance Place(Document document, FamilySymbol type, Wall wall, Level level, double x)
    {
        var instance = document.Create.NewFamilyInstance(new XYZ(x / .3048, 0, .9 / .3048), type, wall, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
        instance.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM).Set(.9 / .3048); return instance;
    }
    private static void SetSize(FamilySymbol type, double width, double height)
    {
        var widthParameter = type.get_Parameter(BuiltInParameter.WINDOW_WIDTH); var heightParameter = type.get_Parameter(BuiltInParameter.WINDOW_HEIGHT);
        Require(widthParameter != null && heightParameter != null && !widthParameter.IsReadOnly && !heightParameter.IsReadOnly, "Fixture family must expose writable native window width and height.");
        widthParameter.Set(width / .3048); heightParameter.Set(height / .3048);
        Require(Math.Abs(widthParameter.AsDouble() * .3048 - width) < 1e-8 && Math.Abs(heightParameter.AsDouble() * .3048 - height) < 1e-8, "Window dimension fixture readback failed.");
    }
    private static void Ensure(Document document, Element[] owners, string source, string guid, string origin)
    {
        var changes = owners.Select(e => new WriteChange { OwnerId = e.Id.IntegerValue, OwnerUniqueId = e.UniqueId, Source = source, SourceGuid = guid, Origin = origin, DefinitionType = "Text" }).ToArray();
        typeof(NativeWriter).GetMethod("EnsureDefinition", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { document, changes, null });
    }
    private static string FileHash(string path) { using (var input = File.OpenRead(path)) using (var hash = SHA256.Create()) return string.Concat(hash.ComputeHash(input).Select(b => b.ToString("x2"))); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
