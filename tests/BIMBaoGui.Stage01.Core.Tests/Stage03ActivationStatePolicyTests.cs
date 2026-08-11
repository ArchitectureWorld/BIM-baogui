using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.Stage03;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage03ActivationStatePolicyTests
  {
    [Fact]
    public void Evaluate_AcceptsCompleteCompiledConditionalActivation()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      RuleActivationProjection projection =
        RuleActivationCatalog.FromDatabase(database);
      string enabledCondition = projection.ConditionRules.Keys
        .OrderBy(value => value, StringComparer.Ordinal)
        .First();
      Dictionary<string, bool> conditions = CompleteConditions(database);
      conditions[enabledCondition] = true;
      RuleActivationResult expected = projection.Compile(
        PlanningTargetRequirementPolicy.SiteModel,
        conditions);

      Stage03ActivationStateDecision decision =
        Stage03ActivationStatePolicy.Evaluate(
          database,
          PlanningTargetRequirementPolicy.SiteModel,
          conditions,
          expected.Activated,
          expected.NotApplicable);

      Assert.True(decision.Success, decision.Message);
    }

    [Fact]
    public void Evaluate_AcceptsSiteContextWithoutNonActivationConditions()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      RuleActivationProjection projection =
        RuleActivationCatalog.FromDatabase(database);
      Dictionary<string, bool> conditions = database.Package.Conditions
        .Where(value => !string.IsNullOrWhiteSpace(value.ActivationRuleId))
        .ToDictionary(
          value => value.ConditionId,
          value => false,
          StringComparer.Ordinal);
      RuleActivationResult expected = projection.Compile(
        PlanningTargetRequirementPolicy.SiteModel,
        conditions);

      Stage03ActivationStateDecision decision =
        Stage03ActivationStatePolicy.Evaluate(
          database,
          PlanningTargetRequirementPolicy.SiteModel,
          conditions,
          expected.Activated,
          expected.NotApplicable);

      Assert.True(decision.Success, decision.Message);
    }

    [Fact]
    public void Evaluate_RejectsMissingEnabledConditionalRule()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      RuleActivationProjection projection =
        RuleActivationCatalog.FromDatabase(database);
      string enabledCondition = projection.ConditionRules.Keys
        .OrderBy(value => value, StringComparer.Ordinal)
        .First();
      Dictionary<string, bool> conditions = CompleteConditions(database);
      conditions[enabledCondition] = true;
      RuleActivationResult expected = projection.Compile(
        PlanningTargetRequirementPolicy.SiteModel,
        conditions);
      string enabledRule = projection.ConditionRules[enabledCondition];

      Stage03ActivationStateDecision decision =
        Stage03ActivationStatePolicy.Evaluate(
          database,
          PlanningTargetRequirementPolicy.SiteModel,
          conditions,
          expected.Activated.Where(value => !string.Equals(
            value,
            enabledRule,
            StringComparison.Ordinal)),
          expected.NotApplicable);

      Assert.False(decision.Success);
      Assert.NotEmpty(decision.Message);
    }

    [Fact]
    public void Evaluate_RejectsActivatedAndNotApplicableOverlap()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      Dictionary<string, bool> conditions = CompleteConditions(database);
      RuleActivationResult expected = RuleActivationCatalog.FromDatabase(
        database).Compile(
          PlanningTargetRequirementPolicy.SiteModel,
          conditions);
      string overlap = expected.Activated.First();

      Stage03ActivationStateDecision decision =
        Stage03ActivationStatePolicy.Evaluate(
          database,
          PlanningTargetRequirementPolicy.SiteModel,
          conditions,
          expected.Activated,
          expected.NotApplicable.Concat(new[] { overlap }));

      Assert.False(decision.Success);
      Assert.NotEmpty(decision.Message);
    }

    [Fact]
    public void Evaluate_RejectsMissingActivationProjectCondition()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      RuleActivationProjection projection =
        RuleActivationCatalog.FromDatabase(database);
      Dictionary<string, bool> conditions = CompleteConditions(database);
      string missingCondition = projection.ConditionRules.Keys
        .OrderBy(value => value, StringComparer.Ordinal)
        .First();
      conditions.Remove(missingCondition);
      RuleActivationResult expected = projection.Compile(
        PlanningTargetRequirementPolicy.SiteModel,
        conditions);

      Stage03ActivationStateDecision decision =
        Stage03ActivationStatePolicy.Evaluate(
          database,
          PlanningTargetRequirementPolicy.SiteModel,
          conditions,
          expected.Activated,
          expected.NotApplicable);

      Assert.False(decision.Success);
      Assert.NotEmpty(decision.Message);
    }

    [Fact]
    public void Evaluate_RejectsUnknownProjectCondition()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      RuleActivationProjection projection =
        RuleActivationCatalog.FromDatabase(database);
      Dictionary<string, bool> conditions = CompleteConditions(database);
      conditions.Add("forged.condition", true);
      RuleActivationResult expected = projection.Compile(
        PlanningTargetRequirementPolicy.SiteModel,
        conditions);

      Stage03ActivationStateDecision decision =
        Stage03ActivationStatePolicy.Evaluate(
          database,
          PlanningTargetRequirementPolicy.SiteModel,
          conditions,
          expected.Activated,
          expected.NotApplicable);

      Assert.False(decision.Success);
      Assert.NotEmpty(decision.Message);
    }

    private static Dictionary<string, bool> CompleteConditions(
      HbrRuleDatabase database)
    {
      return database.Package.Conditions.ToDictionary(
        value => value.ConditionId,
        value => false,
        StringComparer.Ordinal);
    }
  }
}
