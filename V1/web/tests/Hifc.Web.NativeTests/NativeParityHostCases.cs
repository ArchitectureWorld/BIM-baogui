using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Hifc.Web.Revit;

internal static class NativeParityHostCases
{
    internal static void Run(Autodesk.Revit.ApplicationServices.Application application, Action<string, Action> test)
    {
        test("protocol 2 native YesNo migration preserves every bound owner including unselected false", () => {
            var document = application.NewProjectDocument(UnitSystem.Metric);
            try {
                using (var transaction = new Transaction(document, "Web parity migration fixtures")) { transaction.Start(); Level.Create(document, 0); Level.Create(document, 10); transaction.Commit(); }
                var owners = new FilteredElementCollector(document).OfClass(typeof(Level)).ToArray();
                var source = "HIFC_Web_YesNo_" + Guid.NewGuid().ToString("N").Substring(0, 8); var guid = Guid.NewGuid().ToString("D"); var yesNoGuid = Guid.NewGuid().ToString("D");
                var targets = owners.Select(e => new CaptureTarget { ElementUniqueId = e.UniqueId, OwnerId = e.Id.IntegerValue, OwnerUniqueId = e.UniqueId, Source = source, Origin = "instance" }).ToArray();
                using (var rsa = RSA.Create()) {
                    rsa.KeySize = 2048; var key = Key(rsa);
                    var initial = Build(document, targets, rsa, values => targets.Select((t, i) => Change(t, values, guid, "Text", i == 0 ? "是" : "否")).ToArray());
                    NativeWriter.Apply(document, initial, key, DateTime.UtcNow);
                    var definition = NativeInventory.ReadDefinitions(document, new[] { source }).Single();
                    ProbeDeletion(document, definition, targets);
                    var operation = new NativeDefinitionOperation { Source = source, Guid = yesNoGuid, DefinitionType = "YesNo", Origin = "instance", CategoryIds = definition.CategoryIds,
                        ExpectedBindingHash = Canonical.DefinitionSetHash(source, new[] { definition }), ReplaceParameterId = definition.ParameterId,
                        MigrationValues = definition.Values.Select(v => new NativeMigrationValue { OwnerId = v.OwnerId, OwnerUniqueId = v.OwnerUniqueId, HasValue = v.HasValue,
                            ExpectedSignature = Canonical.ValuesHash(new[] { v }), Value = v.RawValue == "是" ? "1" : "0" }).ToArray() };
                    var input = Build(document, targets, rsa, values => new[] { Change(targets[0], values, yesNoGuid, "YesNo", "1") }, new NativeOperations { Definitions = new[] { operation } });
                    NativeWriter.Apply(document, input, key, DateTime.UtcNow);
                    var actual = NativeInventory.ReadValues(document, targets);
                    Require(actual.All(v => v.DefinitionType == "YesNo" && v.StorageType == "Integer"), "Migration did not create real Revit YesNo definitions.");
                    Require(SharedParameterElement.Lookup(document, new Guid(guid)) == null && actual.All(v => v.SharedGuid == yesNoGuid), "Migration did not replace the old Text GUID with the new YesNo GUID.");
                    Require(actual.Single(v => v.OwnerUniqueId == targets[0].OwnerUniqueId).RawValue == "1" && actual.Where(v => v.OwnerUniqueId != targets[0].OwnerUniqueId).All(v => v.RawValue == "0"), "Migration overwrote unselected false values.");
                }
            } finally { document.Close(false); }
        });
        test("native model archive and parameter creation rollback together when an attribute write fails", () => {
            var document = application.NewProjectDocument(UnitSystem.Metric);
            try {
                Level level; using (var tx = new Transaction(document, "Archive fixture")) { tx.Start(); level = Level.Create(document, 0); tx.Commit(); }
                var source = "HIFC_Web_Archive_" + Guid.NewGuid().ToString("N").Substring(0, 8); var guid = Guid.NewGuid().ToString("D");
                var target = new CaptureTarget { OwnerId = level.Id.IntegerValue, OwnerUniqueId = level.UniqueId, ElementUniqueId = level.UniqueId, Source = source, Origin = "instance" };
                using (var rsa = RSA.Create()) {
                    rsa.KeySize = 2048; var before = NativeProjectData.Read(document); var archive = before.Single(d => d.SchemaId == "fbdf2a40-b13c-4a9a-aeda-525e010a4785");
                    var operations = new NativeOperations { DataWrites = new[] { new NativeDataWrite { SchemaId = archive.SchemaId, SchemaName = archive.SchemaName, ExpectedHash = archive.Hash, Json = "[]", Hash = Canonical.Hash("[]") } } };
                    // Invalid integer is detected inside the transaction after parameter creation.
                    var input = Build(document, new[] { target }, rsa, values => new[] { Change(target, values, guid, "YesNo", "2") }, operations);
                    bool rejected = false; try { NativeWriter.Apply(document, input, Key(rsa), DateTime.UtcNow); } catch (InvalidOperationException) { rejected = true; }
                    Require(rejected && !level.GetParameters(source).Any(), "Failed write left a new definition behind.");
                    Require(Canonical.ProjectDataHash(NativeProjectData.Read(document)) == Canonical.ProjectDataHash(before), "Failed attribute write changed project data.");
                    var valid = Build(document, new[] { target }, rsa, values => new[] { Change(target, values, guid, "YesNo", "0") }, operations);
                    var receipt = NativeWriter.Apply(document, valid, Key(rsa), DateTime.UtcNow);
                    Require(receipt.ReadbackPassed && NativeProjectData.Read(document).Single(d => d.SchemaId == archive.SchemaId).Json == "[]" && level.LookupParameter(source).AsInteger() == 0, "Valid zero and archive were not committed together.");
                }
            } finally { document.Close(false); }
        });
        test("Boolean alias copy rejects an omitted peer and preserves the old alias for every owner", () => {
            var document = application.NewProjectDocument(UnitSystem.Metric);
            try {
                using (var tx = new Transaction(document, "Alias copy fixtures")) { tx.Start(); Level.Create(document, 0); Level.Create(document, 10); tx.Commit(); }
                var owners = new FilteredElementCollector(document).OfClass(typeof(Level)).ToArray();
                var oldName = "HIFC_Web_Legacy_" + Guid.NewGuid().ToString("N").Substring(0, 8); var newName = oldName + "_YesNo";
                var oldGuid = Guid.NewGuid().ToString("D"); var newGuid = Guid.NewGuid().ToString("D");
                var oldTargets = owners.Select(e => new CaptureTarget { OwnerId = e.Id.IntegerValue, OwnerUniqueId = e.UniqueId, ElementUniqueId = e.UniqueId, Source = oldName, Origin = "instance" }).ToArray();
                var newTargets = owners.Select(e => new CaptureTarget { OwnerId = e.Id.IntegerValue, OwnerUniqueId = e.UniqueId, ElementUniqueId = e.UniqueId, Source = newName, Origin = "instance" }).ToArray();
                using (var rsa = RSA.Create()) {
                    rsa.KeySize = 2048; var key = Key(rsa);
                    NativeWriter.Apply(document, Build(document, oldTargets, rsa, values => oldTargets.Select((t, i) => Change(t, values, oldGuid, "Text", i == 0 ? "1" : "0")).ToArray()), key, DateTime.UtcNow);
                    var definition = NativeInventory.ReadDefinitions(document, new[] { oldName }).Single();
                    var migrations = definition.Values.Select(v => new NativeMigrationValue { Source = oldName, OwnerId = v.OwnerId, OwnerUniqueId = v.OwnerUniqueId, HasValue = true, Value = v.RawValue, ExpectedSignature = Canonical.ValuesHash(new[] { v }) }).ToArray();
                    var operation = new NativeDefinitionOperation { Source = newName, Guid = newGuid, DefinitionType = "YesNo", Origin = "instance", CategoryIds = definition.CategoryIds,
                        ExpectedBindingHash = Canonical.DefinitionSetHash(newName, new NativeDefinitionSnapshot[0]), MigrationValues = migrations.Take(1).ToArray() };
                    var combined = oldTargets.Concat(newTargets).ToArray();
                    var partial = Build(document, combined, rsa, values => new[] { Change(newTargets[0], values, newGuid, "YesNo", "1") }, new NativeOperations { Definitions = new[] { operation } });
                    bool rejected = false; try { NativeWriter.Apply(document, partial, key, DateTime.UtcNow); } catch (InvalidOperationException) { rejected = true; }
                    Require(rejected && owners.All(e => e.GetParameters(newName).Count == 0), "Partial alias migration created default values for an unselected peer.");
                    operation.MigrationValues = migrations;
                    NativeWriter.Apply(document, Build(document, combined, rsa, values => new[] { Change(newTargets[0], values, newGuid, "YesNo", "1") }, new NativeOperations { Definitions = new[] { operation } }), key, DateTime.UtcNow);
                    Require(NativeInventory.ReadValues(document, oldTargets).Select(v => v.RawValue).SequenceEqual(NativeInventory.ReadValues(document, newTargets).Select(v => v.RawValue)), "Alias copy lost a peer's original value.");
                    Require(owners.All(e => e.GetParameters(oldName).Count == 1), "Old alias was deleted.");
                }
            } finally { document.Close(false); }
        });
    }
    private static void ProbeDeletion(Document document, NativeDefinitionSnapshot definition, CaptureTarget[] targets)
    {
        var beforeValues = Canonical.ValuesHash(NativeInventory.ReadValues(document, targets));
        var beforeDefinition = Canonical.DefinitionHash(NativeInventory.ReadDefinitions(document, new[] { definition.Source }).Single());
        var records = new List<object>();
        using (var transaction = new Transaction(document, "Rollback-only parameter deletion evidence"))
        {
            transaction.Start();
            try
            {
                foreach (var unbindFirst in new[] { false, true })
                using (var probe = new SubTransaction(document))
                {
                    probe.Start();
                    try
                    {
                        var parameter = (SharedParameterElement)document.GetElement(new ElementId(definition.ParameterId));
                        var dependents = parameter.GetDependentElements(null).Select(id => document.GetElement(id)).Where(e => e != null)
                            .Select(e => new { id = e.Id.IntegerValue, type = e.GetType().FullName, name = e.Name, category = e.Category?.Name,
                                bareBindingNode = e.GetType() == typeof(Element) && e.Category == null && string.IsNullOrEmpty(e.Name) }).ToArray();
                        bool? unbound = null;
                        if (unbindFirst) { unbound = document.ParameterBindings.Remove(parameter.GetDefinition()); document.Regenerate(); }
                        var disappearedAfterUnbind = dependents.Where(e => document.GetElement(new ElementId(e.id)) == null).Select(e => e.id).ToArray();
                        var definitionExistsBeforeDelete = document.GetElement(new ElementId(definition.ParameterId)) != null;
                        var returned = definitionExistsBeforeDelete ? document.Delete(new ElementId(definition.ParameterId)).Select(id => id.IntegerValue).ToArray() : new int[0];
                        document.Regenerate();
                        records.Add(new { unbindFirst, unbound, requestedId = definition.ParameterId, dependents, disappearedAfterUnbind, definitionExistsBeforeDelete,
                            returnedIds = returned, definitionAbsentAfterDelete = document.GetElement(new ElementId(definition.ParameterId)) == null });
                    }
                    catch (Exception ex) { records.Add(new { unbindFirst, error = ex.ToString() }); }
                    finally { probe.RollBack(); }
                }
            }
            finally { transaction.RollBack(); }
        }
        var afterValues = Canonical.ValuesHash(NativeInventory.ReadValues(document, targets));
        var afterDefinition = Canonical.DefinitionHash(NativeInventory.ReadDefinitions(document, new[] { definition.Source }).Single());
        var restored = beforeValues == afterValues && beforeDefinition == afterDefinition;
        var run = Environment.GetEnvironmentVariable("HIFC_WEB_NATIVE_RUN");
        if (!string.IsNullOrEmpty(run)) File.WriteAllText(Path.Combine(run, "native-definition-delete-probe.json"), JsonWire.Serialize(new {
            source = definition.Source, definition.ParameterId, definition.Dependencies, records, beforeValues, afterValues, beforeDefinition, afterDefinition, restored
        }));
        Require(restored, "Deletion evidence probes did not restore the original binding and all owner values.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static PublicSigningKey Key(RSA rsa) { var parts = rsa.ExportParameters(false); return new PublicSigningKey { Modulus = Convert.ToBase64String(parts.Modulus), Exponent = Convert.ToBase64String(parts.Exponent) }; }
    private static WriteChange Change(CaptureTarget target, Hifc.Web.Contracts.ParameterValue[] values, string guid, string type, string value)
    {
        var original = values.Where(v => v.OwnerUniqueId == target.OwnerUniqueId && v.Source == target.Source && v.Origin == target.Origin).ToArray();
        return new WriteChange { FieldKey = target.OwnerUniqueId + "|" + target.Source, OwnerId = target.OwnerId, OwnerUniqueId = target.OwnerUniqueId, Source = target.Source,
            SourceGuid = guid, Origin = target.Origin, DefinitionType = type, RawType = type == "YesNo" ? "Boolean" : "Text", Value = value,
            ExpectedSignature = Canonical.ValuesHash(original), Action = original.Single().Exists ? "Set" : "Create" };
    }
    private static NativeWriteInput Build(Document document, CaptureTarget[] targets, RSA rsa, Func<Hifc.Web.Contracts.ParameterValue[], WriteChange[]> changes, NativeOperations native = null)
    {
        var values = NativeInventory.ReadValues(document, targets); var sources = targets.Select(t => t.Source).Distinct().ToArray(); var definitions = NativeInventory.ReadDefinitions(document, sources); var settings = new ModelSettings();
        var context = new RequestContext { RuleVersion = "native-fixture-v2", RequestId = Guid.NewGuid().ToString("N"), DocumentSessionId = "native-parity", ScanId = Guid.NewGuid().ToString("N"), SnapshotHash = Canonical.InventoryHash(NativeInventory.Read(document)), ConfigurationHash = Canonical.ConfigurationHash(settings, NativeProjectData.Read(document)) };
        var basis = new CheckResult { Context = context, Complete = true, CaptureHash = Canonical.CaptureHash(values, definitions), Definitions = definitions };
        var plan = new EditPlan { Context = context, Id = Guid.NewGuid().ToString("N"), CaptureHash = basis.CaptureHash, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2), Changes = changes(values), Native = native ?? new NativeOperations() };
        plan.Hash = Canonical.PlanHash(plan); plan.Signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(plan.Hash), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var permit = new WritePermit { Context = context, Id = Guid.NewGuid().ToString("N"), PlanId = plan.Id, PlanHash = plan.Hash, ExpiresAtUtc = DateTime.UtcNow.AddSeconds(25) };
        permit.Signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(Canonical.PermitPayload(permit)), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return new NativeWriteInput { DocumentSessionId = context.DocumentSessionId, Settings = settings, Basis = basis, Capture = new CapturePlan { Context = context, InventoryComplete = true, DefinitionSources = sources, Targets = targets }, Plan = plan, Permit = permit };
    }
}
