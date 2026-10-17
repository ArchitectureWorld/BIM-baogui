using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Hifc.Web.Revit;

public sealed class HifcWebNativeHost : IExternalApplication
{
    private string run;
    private readonly List<object> results = new List<object>();
    private int passed, failed;
    private const string Source = "HIFC_Web_验收来源";
    private const string SourceGuid = "5ebfb6d4-fdc5-4f0f-aa6c-f56afc111b81";
    public Result OnStartup(UIControlledApplication application)
    {
        run = Environment.GetEnvironmentVariable("HIFC_WEB_NATIVE_RUN");
        if (!string.IsNullOrEmpty(run)) application.Idling += Tick;
        return Result.Succeeded;
    }
    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
    private void Log(string message) => File.AppendAllText(Path.Combine(run, "progress.log"), DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine);
    private void Test(string name, Action action)
    {
        try { action(); passed++; results.Add(new { name, passed = true }); Log("PASS " + name); }
        catch (Exception ex) { failed++; results.Add(new { name, passed = false, error = ex.ToString() }); Log("FAIL " + name + " " + ex.Message); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Unsafe transaction was accepted"); }
    private void Tick(object sender, IdlingEventArgs args)
    {
        var ui = sender as UIApplication; if (ui == null) return;
        ui.Idling -= Tick;
        if (File.Exists(Path.Combine(run, "full-flow.json"))) { FullFlow.Start(ui, run); return; }
        Document doc = null;
        try
        {
            var config = JsonWire.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(run, "config.json")));
            Log("Opening model copy"); doc = ui.Application.OpenDocumentFile(config["model"]);
            ElementId levelId, materialId;
            using (var tx = new Transaction(doc, "HIFC Web validation fixtures"))
            {
                tx.Start(); var level = Level.Create(doc, 900); level.Name = "HIFC Web 验收标高"; levelId = level.Id;
                materialId = Material.Create(doc, "HIFC Web 资源排除验收"); tx.Commit();
            }
            var levelUniqueId = doc.GetElement(levelId).UniqueId;
            Test("complete inventory includes project and levels but excludes material resources", () => {
                var rows = NativeInventory.Read(doc);
                Require(rows.Any(r => r.UniqueId == levelUniqueId) && rows.Any(r => r.Entity == "IfcBuilding"), "Required model records missing");
                Require(rows.All(r => r.Id != materialId.IntegerValue), "Material resource leaked into model inventory");
            });
            var target = new CaptureTarget { ElementUniqueId = levelUniqueId, OwnerUniqueId = levelUniqueId, OwnerId = levelId.IntegerValue, Source = Source, Origin = "instance" };
            Test("missing parameter is captured explicitly", () => { var values = NativeInventory.ReadValues(doc, new[] { target }); Require(values.Length == 1 && !values[0].Exists && !values[0].HasValue, "Missing parameter was omitted or fabricated"); });
            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                var publicParts = rsa.ExportParameters(false);
                var key = new PublicSigningKey { Modulus = Convert.ToBase64String(publicParts.Modulus), Exponent = Convert.ToBase64String(publicParts.Exponent) };
                Func<string, NativeWriteInput> plan = value => Build(doc, target, value, rsa);
                Test("signed native transaction creates source and reads back exact value", () => {
                    var input = plan("web-before"); var receipt = NativeWriter.Apply(doc, input, key, DateTime.UtcNow);
                    Require(receipt.TransactionCommitted && receipt.ReadbackPassed, "Transaction was not committed and verified");
                    var parameter = doc.GetElement(levelId).GetParameters(Source).Single();
                    Require(parameter.IsShared && parameter.GUID == new Guid(SourceGuid) && parameter.AsString() == "web-before", "Wrong GUID or value");
                });
                Test("stale native source is rejected before writing", () => {
                    var input = plan("web-stale");
                    using (var tx = new Transaction(doc, "Concurrent source change")) { tx.Start(); doc.GetElement(levelId).LookupParameter(Source).Set("changed-in-model"); tx.Commit(); }
                    Reject(() => NativeWriter.Apply(doc, input, key, DateTime.UtcNow));
                    Require(doc.GetElement(levelId).LookupParameter(Source).AsString() == "changed-in-model", "Stale plan overwrote model edit");
                });
                Test("readback interference rolls back the entire native transaction", () => {
                    var parameter = doc.GetElement(levelId).LookupParameter(Source);
                    var updater = new InterferenceUpdater(ui.ActiveAddInId, levelId, Source);
                    UpdaterRegistry.RegisterUpdater(updater, true);
                    UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), doc, new List<ElementId> { levelId }, Element.GetChangeTypeParameter(parameter.Id));
                    try { var input = plan("web-trigger"); Reject(() => NativeWriter.Apply(doc, input, key, DateTime.UtcNow)); Require(doc.GetElement(levelId).LookupParameter(Source).AsString() == "changed-in-model", "Rollback lost original value"); }
                    finally { UpdaterRegistry.UnregisterUpdater(updater.GetUpdaterId()); }
                });
            }
            var savedModel = doc.PathName;
            Test("saved model copy preserves verified source on reopen", () => {
                doc.Save(); doc.Close(false); doc = null; doc = ui.Application.OpenDocumentFile(savedModel);
                Require(doc.GetElement(levelUniqueId).LookupParameter(Source).AsString() == "changed-in-model", "Saved model did not preserve verified value");
            });
            doc.Close(false); doc = null;
            File.WriteAllText(Path.Combine(run, "native-results.json"), JsonWire.Serialize(new { passed, failed, results, scope = "Real Revit 2020 model copy; native transport/security tests, not Linux end-to-end acceptance" }));
        }
        catch (Exception ex) { Log("HOST_ERROR " + ex); File.WriteAllText(Path.Combine(run, "host-error.json"), JsonWire.Serialize(new { error = ex.ToString() })); }
        finally { if (doc != null && doc.IsValidObject) doc.Close(false); if (ui.Application.Documents.Size == 0) ui.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit)); }
    }
    private static NativeWriteInput Build(Document doc, CaptureTarget target, string value, RSA rsa)
    {
        var records = NativeInventory.Read(doc); var values = NativeInventory.ReadValues(doc, new[] { target });
        var context = new RequestContext { RuleVersion = "native-fixture", RequestId = Guid.NewGuid().ToString("N"), DocumentSessionId = "native-doc", ScanId = Guid.NewGuid().ToString("N"), SnapshotHash = Canonical.InventoryHash(records) };
        var basis = new CheckResult { Context = context, Complete = true, CaptureHash = Canonical.ValuesHash(values) };
        var plan = new EditPlan { Context = context, Id = Guid.NewGuid().ToString("N"), CaptureHash = basis.CaptureHash, ExpiresAtUtc = DateTime.UtcNow.AddSeconds(120), Changes = new[] { new WriteChange { OwnerId = target.OwnerId, OwnerUniqueId = target.OwnerUniqueId, Source = Source, SourceGuid = SourceGuid,
            Origin = "instance", Action = values.FirstOrDefault()?.Exists == true ? "Set" : "Create", RawType = "Text", Value = value, ExpectedSignature = Canonical.ValuesHash(values), Manual = true } } };
        plan.Hash = Canonical.PlanHash(plan); plan.Signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(plan.Hash), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var permit = new WritePermit { Context = context, Id = Guid.NewGuid().ToString("N"), PlanId = plan.Id, PlanHash = plan.Hash, ExpiresAtUtc = DateTime.UtcNow.AddSeconds(25) };
        permit.Signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(Canonical.PermitPayload(permit)), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return new NativeWriteInput { DocumentSessionId = "native-doc", Basis = basis, Capture = new CapturePlan { Context = context, Targets = new[] { target }, InventoryComplete = true }, Plan = plan, Permit = permit };
    }
    private sealed class InterferenceUpdater : IUpdater
    {
        private readonly UpdaterId id; private readonly ElementId element; private readonly string source;
        internal InterferenceUpdater(AddInId addIn, ElementId element, string source) { id = new UpdaterId(addIn, new Guid("4df1d18e-f0a4-4c55-83e1-a392bcbd2674")); this.element = element; this.source = source; }
        public void Execute(UpdaterData data) { var p = data.GetDocument().GetElement(element)?.LookupParameter(source); if (p?.AsString() == "web-trigger") p.Set("updater-interference"); }
        public string GetAdditionalInformation() => "Web native readback rollback validation";
        public ChangePriority GetChangePriority() => ChangePriority.FreeStandingComponents;
        public UpdaterId GetUpdaterId() => id;
        public string GetUpdaterName() => "HIFC Web native validation interference";
    }
}
