using Hifc.Reference;
using Hifc.Web.Contracts;
using Hifc.Web.RuleEngine;

internal static class RuleCases
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action run) { try { run(); } catch (InvalidOperationException) { return; } throw new Exception("Unsafe rule operation was accepted."); }
    internal static (RuleAdapter Engine, InventoryPage[] Pages, CapturePlan Plan, ParameterValue[] Values) Model(string slot = "规划净用地", string entity = "IfcSlab", string name = "规划净用地")
    {
        var engine = new RuleAdapter();
        var element = new ElementRecord { Id = 1, UniqueId = "element-1", TypeId = 2, TypeUniqueId = "type-1", Name = name, Entity = entity, DeclaredSlot = slot, ProjectedAreaM2 = 123.5 };
        var context = new RequestContext { RuleVersion = engine.RuleVersion, RequestId = "r1", ScanId = "s1", DocumentSessionId = "d1", SnapshotHash = Canonical.InventoryHash(new[] { element }) };
        var pages = new[] { new InventoryPage { Context = context, Stage = "报规", ModelKind = "总图", PageCount = 1, TotalElements = 1, PageHash = Canonical.InventoryHash(new[] { element }), Elements = new[] { element } } };
        var plan = engine.CreateCapturePlan(pages);
        var values = plan.Targets.GroupBy(t => t.OwnerUniqueId + "|" + t.Origin + "|" + t.Source).Select(g => g.First()).Select(t => new ParameterValue { OwnerUniqueId = t.OwnerUniqueId, OwnerId = t.OwnerId, Source = t.Source, Origin = t.Origin }).ToArray();
        return (engine, pages, plan, values);
    }
    internal static CheckResult Evaluate((RuleAdapter Engine, InventoryPage[] Pages, CapturePlan Plan, ParameterValue[] Values) fixture)
    {
        var pages = new[] { new CapturePage { Context = fixture.Plan.Context, PageCount = 1, TotalValues = fixture.Values.Length, Values = fixture.Values, PageHash = Canonical.ValuesHash(fixture.Values) } };
        return fixture.Engine.Evaluate(fixture.Pages, fixture.Plan, pages);
    }
    internal static void Run(Action<string, Action> test)
    {
        test("server selects fields only for current model carriers", () => { var fixture = Model(null, "IfcFurnishingElement", "办公桌"); Require(fixture.Plan.Targets.All(t => t.Source == "类目"), "Unrelated business fields were requested"); });
        test("server returns native area suggestion without changing input", () => { var fixture = Model(); var result = Evaluate(fixture); var area = result.Fields.Single(f => f.Property == "投影面积"); Require(area.SuggestedValue == "123.5" && area.Value == null, "Area was fabricated or overwrote existing data"); });
        test("only road width carries manual input requirement", () => { var fixture = Model("道路边界线", "IfcSlab", "道路边界线"); var result = Evaluate(fixture); Require(result.Fields.Any(f => f.Property == "道路宽度" && f.ManualInputRequired) && result.Fields.Where(f => f.Property != "道路宽度").All(f => !f.ManualInputRequired), "Required field scope changed"); });
        test("numeric zero remains a value and is never autofilled", () => { var fixture = Model(); var source = CaseWorkflow.ForModel("报规", "总图").Single(r => r.Slot == "规划净用地").Properties.Single(p => p.Property == "投影面积").SourceParameter; var value = fixture.Values.Single(v => v.OwnerUniqueId == "element-1" && v.Source == source); value.Exists = true; value.HasValue = true; value.RawValue = "0"; value.StorageType = "Double"; var field = Evaluate(fixture).Fields.Single(f => f.Property == "投影面积"); Require(field.Value == "0" && field.SuggestedValue == null, "Zero was replaced"); });
        test("duplicate source names stay ambiguous and nonwritable", () => { var fixture = Model(); var source = CaseWorkflow.ForModel("报规", "总图").Single(r => r.Slot == "规划净用地").Properties.Single(p => p.Property == "名称").SourceParameter; var a = fixture.Values.Single(v => v.OwnerUniqueId == "element-1" && v.Source == source); a.Exists = true; a.HasValue = true; a.RawValue = "A"; a.StorageType = "String"; fixture.Values = fixture.Values.Append(new ParameterValue { OwnerUniqueId = a.OwnerUniqueId, OwnerId = a.OwnerId, Source = a.Source, Origin = a.Origin, Exists = true, HasValue = true, RawValue = "B", StorageType = "String" }).ToArray(); var field = Evaluate(fixture).Fields.Single(f => f.Property == "名称"); Require(field.State == "有歧义" && !field.Editable, "Ambiguous sources were writable"); });
        test("manual edits cannot overwrite a concurrently changed source", () => { var fixture = Model(); var result = Evaluate(fixture); var field = result.Fields.Single(f => f.Property == "名称"); Reject(() => fixture.Engine.BuildEditPlan(result, fixture.Values, new[] { new EditDraft { FieldKey = field.Key, OriginalSignature = "stale", Value = "changed" } }, true, DateTime.UtcNow)); });
        test("server creates a plan from scoped automatic suggestions", () => { var fixture = Model(); var result = Evaluate(fixture); var plan = fixture.Engine.BuildEditPlan(result, fixture.Values, Array.Empty<EditDraft>(), true, new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc)); Require(plan.Changes.Any(c => c.Value == "123.5") && plan.ExpiresAtUtc == new DateTime(2026, 10, 17, 0, 2, 0, DateTimeKind.Utc) && plan.Hash == Canonical.PlanHash(plan), "Invalid or missing edit plan"); });
        test("assigned classification outside current model scope is refused", () => { var fixture = Model(); fixture.Pages[0].Elements[0].AssignedSlot = "任意私有类目"; fixture.Pages[0].Context.SnapshotHash = fixture.Pages[0].PageHash = Canonical.InventoryHash(fixture.Pages[0].Elements); Reject(() => fixture.Engine.CreateCapturePlan(fixture.Pages)); });
    }
}
