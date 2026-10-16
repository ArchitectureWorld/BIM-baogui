using System.Security.Cryptography;
using System.Text;
using Hifc.Web.Client;
using Hifc.Web.Contracts;

internal static class ClientCases
{
    internal static void Run(Action<string, Action> test)
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        var key = new PublicSigningKey { Modulus = Convert.ToBase64String(parameters.Modulus), Exponent = Convert.ToBase64String(parameters.Exponent) };
        var context = new RequestContext { RuleVersion = "rules1", RequestId = "r", DocumentSessionId = "d", ScanId = "s", SnapshotHash = "metadata" };
        var now = new DateTime(2026, 10, 17, 1, 0, 0, DateTimeKind.Utc);
        EditPlan Plan()
        {
            var plan = new EditPlan { Context = context, Id = "p1", CaptureHash = "capture", ExpiresAtUtc = now.AddSeconds(120), Changes = new[] { new WriteChange { OwnerUniqueId = "owner", Source = "width", Value = "4" } } };
            plan.Hash = Canonical.PlanHash(plan); plan.Signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(plan.Hash), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)); return plan;
        }
        WritePermit Permit(EditPlan plan)
        {
            var permit = new WritePermit { Context = context, Id = "permit-1", PlanId = plan.Id, PlanHash = plan.Hash, ExpiresAtUtc = now.AddSeconds(30) };
            permit.Signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(Canonical.PermitPayload(permit)), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)); return permit;
        }
        void Reject(Action run) { try { run(); } catch (InvalidOperationException) { return; } throw new Exception("Unsafe write was accepted."); }
        test("client accepts a valid signed plan and permit", () => { var plan = Plan(); WriteGuard.Plan(plan, context, key, now); WriteGuard.Permit(Permit(plan), plan, key, now); });
        test("client rejects edits to a signed write value", () => { var plan = Plan(); plan.Changes[0].Value = "100"; Reject(() => WriteGuard.Plan(plan, context, key, now)); });
        test("client rejects recomputed hash without server signature", () => { var plan = Plan(); plan.Changes[0].Value = "100"; plan.Hash = Canonical.PlanHash(plan); Reject(() => WriteGuard.Plan(plan, context, key, now)); });
        test("client rejects expired plans without deleting drafts", () => Reject(() => WriteGuard.Plan(Plan(), context, key, now.AddSeconds(120))));
        test("client rejects expired write permits", () => { var plan = Plan(); Reject(() => WriteGuard.Permit(Permit(plan), plan, key, now.AddSeconds(30))); });
        test("client rejects permit transplanted to another plan", () => { var plan = Plan(); var permit = Permit(plan); plan.Id = "other-plan"; Reject(() => WriteGuard.Permit(permit, plan, key, now)); });
        test("client detects a new instance parameter overriding type value", () => { var value = new ParameterValue { OwnerUniqueId = "type", Source = "width", Origin = "type", Exists = true, HasValue = true, RawValue = "4" }; var element = new ElementRecord { UniqueId = "instance", TypeUniqueId = "type" }; var basis = new CheckResult { Context = new RequestContext { SnapshotHash = Canonical.InventoryHash(new[] { element }) }, Complete = true, CaptureHash = Canonical.ValuesHash(new[] { value }) }; Reject(() => WriteGuard.Capture(basis, new[] { element }, new[] { value, new ParameterValue { OwnerUniqueId = "instance", Source = "width", Origin = "instance", Exists = true, HasValue = true, RawValue = "9" } })); });
        test("client detects a changed type before beginning a transaction", () => { var element = new ElementRecord { UniqueId = "instance", TypeUniqueId = "type-old" }; var basis = new CheckResult { Context = new RequestContext { SnapshotHash = Canonical.InventoryHash(new[] { element }) }, Complete = true, CaptureHash = Canonical.ValuesHash(Array.Empty<ParameterValue>()) }; element.TypeUniqueId = "type-new"; Reject(() => WriteGuard.Capture(basis, new[] { element }, Array.Empty<ParameterValue>())); });
        test("client and server JSON preserve signed UTC timestamps", () => { var plan = Plan(); var serverCopy = System.Text.Json.JsonSerializer.Deserialize<EditPlan>(JsonWire.Serialize(plan)); if (serverCopy == null || serverCopy.ExpiresAtUtc != plan.ExpiresAtUtc || Canonical.PlanHash(serverCopy) != plan.Hash) throw new Exception("Cross-runtime JSON changed a signed plan"); var clientCopy = JsonWire.Deserialize<EditPlan>(System.Text.Json.JsonSerializer.Serialize(plan)); if (clientCopy == null || Canonical.PlanHash(clientCopy) != plan.Hash) throw new Exception("Client could not read server JSON"); });
        test("JSON keeps unset zero false and explicit empty distinct", () => { var data = new[] { new ParameterValue { Exists = false }, new ParameterValue { Exists = true, HasValue = false }, new ParameterValue { Exists = true, HasValue = true, RawValue = "0", StorageType = "Integer" }, new ParameterValue { Exists = true, HasValue = true, RawValue = "", StorageType = "String" } }; var copy = JsonWire.Deserialize<ParameterValue[]>(JsonWire.Serialize(data)); if (copy == null || copy.Length != 4 || copy[0].Exists || copy[1].HasValue || copy[2].RawValue != "0" || copy[3].RawValue != "") throw new Exception("Value states collapsed in transit"); });
    }
}
