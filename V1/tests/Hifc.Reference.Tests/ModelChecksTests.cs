using System;
using System.Linq;
using Hifc.Reference;

internal static class ModelChecksTests
{
    private static void Assert(bool value) { if (!value) throw new Exception("assertion failed"); }
    private static ReferenceProperty Property(string type = "Real") => new ReferenceProperty { PropertySet = "Pset_阳台信息属性集", Property = "投影面积", SourceParameter = "阳台投影面积", RawType = type };
    private static ParameterRead Read(object value, string origin = "instance") => new ParameterRead { Name = "阳台投影面积", Origin = origin, HasValue = value != null, Value = value };
    public static void Run(Action<string, Action> check)
    {
        check("manager_zero_is_valid", () => Assert(ModelChecks.Evaluate(Property(), new[] { Read(0.0) }).Valid));
        check("manager_false_is_valid", () => Assert(ModelChecks.Evaluate(Property("Boolean"), new[] { Read(false) }).Valid));
        check("manager_distinguishes_missing_parameter_and_empty", () => {
            Assert(ModelChecks.Evaluate(Property(), new ParameterRead[0]).State == "缺参数");
            Assert(ModelChecks.Evaluate(Property(), new[] { Read(null) }).State == "未填写");
        });
        check("manager_ambiguous_names_are_not_complete", () => Assert(ModelChecks.Evaluate(Property(), new[] { Read(1), Read(2) }).State == "有歧义"));
        check("manager_bad_number_is_not_complete", () => Assert(ModelChecks.Evaluate(Property(), new[] { Read("abc") }).State == "值非法"));
        check("manager_unknown_reference_type_remains_unverified", () => Assert(ModelChecks.Evaluate(Property("Reall"), new[] { Read("3") }).State == "类型待核验"));
        check("manager_type_source_is_read_only", () => Assert(!ModelChecks.Evaluate(Property(), new[] { Read(9, "type") }).Editable));
        var catalog = ReferenceCatalog.FromEmbeddedBaseline();
        Func<ModelElementCheck[], ModelSummary> required = rows => ModelChecks.Summarize(catalog, rows, catalog.Slots().Select(s => new RequirementSetting { Slot = s, Mode = s == "阳台" ? "必要" : "不适用" }).ToArray());
        check("manager_missing_required_model_blocks_ready", () => {
            var result = required(new ModelElementCheck[0]); Assert(result.MissingRequired == 1 && !result.Ready);
            Assert(result.Roles.Single(r => r.Slot == "阳台").State == "未识别到");
        });
        check("manager_unclassified_candidate_is_not_declared_absent", () => {
            var result = required(new[] { new ModelElementCheck { Entity = "IfcSlab" } });
            Assert(result.Roles.Single(r => r.Slot == "阳台").State == "待分类");
        });
        check("manager_unconfirmed_scope_is_not_ready", () => Assert(ModelChecks.Summarize(catalog, new ModelElementCheck[0], new RequirementSetting[0]).PendingRequirements == 43));
        check("manager_no_fields_is_not_100_percent", () => Assert(required(new ModelElementCheck[0]).FieldPercent == null));
        check("manager_complete_assigned_sample_has_separate_metrics", () => {
            var result = required(new[] { new ModelElementCheck { Entity = "IfcSlab", Slot = "阳台", Classified = true, Fields = new[] { ModelChecks.Evaluate(Property(), new[] { Read("40.5") }) } } });
            Assert(result.Ready && result.ClassifiedCount == 1 && result.ValidFields == 1 && result.FieldPercent == 100);
        });
        check("manager_nonfinite_and_invalid_boolean_are_rejected", () => Assert(!ModelChecks.ValueIsValid("Real", "NaN") && !ModelChecks.ValueIsValid("Boolean", "2")));
        check("manager_preview_guard_accepts_same_document_snapshot", () => EditGuard.Check("A", "A", "hash", "hash"));
        check("manager_preview_guard_rejects_changed_value", () => { bool rejected = false; try { EditGuard.Check("A", "A", "old", "new"); } catch (InvalidOperationException) { rejected = true; } Assert(rejected); });
        check("manager_preview_guard_rejects_other_document", () => { bool rejected = false; try { EditGuard.Check("A", "B", "hash", "hash"); } catch (InvalidOperationException) { rejected = true; } Assert(rejected); });
        check("manager_preview_guard_requires_evidence", () => { bool rejected = false; try { EditGuard.Check("A", "A", null, null); } catch (InvalidOperationException) { rejected = true; } Assert(rejected); });
    }
}
