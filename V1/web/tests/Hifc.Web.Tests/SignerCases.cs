using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Hifc.Web.Server;

internal static class SignerCases
{
    internal static void Run(Action<string, Action> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "hifc-web-key-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "signing-key.pem");
        var now = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
        EditPlan Plan() => new() { Context = new RequestContext { RuleVersion = "r", RequestId = "request", DocumentSessionId = "doc", ScanId = "scan", SnapshotHash = "hash" }, Id = "plan1", CaptureHash = "capture", ExpiresAtUtc = now.AddSeconds(120) };
        void Reject(Action run) { try { run(); } catch (InvalidOperationException) { return; } throw new Exception("Unsafe authorization was accepted."); }
        test("server signs plans and permits accepted by the real client", () => { using var signer = new PlanSigner(path); var plan = Plan(); signer.Sign(plan); var permit = signer.IssuePermit(plan, new AccountInfo { ExpiresAtUtc = now.AddHours(1) }, now, "permit"); WriteGuard.Plan(plan, plan.Context, signer.PublicKey, now); WriteGuard.Permit(permit, plan, signer.PublicKey, now); });
        test("server signing identity survives restart", () => { string first; using (var signer = new PlanSigner(path)) first = signer.PublicKey?.Modulus; using var restarted = new PlanSigner(path); if (string.IsNullOrEmpty(first) || restarted.PublicKey?.Modulus != first || !File.Exists(path)) throw new Exception("Server changed signing identity on restart"); });
        test("write authorization rechecks account deadline", () => { using var signer = new PlanSigner(path); var plan = Plan(); signer.Sign(plan); Reject(() => signer.IssuePermit(plan, new AccountInfo { ExpiresAtUtc = now }, now, "permit")); });
        test("write permit cannot outlive its edit plan", () => { using var signer = new PlanSigner(path); var plan = Plan(); plan.ExpiresAtUtc = now.AddSeconds(5); signer.Sign(plan); var permit = signer.IssuePermit(plan, new AccountInfo { ExpiresAtUtc = now.AddHours(1) }, now, "permit"); if (permit.ExpiresAtUtc != now.AddSeconds(5)) throw new Exception("Permit outlived edit plan"); });
        test("ordinary write permit lasts no more than 30 seconds", () => { using var signer = new PlanSigner(path); var plan = Plan(); signer.Sign(plan); var permit = signer.IssuePermit(plan, new AccountInfo { ExpiresAtUtc = now.AddHours(1) }, now, "permit"); if (permit.ExpiresAtUtc != now.AddSeconds(30)) throw new Exception("Permit expiry is incorrect"); });
        test("permit cannot start a transaction after account expiry", () => { using var signer = new PlanSigner(path); var plan = Plan(); signer.Sign(plan); var permit = signer.IssuePermit(plan, new AccountInfo { ExpiresAtUtc = now.AddSeconds(3) }, now, "permit"); if (permit.ExpiresAtUtc != now.AddSeconds(3)) throw new Exception("Permit permits a write after account expiration"); });
    }
}
