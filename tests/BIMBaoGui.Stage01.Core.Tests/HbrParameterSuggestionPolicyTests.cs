using System;
using BIMBaoGui.Stage01.Revit.Parameters;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HbrParameterSuggestionPolicyTests
  {
    [Fact]
    public void UsesNonBlankCanonicalGuidValueFirst()
    {
      HbrParameterSuggestionDecision decision = Resolve(
        Candidate(HbrSuggestionSources.CanonicalGuid, "guid", "canonical"),
        Candidate(HbrSuggestionSources.LegacyName, "legacy", "legacy-value"),
        Candidate(HbrSuggestionSources.RuleAlias, "alias", "alias-value"),
        Candidate(HbrSuggestionSources.Stage01Projection, "stage01", "projected"));

      Assert.Equal("canonical", decision.SuggestedValue);
      Assert.Equal(HbrSuggestionSources.CanonicalGuid, decision.ValueSource);
      Assert.Empty(decision.Blockers);
    }

    [Fact]
    public void FallsBackThroughLegacyAliasStage01AndBlank()
    {
      Assert.Equal(
        HbrSuggestionSources.LegacyName,
        Resolve(
          Candidate(HbrSuggestionSources.CanonicalGuid, "guid", ""),
          Candidate(HbrSuggestionSources.LegacyName, "legacy", "legacy-value"))
          .ValueSource);
      Assert.Equal(
        HbrSuggestionSources.RuleAlias,
        Resolve(
          Candidate(HbrSuggestionSources.LegacyName, "legacy", ""),
          Candidate(HbrSuggestionSources.RuleAlias, "alias", "alias-value"))
          .ValueSource);
      Assert.Equal(
        HbrSuggestionSources.Stage01Projection,
        Resolve(
          Candidate(HbrSuggestionSources.RuleAlias, "alias", ""),
          Candidate(HbrSuggestionSources.Stage01Projection, "stage01", "projected"))
          .ValueSource);

      HbrParameterSuggestionDecision blank = Resolve();
      Assert.Equal(HbrSuggestionSources.Blank, blank.ValueSource);
      Assert.Equal(string.Empty, blank.SuggestedValue);
    }

    [Fact]
    public void BlocksMultipleParametersWithSameName()
    {
      HbrParameterSuggestionDecision decision = Resolve(
        new HbrParameterValueCandidate(
          HbrSuggestionSources.LegacyName,
          "legacy",
          "value",
          2));

      Assert.Contains(
        decision.Blockers,
        x => x.Code == HbrSuggestionBlockerCodes.AmbiguousNamedParameter);
    }

    [Fact]
    public void BlocksMultipleNonBlankRuleAliases()
    {
      HbrParameterSuggestionDecision decision = Resolve(
        Candidate(HbrSuggestionSources.RuleAlias, "alias-a", "a"),
        Candidate(HbrSuggestionSources.RuleAlias, "alias-b", "b"));

      Assert.Contains(
        decision.Blockers,
        x => x.Code == HbrSuggestionBlockerCodes.MultipleAliasValues);
    }

    [Fact]
    public void BlocksMultipleNonBlankLegacyNames()
    {
      HbrParameterSuggestionDecision decision = Resolve(
        Candidate(HbrSuggestionSources.LegacyName, "legacy-a", "a"),
        Candidate(HbrSuggestionSources.LegacyName, "legacy-b", "b"));

      Assert.Contains(
        decision.Blockers,
        x => x.Code == HbrSuggestionBlockerCodes.MultipleLegacyValues);
    }

    [Fact]
    public void Canonical_value_short_circuits_lower_priority_ambiguity()
    {
      HbrParameterSuggestionDecision decision = Resolve(
        Candidate(HbrSuggestionSources.CanonicalGuid, "guid", "canonical"),
        new HbrParameterValueCandidate(
          HbrSuggestionSources.LegacyName,
          "legacy-duplicate",
          "legacy",
          2),
        Candidate(HbrSuggestionSources.RuleAlias, "alias-a", "a"),
        Candidate(HbrSuggestionSources.RuleAlias, "alias-b", "b"));

      Assert.Equal("canonical", decision.SuggestedValue);
      Assert.Equal(HbrSuggestionSources.CanonicalGuid, decision.ValueSource);
      Assert.Empty(decision.Blockers);
    }

    [Fact]
    public void Legacy_value_short_circuits_alias_ambiguity()
    {
      HbrParameterSuggestionDecision decision = Resolve(
        Candidate(HbrSuggestionSources.CanonicalGuid, "guid", ""),
        Candidate(HbrSuggestionSources.LegacyName, "legacy", "legacy-value"),
        Candidate(HbrSuggestionSources.RuleAlias, "alias-a", "a"),
        Candidate(HbrSuggestionSources.RuleAlias, "alias-b", "b"));

      Assert.Equal("legacy-value", decision.SuggestedValue);
      Assert.Equal(HbrSuggestionSources.LegacyName, decision.ValueSource);
      Assert.Empty(decision.Blockers);
    }

    [Theory]
    [InlineData("String", "Text", "Double", "Length")]
    [InlineData("Double", "Number", "Double", "Length")]
    [InlineData("Integer", "Integer", "Integer", "YesNo")]
    public void Incompatible_named_source_types_are_rejected(
      string sourceStorage,
      string sourceParameterType,
      string targetStorage,
      string targetParameterType)
    {
      HbrNamedParameterCompatibilityDecision compatibility =
        HbrNamedParameterCompatibilityPolicy.Evaluate(
          targetStorage,
          targetParameterType,
          sourceStorage,
          sourceParameterType);

      Assert.False(compatibility.Compatible);
      Assert.False(compatibility.SourceAlreadyUsesInternalUnits);
    }

    [Theory]
    [InlineData("Double", "Length")]
    [InlineData("Integer", "YesNo")]
    public void Exact_named_source_semantics_are_internal_and_reusable(
      string storageType,
      string parameterType)
    {
      HbrNamedParameterCompatibilityDecision compatibility =
        HbrNamedParameterCompatibilityPolicy.Evaluate(
          storageType,
          parameterType,
          storageType,
          parameterType);

      Assert.True(compatibility.Compatible);
      Assert.True(compatibility.SourceAlreadyUsesInternalUnits);
    }

    [Fact]
    public void String_height_cannot_feed_length_target()
    {
      HbrParameterSuggestionDecision decision = Resolve(TypedCandidate(
        HbrSuggestionSources.RuleAlias,
        "高度",
        "12",
        "String",
        "Text",
        compatible: false,
        sourceAlreadyUsesInternalUnits: false));

      Assert.Equal(HbrSuggestionSources.Blank, decision.ValueSource);
      Assert.False(decision.SourceAlreadyUsesInternalUnits);
      Assert.Contains(
        decision.Blockers,
        x => x.Code == HbrSuggestionBlockerCodes.SourceTypeMismatch);
    }

    [Theory]
    [InlineData("Double", "Length", "12")]
    [InlineData("Integer", "YesNo", "1")]
    public void Compatible_named_source_preserves_internal_value_contract(
      string storageType,
      string parameterType,
      string value)
    {
      HbrParameterSuggestionDecision decision = Resolve(TypedCandidate(
        HbrSuggestionSources.RuleAlias,
        "typed-alias",
        value,
        storageType,
        parameterType,
        compatible: true,
        sourceAlreadyUsesInternalUnits: true));

      Assert.Equal(value, decision.SuggestedValue);
      Assert.True(decision.SourceAlreadyUsesInternalUnits);
      Assert.Empty(decision.Blockers);
    }

    [Fact]
    public void Live_nonblank_alias_shared_by_rule_properties_is_blocked()
    {
      HbrParameterSuggestionDecision decision = Resolve(TypedCandidate(
        HbrSuggestionSources.RuleAlias,
        "投影面积",
        "120",
        "Double",
        "Area",
        compatible: true,
        sourceAlreadyUsesInternalUnits: true,
        ruleAliasPropertyCount: 7));

      Assert.Equal(HbrSuggestionSources.Blank, decision.ValueSource);
      Assert.Contains(
        decision.Blockers,
        x => x.Code == HbrSuggestionBlockerCodes.AmbiguousSuggestionAliasRule);
    }

    [Fact]
    public void Higher_priority_value_ignores_lower_rule_alias_ambiguity()
    {
      HbrParameterSuggestionDecision decision = Resolve(
        Candidate(HbrSuggestionSources.CanonicalGuid, "guid", "canonical"),
        TypedCandidate(
          HbrSuggestionSources.RuleAlias,
          "名称",
          "broadcast",
          "String",
          "Text",
          compatible: true,
          sourceAlreadyUsesInternalUnits: true,
          ruleAliasPropertyCount: 16));

      Assert.Equal("canonical", decision.SuggestedValue);
      Assert.Empty(decision.Blockers);
    }

    private static HbrParameterSuggestionDecision Resolve(
      params HbrParameterValueCandidate[] candidates)
    {
      return HbrParameterSuggestionPolicy.Resolve(candidates);
    }

    private static HbrParameterValueCandidate Candidate(
      string source,
      string identity,
      string value)
    {
      return new HbrParameterValueCandidate(source, identity, value, 1);
    }

    private static HbrParameterValueCandidate TypedCandidate(
      string source,
      string identity,
      string value,
      string sourceStorageType,
      string sourceParameterType,
      bool compatible,
      bool sourceAlreadyUsesInternalUnits,
      int ruleAliasPropertyCount = 1)
    {
      return new HbrParameterValueCandidate(
        source,
        identity,
        value,
        1,
        sourceStorageType,
        sourceParameterType,
        "11111111-1111-1111-1111-111111111111",
        compatible,
        sourceAlreadyUsesInternalUnits,
        ruleAliasPropertyCount);
    }
  }
}
