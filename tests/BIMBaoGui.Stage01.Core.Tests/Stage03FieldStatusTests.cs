using System.Collections.Generic;
using BIMBaoGui.Stage01.Stage03;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage03FieldStatusTests
  {
    [Fact]
    public void Not_evaluated_is_distinct_from_pass_for_future_ifc_phases()
    {
      Assert.NotEqual(
        Stage03FieldStatus.Pass,
        Stage03FieldStatus.NotEvaluated);
      Assert.Equal(
        "NOT_EVALUATED",
        Stage03FieldStatusCodes.ToCode(
          Stage03FieldStatus.NotEvaluated));

      var field = new Stage03FieldResult();
      Assert.Equal(
        Stage03FieldStatus.NotEvaluated,
        field.RawIfcStatus);
      Assert.Equal(
        Stage03FieldStatus.NotEvaluated,
        field.FinalIfcStatus);
    }

    [Theory]
    [InlineData("CANONICAL_SPATIAL_ZONE_RECORD")]
    [InlineData("USER_SELECTED_EXPORTABLE_GENERIC_MODEL")]
    public void Owner_strategy_marks_unsupported_records_without_export_guid(
      string strategy)
    {
      Stage03IfcOwnerStrategyDecision decision =
        Stage03IfcOwnerStrategyPolicy.Evaluate(strategy);

      Assert.False(decision.Implemented);
      Assert.False(decision.UsesExportGuid);
      Assert.Equal(Stage03FieldStatus.RuleNotImplemented, decision.Status);
    }

    [Fact]
    public void Owner_strategy_allows_export_guid_only_for_by_export_guid()
    {
      Stage03IfcOwnerStrategyDecision decision =
        Stage03IfcOwnerStrategyPolicy.Evaluate("BY_EXPORT_GUID");

      Assert.True(decision.Implemented);
      Assert.True(decision.UsesExportGuid);
      Assert.Equal(Stage03FieldStatus.Pass, decision.Status);
    }

    [Fact]
    public void Applicability_missing_condition_is_fail_closed()
    {
      Stage03RequirementApplicabilityDecision decision =
        Stage03RequirementApplicabilityPolicy.Evaluate(
          "CONDITIONAL",
          "condition.missing",
          new Dictionary<string, bool>());

      Assert.True(decision.Active);
      Assert.Equal("UNKNOWN", decision.Applicability);
      Assert.Equal(
        Stage03FieldStatus.UnclassifiedRequirement,
        decision.FailureStatus);
    }

    [Fact]
    public void Overall_status_keeps_rule_not_implemented_over_missing_parameter()
    {
      Stage03FieldStatus status = Stage03FieldStatusPolicy.Resolve(
        true,
        "APPLICABLE",
        Stage03FieldStatus.Pass,
        Stage03FieldStatus.MissingParameter,
        Stage03FieldStatus.RuleNotImplemented,
        "REQUIRED");

      Assert.Equal(Stage03FieldStatus.RuleNotImplemented, status);
    }

    [Fact]
    public void Overall_status_never_passes_unknown_applicability()
    {
      Stage03FieldStatus status = Stage03FieldStatusPolicy.Resolve(
        true,
        "UNKNOWN",
        Stage03FieldStatus.Pass,
        Stage03FieldStatus.Pass,
        Stage03FieldStatus.Pass,
        "CONDITIONAL");

      Assert.Equal(Stage03FieldStatus.UnclassifiedRequirement, status);
    }

    [Fact]
    public void Overall_status_preserves_normal_missing_parameter()
    {
      Stage03FieldStatus status = Stage03FieldStatusPolicy.Resolve(
        true,
        "APPLICABLE",
        Stage03FieldStatus.Pass,
        Stage03FieldStatus.MissingParameter,
        Stage03FieldStatus.Pass,
        "REQUIRED");

      Assert.Equal(Stage03FieldStatus.MissingParameter, status);
    }
  }
}
