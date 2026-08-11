using System;
using System.Collections.Generic;
using BIMBaoGui.Stage01.Stage02;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage02RequirementDecisionPolicyTests
  {
    [Theory]
    [InlineData("REQUIRED")]
    [InlineData("OPTIONAL")]
    [InlineData("UNCLASSIFIED")]
    public void Ordinary_requirement_is_applicable(string level)
    {
      Stage02RequirementDecision decision = Resolve(
        level,
        string.Empty,
        new Dictionary<string, bool>());

      Assert.True(decision.Success);
      Assert.Equal("APPLICABLE", decision.Applicability);
      Assert.Equal(string.Empty, decision.ValueActionOverride);
      Assert.Empty(decision.Blockers);
    }

    [Fact]
    public void Explicit_not_applicable_never_writes()
    {
      Stage02RequirementDecision decision = Resolve(
        "NOT_APPLICABLE",
        string.Empty,
        new Dictionary<string, bool>());

      Assert.True(decision.Success);
      Assert.Equal("NOT_APPLICABLE", decision.Applicability);
      Assert.Equal("NO_WRITE", decision.ValueActionOverride);
    }

    [Theory]
    [InlineData(true, "APPLICABLE", "")]
    [InlineData(false, "NOT_APPLICABLE", "NO_WRITE")]
    public void Conditional_true_and_false_are_deterministic(
      bool conditionValue,
      string expectedApplicability,
      string expectedAction)
    {
      Stage02RequirementDecision decision = Resolve(
        "CONDITIONAL",
        "building.roof",
        new Dictionary<string, bool>
        {
          ["building.roof"] = conditionValue
        });

      Assert.True(decision.Success);
      Assert.Equal(expectedApplicability, decision.Applicability);
      Assert.Equal(expectedAction, decision.ValueActionOverride);
    }

    [Fact]
    public void Missing_conditional_state_is_unknown_and_blocked_per_property()
    {
      Stage02RequirementDecision decision = Resolve(
        "CONDITIONAL",
        "building.roof",
        new Dictionary<string, bool>());

      Assert.True(decision.Success);
      Assert.Equal("UNKNOWN", decision.Applicability);
      Assert.Equal("NO_WRITE", decision.ValueActionOverride);
      Stage02Blocker blocker = Assert.Single(decision.Blockers);
      Assert.Equal(Stage02Codes.ConditionStateMissing, blocker.Code);
    }

    [Theory]
    [InlineData("CONDITIONAL", "")]
    [InlineData("CONDITIONAL", "unknown.condition")]
    [InlineData("OPTIONAL", "building.roof")]
    [InlineData("INVALID", "")]
    public void Invalid_requirement_contract_fails_closed(
      string level,
      string conditionId)
    {
      Stage02RequirementDecision decision = Resolve(
        level,
        conditionId,
        new Dictionary<string, bool>());

      Assert.False(decision.Success);
      Assert.NotEmpty(decision.ErrorCode);
    }

    private static Stage02RequirementDecision Resolve(
      string level,
      string conditionId,
      IReadOnlyDictionary<string, bool> values)
    {
      return Stage02RequirementDecisionPolicy.Resolve(
        "property-1",
        level,
        conditionId,
        new[] { "building.roof" },
        values);
    }
  }
}
