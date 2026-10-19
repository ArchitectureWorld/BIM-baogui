using Hifc.Web.Contracts;

internal static class MainSyncCases
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action run) { try { run(); } catch (InvalidOperationException) { return; } throw new Exception("An incompatible rule operation was accepted."); }

    internal static void Run(Action<string, Action> test)
    {
        test("server JSON adapter reads the new core underground context by runtime type", () => {
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            var deserialize = serializer.GetType().GetMethod("Deserialize", new[] { typeof(string), typeof(Type) });
            Require(deserialize != null, "The main core runtime-type JSON API is unavailable on the Web server.");
            var settings = (Hifc.Reference.UndergroundSettings)deserialize.Invoke(serializer, new object[] {
                "\uFEFF{\"confirmed\":true,\"groundElevationM\":101,\"levelUniqueIds\":[\"b1\"]}", typeof(Hifc.Reference.UndergroundSettings)
            });
            Require(settings.Confirmed && settings.GroundElevationM == 101 && settings.LevelUniqueIds.SequenceEqual(new[] { "b1" }), "Underground context changed while deserializing.");
        });
        test("main synchronization refuses a new scan using the old 0.4.3 rules", () => {
            var fixture = RuleCases.Model();
            fixture.Pages[0].Context.RuleVersion = "reference-0.4.3-web1";
            Reject(() => fixture.Engine.CreateCapturePlan(fixture.Pages));
        });
        test("persisted capture cannot be evaluated under a different rule revision", () => {
            var fixture = RuleCases.Model();
            fixture.Pages[0].Context.RuleVersion = "reference-0.4.2-web1";
            Reject(() => RuleCases.Evaluate(fixture));
        });
        test("underground floor count can be edited as an integer through the Web plan", () => {
            var fixture = RuleCases.Model(null, "IfcBuilding", "地下建筑", kind: "单体建筑");
            var result = RuleCases.Evaluate(fixture);
            var field = result.Fields.Single(f => f.Property == "地下建筑层数");
            var plan = fixture.Engine.BuildEditPlan(result, fixture.Values, new[] {
                new EditDraft { FieldKey = field.Key, OriginalSignature = field.SourceSignature, Value = "2" }
            }, false, DateTime.UtcNow);
            var change = plan.Changes.Single(c => c.Source == field.Source);
            Require(change.RawType == "Integer" && change.Value == "2", "The frozen Integerl spelling reached the writable contract.");
        });
        test("declared planning areas on a room carrier remain nonwritable", () => {
            foreach (var slot in new[] { "人防区域", "消防分区", "停车场", "停车位" }) {
                var fixture = RuleCases.Model(slot, "IfcSpace", slot, kind: "单体建筑");
                var result = RuleCases.Evaluate(fixture);
                Require(result.Fields.Length == 0 && result.ReviewCount == 1 && result.Elements.Single().Status.Contains("Area"),
                    slot + " accepted a room as the required Revit Area carrier.");
            }
        });
        test("manual planning area classification refuses a room carrier", () => {
            var fixture = RuleCases.Model(null, "IfcSpace", "普通房间", kind: "单体建筑");
            fixture.Pages[0].Elements[0].AssignedSlot = "人防区域";
            fixture.Pages[0].Context.SnapshotHash = fixture.Pages[0].PageHash = Canonical.InventoryHash(fixture.Pages[0].Elements);
            Reject(() => fixture.Engine.CreateCapturePlan(fixture.Pages));
        });
        test("planning Area suggestions use actual square metres from older clients", () => {
            foreach (var slot in new[] { "人防区域", "消防分区", "停车场" }) {
                var fixture = RuleCases.Model(slot, "IfcSpace", slot, kind: "单体建筑", areaScheme: slot, nativeAreaM2: 110);
                fixture.Pages[0].Elements[0].ProjectedAreaM2 = null;
                fixture.Pages[0].Context.SnapshotHash = fixture.Pages[0].PageHash = Canonical.InventoryHash(fixture.Pages[0].Elements);
                var field = RuleCases.Evaluate(fixture).Fields.Single(f => f.Property == "投影面积");
                Require(field.SuggestedValue == "110" && field.SuggestionUnit == "m²" && field.Detail.Contains("Area"), slot + " lost its actual Area value or source.");
            }
        });
    }
}
