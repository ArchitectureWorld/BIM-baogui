using Hifc.Web.Contracts;
using Hifc.Web.RuleEngine;
using Hifc.Web.Server;
using Npgsql;

internal static class WorkflowCases
{
    internal static async Task Run(NpgsqlDataSource source, Func<string, Func<Task>, Task> test)
    {
        using var signer = new PlanSigner(Path.Combine(Path.GetTempPath(), "HifcWorkflowTests", Guid.NewGuid().ToString("N"), "signing.pem"));
        var workflow = new WorkflowStore(source, new RuleAdapter(), signer);
        await workflow.InitializeAsync();
        var accounts = new AccountStore(source);
        async Task<Guid> Actor() => Guid.Parse((await accounts.TryCreateOrdinaryAsync("139" + Random.Shared.NextInt64(10000000, 100000000), "模型流程验收")).Id);
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        static async Task Reject(Func<Task> action) { try { await action(); } catch (InvalidOperationException) { return; } throw new Exception("Unsafe workflow operation was accepted."); }
        InventoryPage Page()
        {
            var elements = new[] { new ElementRecord { Id = 1, UniqueId = "native-element", Name = "规划净用地", Entity = "IfcSlab", DeclaredSlot = "规划净用地", ProjectedAreaM2 = 123.5 } };
            return new InventoryPage { Context = new RequestContext { RuleVersion = "reference-0.4.3-web1", RequestId = Guid.NewGuid().ToString("N"), DocumentSessionId = "native-doc", ScanId = Guid.NewGuid().ToString("N"), SnapshotHash = Canonical.InventoryHash(elements) }, Stage = "报规", ModelKind = "总图", PageCount = 1, TotalElements = 1, Elements = elements, PageHash = Canonical.InventoryHash(elements) };
        }
        async Task<CheckResult> Scan(Guid actor, InventoryPage page)
        {
            var capture = await workflow.StartAsync(actor, page);
            var values = capture.Targets.GroupBy(t => t.OwnerUniqueId + "|" + t.Origin + "|" + t.Source).Select(g => g.First()).Select(t => new ParameterValue { OwnerUniqueId = t.OwnerUniqueId, OwnerId = t.OwnerId, Source = t.Source, Origin = t.Origin }).ToArray();
            await workflow.CaptureAsync(actor, new CapturePage { Context = page.Context, PageCount = 1, TotalValues = values.Length, Values = values, PageHash = Canonical.ValuesHash(values) });
            return await workflow.CompleteAsync(actor, page.Context);
        }
        await test("incomplete server capture cannot yield a check or write preview", async () => {
            var actor = await Actor(); var page = Page(); await workflow.StartAsync(actor, page);
            await Reject(async () => await workflow.CompleteAsync(actor, page.Context));
            await Reject(async () => await workflow.PreviewAsync(actor, new PreviewRequest { Context = page.Context }));
        });
        await test("server workflow persists actual checks and signed plans across store recreation", async () => {
            var actor = await Actor(); var page = Page(); var result = await Scan(actor, page);
            var plan = await workflow.PreviewAsync(actor, new PreviewRequest { Context = result.Context });
            Require(result.Complete && result.Fields.Any(f => f.Property == "投影面积" && f.SuggestedValue == "123.5"), "Native metadata did not reach the real rule engine.");
            Require(plan.Changes.Any(c => c.Value == "123.5") && !string.IsNullOrEmpty(plan.Signature), "Preview is missing real changes or signature.");
            var reopened = new WorkflowStore(source, new RuleAdapter(), signer);
            Require((await reopened.GetResultAsync(actor, page.Context.ScanId)).CaptureHash == result.CaptureHash, "Check did not persist.");
            var permit = await reopened.AuthorizeAsync(actor, plan.Id, new AuthorizeRequest { PlanHash = plan.Hash, IdempotencyKey = "same-click" });
            var repeated = await workflow.AuthorizeAsync(actor, plan.Id, new AuthorizeRequest { PlanHash = plan.Hash, IdempotencyKey = "same-click" });
            Require(permit.Id == repeated.Id && permit.ExpiresAtUtc == repeated.ExpiresAtUtc, "A retry extended or duplicated the write permit.");
            var receipt = new WriteReceipt { PermitId = permit.Id, PlanId = plan.Id, DocumentSessionId = "native-doc", TransactionCommitted = true, ReadbackPassed = true, CompletedUtc = DateTime.UtcNow };
            await workflow.ReceiptAsync(actor, receipt);
            Require((await reopened.GetReceiptAsync(actor, plan.Id)).ReadbackPassed, "Write receipt did not persist.");
        });
        await test("one account cannot read or authorize another account's model workflow", async () => {
            var owner = await Actor(); var other = await Actor(); var page = Page(); await Scan(owner, page);
            var plan = await workflow.PreviewAsync(owner, new PreviewRequest { Context = page.Context });
            await Reject(async () => await workflow.GetResultAsync(other, page.Context.ScanId));
            await Reject(async () => await workflow.AuthorizeAsync(other, plan.Id, new AuthorizeRequest { PlanHash = plan.Hash, IdempotencyKey = "cross-account" }));
        });
        await test("same-page network retries are idempotent while conflicting page data is refused", async () => {
            var actor = await Actor(); var page = Page(); await workflow.StartAsync(actor, page); await workflow.StartAsync(actor, page);
            page.Elements[0].Name = "changed-after-upload"; page.PageHash = Canonical.InventoryHash(page.Elements);
            await Reject(async () => await workflow.StartAsync(actor, page));
        });
        await test("expired accounts keep existing check history but cannot obtain a new write permit", async () => {
            var actor = await Actor(); var page = Page(); await Scan(actor, page);
            var plan = await workflow.PreviewAsync(actor, new PreviewRequest { Context = page.Context });
            await using var expire = source.CreateCommand("UPDATE hifc_web.accounts SET expires_at=clock_timestamp()-interval '1 second' WHERE id=@id");
            expire.Parameters.AddWithValue("id", actor); await expire.ExecuteNonQueryAsync();
            Require((await workflow.GetResultAsync(actor, page.Context.ScanId)).Complete, "Expiry hid existing history.");
            await Reject(async () => await workflow.AuthorizeAsync(actor, plan.Id, new AuthorizeRequest { PlanHash = plan.Hash, IdempotencyKey = "expired" }));
        });
    }
}
