using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Rules;

namespace BIMBaoGui.Stage01.Stage03
{
  internal sealed class Stage03ActivationStateDecision
  {
    internal Stage03ActivationStateDecision(bool success, string message)
    {
      Success = success;
      Message = message ?? string.Empty;
    }

    internal bool Success { get; }
    internal string Message { get; }
  }

  internal static class Stage03ActivationStatePolicy
  {
    internal static Stage03ActivationStateDecision Evaluate(
      HbrRuleDatabase database,
      string modelFileType,
      IReadOnlyDictionary<string, bool> projectConditions,
      IEnumerable<string> activatedRuleIds,
      IEnumerable<string> notApplicableRuleIds)
    {
      if (database == null) throw new ArgumentNullException(nameof(database));
      Dictionary<string, bool> conditions = (projectConditions
          ?? new Dictionary<string, bool>())
        .ToDictionary(
          pair => pair.Key,
          pair => pair.Value,
          StringComparer.Ordinal);
      string[] expectedConditionIds = database.Package.Conditions
        .Select(value => value.ConditionId)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      string[] actualConditionIds = conditions.Keys
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      if (!actualConditionIds.SequenceEqual(
        expectedConditionIds,
        StringComparer.Ordinal))
      {
        string[] missing = expectedConditionIds.Except(
          actualConditionIds,
          StringComparer.Ordinal).ToArray();
        string[] unknown = actualConditionIds.Except(
          expectedConditionIds,
          StringComparer.Ordinal).ToArray();
        return Failed(
          "HBRFileContext 的项目条件键与当前规则包 Conditions 不完整一致。"
          + " 缺失：" + string.Join(", ", missing)
          + "；未知：" + string.Join(", ", unknown));
      }
      RuleActivationResult expected = RuleActivationCatalog.FromDatabase(
        database).Compile(modelFileType, conditions);
      string[] activated = Normalize(activatedRuleIds);
      string[] notApplicable = Normalize(notApplicableRuleIds);
      string[] overlap = activated.Intersect(
        notApplicable,
        StringComparer.Ordinal).ToArray();
      if (overlap.Length > 0)
      {
        return Failed(
          "Stage03 激活规则同时出现在 Activated 与 NotApplicable："
          + string.Join(", ", overlap));
      }
      if (!activated.SequenceEqual(expected.Activated, StringComparer.Ordinal)
        || !notApplicable.SequenceEqual(
          expected.NotApplicable,
          StringComparer.Ordinal))
      {
        return Failed(
          "HBRFileContext 的规则激活状态与 model profile 和项目条件重算结果不一致。");
      }
      return new Stage03ActivationStateDecision(true, string.Empty);
    }

    private static string[] Normalize(IEnumerable<string> values)
    {
      return (values ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
    }

    private static Stage03ActivationStateDecision Failed(string message)
    {
      return new Stage03ActivationStateDecision(false, message);
    }
  }
}
