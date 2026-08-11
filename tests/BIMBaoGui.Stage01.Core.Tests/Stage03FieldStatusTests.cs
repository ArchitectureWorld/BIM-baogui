using System.Collections.Generic;
using BIMBaoGui.Stage01.Stage03;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage03FieldStatusTests
  {
    [Theory]
    [InlineData("OPTIONAL")]
    [InlineData("UNCLASSIFIED")]
    public void Optional_absent_carrier_is_not_a_hard_requirement(
      string requirementLevel)
    {
      Assert.False(Stage03CarrierPresencePolicy.IsMissingCarrierRequired(
        0,
        new[] { requirementLevel }));
    }

    [Theory]
    [InlineData("REQUIRED")]
    [InlineData("CONDITIONAL")]
    public void Active_required_field_makes_absent_carrier_a_hard_requirement(
      string requirementLevel)
    {
      Assert.True(Stage03CarrierPresencePolicy.IsMissingCarrierRequired(
        0,
        new[] { requirementLevel }));
    }

    [Fact]
    public void Cardinality_minimum_makes_absent_carrier_a_hard_requirement()
    {
      Assert.True(Stage03CarrierPresencePolicy.IsMissingCarrierRequired(
        1,
        new[] { "OPTIONAL" }));
    }

    [Theory]
    [InlineData("基点坐标 X", "基点坐标X")]
    [InlineData("基点坐标 Y", "基点坐标Y")]
    public void Stage03_ifc_identity_uses_official_canonical_property_name(
      string sourceProperty,
      string expectedProperty)
    {
      Stage03IfcPropertyIdentity identity =
        Stage03IfcPropertyIdentityPolicy.Resolve(
          "IfcProject",
          "Pset_申报信息属性集",
          sourceProperty);

      Assert.Equal("Pset_申报信息属性集", identity.PropertySetName);
      Assert.Equal(expectedProperty, identity.PropertyName);
    }

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
