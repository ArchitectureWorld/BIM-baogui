using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIMBaoGui.Stage01.Rules;

namespace BIMBaoGui.Stage01.TaskPlanning
{
  internal static class TaskRuleCatalog
  {
    private static readonly Lazy<IReadOnlyList<TaskRuleDefinition>> LazyRules =
      new Lazy<IReadOnlyList<TaskRuleDefinition>>(() =>
        FromDatabase(HbrRuleDatabase.Current));

    internal static IReadOnlyList<TaskRuleDefinition> FromDatabase(
      HbrRuleDatabase database)
    {
      if (database == null) throw new ArgumentNullException(nameof(database));
      return database.Package.Tasks
        .Select(MapRule)
        .ToArray();
    }

    public static IReadOnlyList<TaskRuleDefinition> ForModelType(
      string modelFileType)
    {
      return LazyRules.Value
        .Where(rule => string.Equals(
          rule.ModelFileType,
          modelFileType,
          StringComparison.Ordinal))
        .OrderBy(rule => rule.Item.Sequence)
        .ThenBy(rule => rule.Item.TaskId, StringComparer.Ordinal)
        .ToArray();
    }

    private static TaskRuleDefinition MapRule(HbrTaskRule source)
    {
      if (source == null)
        throw new InvalidDataException("HBR task rule is null.");
      HBRTaskRequirement requirement;
      switch (source.Requirement)
      {
        case "REQUIRED":
          requirement = HBRTaskRequirement.Required;
          break;
        case "CONDITIONAL":
          if (string.IsNullOrWhiteSpace(source.ConditionId))
            throw new InvalidDataException(
              "Conditional HBR task is missing conditionId: "
              + source.TaskId);
          requirement = HBRTaskRequirement.Conditional;
          break;
        default:
          throw new InvalidDataException(
            "Unknown HBR task requirement: "
            + source.Requirement
            + " for "
            + source.TaskId);
      }

      return new TaskRuleDefinition(
        source.ModelFileType,
        new HBRTaskPlanItem(
          source.TaskId,
          source.Name,
          source.ObjectCode,
          requirement,
          source.ConditionId,
          source.Sequence,
          source.SkeletonTask,
          source.AttributeRequirements,
          source.Dependencies,
          source.GeometryChecks,
          source.PropertyChecks,
          source.TargetComparisons));
    }
  }
}
