using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Hifc.Reference;

internal static class RecognitionTests
{
    public sealed class Fixture { public string Scope { get; set; } public string ExpectedSlot { get; set; } public ModelElementCheck Row { get; set; } }
    private static void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static object[] Candidates(ModelElementCheck row, string kind)
    {
        var method = typeof(CaseWorkflow).GetMethod("GetCandidates");
        Assert(method != null, "Candidate evidence policy is missing");
        return ((System.Collections.IEnumerable)method.Invoke(null, new object[] { row, kind })).Cast<object>().ToArray();
    }
    private static object Get(object item, string property) => item.GetType().GetProperty(property)?.GetValue(item);
    public static void Run(Action<string, Action> check)
    {
        var fixtures = new JavaScriptSerializer().Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recognition-rows.json")));
        foreach (var fixture in fixtures)
            check("actual_case_candidate_" + fixture.Scope + "_" + fixture.Row.Id, () =>
                Assert(CaseWorkflow.SuggestSlots(fixture.Row, fixture.Scope).Contains(fixture.ExpectedSlot), "Missing " + fixture.ExpectedSlot + " for " + fixture.Row.Name));
        check("same_numeric_id_in_other_document_cannot_use_case_correspondence", () => {
            var row = new ModelElementCheck { Id = 451481, UniqueId = "another-document", Name = "道路", TypeName = "道路", Category = "楼板", Entity = "IfcSlab" };
            Assert(CaseWorkflow.SuggestSlots(row, "总图").Length == 0, "Numeric ID leaked across documents");
        });
        check("case_correspondence_requires_matching_model_scope", () => {
            var row = fixtures.Single(x => x.Row.Id == 451481 && x.Scope == "总图").Row;
            Assert(CaseWorkflow.SuggestSlots(row, "单体建筑").Length == 0, "Site role used in building");
        });
        check("green_alias_survives_general_type_classification", () => {
            var row = new ModelElementCheck { Name = "绿化", Entity = "IfcSlab", Slots = new[] { "总平构件" }, Classified = true };
            Assert(CaseWorkflow.SuggestSlots(row, "总图").Contains("绿地"), "Type set masked instance candidate");
        });
        check("parking_carrier_mismatch_is_a_review_candidate", () => {
            var row = new ModelElementCheck { Name = "停车位", Entity = "IfcBuildingElementProxy" };
            Assert(CaseWorkflow.SuggestSlots(row, "总图").Contains("停车位"), "Carrier mismatch hidden as missing model");
            Assert(Candidates(row, "总图").All(c => !(bool)Get(c, "CanPrepare")), "Unsupported carrier entered automatic write");
        });
        check("contradictory_case_parking_road_is_not_auto_prepared", () => {
            var row = fixtures.Single(x => x.Row.Id == 455815).Row;
            var candidates = Candidates(row, "总图");
            Assert(candidates.Length >= 2 && candidates.All(c => !(bool)Get(c, "CanPrepare")), "Anomalous case used as automatic classification");
        });
        check("changed_case_identity_requires_review", () => {
            var original = fixtures.Single(x => x.Row.Id == 451959).Row;
            var row = new ModelElementCheck { Id = original.Id, UniqueId = original.UniqueId, Name = "已改用途的楼板", TypeName = original.TypeName, Category = original.Category, Entity = original.Entity };
            var candidates = Candidates(row, "总图");
            Assert(candidates.Any(c => (string)Get(c, "Slot") == "规划净用地") && candidates.All(c => !(bool)Get(c, "CanPrepare")), "Changed identity silently accepted or lost");
        });
        check("candidate_is_not_reported_as_missing_or_complete", () => {
            var row = new ModelElementCheck { Name = "绿化", Entity = "IfcSlab", Slots = new[] { "总平构件" }, Classified = true, SuggestedSlots = new[] { "绿地" } };
            var summary = CaseWorkflow.Summarize("报规", "总图", new[] { row });
            Assert(summary.MissingRequired == 6 && summary.Roles.Single(r => r.Slot == "绿地").State == "待确认分类", "Candidate counted as missing");
            Assert(row.StatusText.Contains("待确认") && row.SlotText.Contains("绿地"), "Type completion hides candidate on screen");
        });
        check("candidate_evidence_identifies_ifc_and_carrier", () => {
            var row = fixtures.Single(x => x.Row.Id == 451959).Row;
            var candidate = Candidates(row, "总图").Single(c => (string)Get(c, "Slot") == "规划净用地");
            var evidence = (string)Get(candidate, "Evidence");
            Assert(evidence.Contains("总图-属性写入-15.ifc") && evidence.Contains("451959") && (bool)Get(candidate, "CanPrepare"), "Reviewable source evidence missing");
        });
    }
}
