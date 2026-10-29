using Hifc.Web.Contracts;

internal static class Protocol2Cases
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Unsafe protocol state was accepted."); }
    internal static void Run(Action<string, Action> test)
    {
        test("protocol 1 cannot authorize a new model operation", () => {
            var legacy = new RequestContext { ProtocolMajor = 1, RequestId = "legacy", DocumentSessionId = "doc", ScanId = "scan", SnapshotHash = "hash" };
            Reject(() => ProtocolGuard.Match(legacy, legacy));
        });
        test("changing planning settings invalidates response context", () => {
            var property = typeof(RequestContext).GetProperty("ConfigurationHash");
            Require(property != null, "Planning and material context is absent from the protocol.");
            var expected = new RequestContext { RequestId = "same", DocumentSessionId = "doc", ScanId = "scan", SnapshotHash = "hash" };
            var changed = new RequestContext { RequestId = "same", DocumentSessionId = "doc", ScanId = "scan", SnapshotHash = "hash" };
            property.SetValue(expected, "before"); property.SetValue(changed, "after");
            Reject(() => ProtocolGuard.Match(expected, changed));
        });
        test("native parameter migrations are covered by the plan signature", () => {
            var property = typeof(EditPlan).GetProperty("Native");
            Require(property != null, "Native migration instructions have no signed plan field.");
            var native = new NativeOperations { Definitions = new[] { new NativeDefinitionOperation { Source = "flag", Guid = "guid", DefinitionType = "YesNo", Origin = "instance", ExpectedBindingHash = "binding", CategoryIds = new[] { 1 } } } };
            var plan = new EditPlan { Context = new RequestContext(), Id = "plan", ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2) };
            property.SetValue(plan, native); var before = Canonical.PlanHash(plan);
            native.Definitions[0].CategoryIds = new[] { 2 };
            Require(Canonical.PlanHash(plan) != before, "A migration can change category scope without invalidating its signature.");
        });
        test("definition chunks retain complete outside-selection owners in the server capture", () => {
            var fixture = RuleCases.Model(); var source = fixture.Values.First(v => v.Origin == "instance" && v.Source != "类目").Source;
            var definition = new NativeDefinitionSnapshot { Source = source, ParameterId = 42, DefinitionType = "Text", Origin = "instance", CategoryIds = new[] { -2000032 },
                Values = Enumerable.Range(1, 500).Select(i => new ParameterValue { OwnerId = i, OwnerUniqueId = "owner-" + i, Source = source, Origin = "instance", Exists = true, HasValue = true, RawValue = i.ToString(), StorageType = "String", DefinitionType = "Text" }).ToArray() };
            definition.Hash = Canonical.DefinitionHash(definition);
            NativeDefinitionSnapshot Header() => new() { Source = source, ParameterId = 42, DefinitionType = "Text", Origin = "instance", CategoryIds = new[] { -2000032 }, Hash = definition.Hash };
            var chunks = new[] { new NativeDefinitionChunk { Header = Header(), TotalValues = 500, ValueOffset = 0, Values = definition.Values.Take(256).ToArray() },
                new NativeDefinitionChunk { Header = Header(), TotalValues = 500, ValueOffset = 256, Values = definition.Values.Skip(256).ToArray() } };
            var pages = new[] { new CapturePage { Context = fixture.Plan.Context, PageCount = 2, TotalValues = fixture.Values.Length, Values = fixture.Values, PageHash = Canonical.ValuesHash(fixture.Values), DefinitionChunks = new[] { chunks[0] } },
                new CapturePage { Context = fixture.Plan.Context, PageIndex = 1, PageCount = 2, TotalValues = fixture.Values.Length, PageHash = Canonical.ValuesHash(Array.Empty<ParameterValue>()), DefinitionChunks = new[] { chunks[1] } } };
            var result = fixture.Engine.Evaluate(fixture.Pages, fixture.Plan, pages);
            Require(result.Definitions.Length == 1 && result.Definitions[0].Values.Length == 500 && result.CaptureHash == Canonical.CaptureHash(fixture.Values, new[] { definition }), "Definition owners were dropped when reassembling capture pages.");
            chunks[1].ValueOffset = 0; Reject(() => ProtocolGuard.Definitions(pages)); chunks[1].ValueOffset = 256;
            chunks[1].Values[0].RawValue = "tampered"; Reject(() => ProtocolGuard.Definitions(pages));
        });
    }
}
