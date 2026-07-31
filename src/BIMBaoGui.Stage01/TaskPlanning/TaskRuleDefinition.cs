using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.Stage01.TaskPlanning
{
  public enum HBRTaskRequirement
  {
    Required,
    Conditional
  }

  public sealed class HBRTaskPlanItem
  {
    public HBRTaskPlanItem(
      string taskId,
      string name,
      string objectCode,
      HBRTaskRequirement requirement,
      string conditionKey,
      int sequence,
      bool skeletonTask,
      IEnumerable<string> attributeRequirements,
      IEnumerable<string> dependencies,
      IEnumerable<string> geometryChecks,
      IEnumerable<string> propertyChecks,
      IEnumerable<string> targetComparisons)
    {
      TaskId = taskId ?? string.Empty;
      Name = name ?? string.Empty;
      ObjectCode = objectCode ?? string.Empty;
      Requirement = requirement;
      ConditionKey = conditionKey ?? string.Empty;
      Sequence = sequence;
      SkeletonTask = skeletonTask;
      AttributeRequirements = Normalize(attributeRequirements);
      Dependencies = Normalize(dependencies);
      GeometryChecks = Normalize(geometryChecks);
      PropertyChecks = Normalize(propertyChecks);
      TargetComparisons = Normalize(targetComparisons);
    }

    public string TaskId { get; }
    public string Name { get; }
    public string ObjectCode { get; }
    public HBRTaskRequirement Requirement { get; }
    public string ConditionKey { get; }
    public int Sequence { get; }
    public bool SkeletonTask { get; }
    public IReadOnlyList<string> AttributeRequirements { get; }
    public IReadOnlyList<string> Dependencies { get; }
    public IReadOnlyList<string> GeometryChecks { get; }
    public IReadOnlyList<string> PropertyChecks { get; }
    public IReadOnlyList<string> TargetComparisons { get; }

    private static IReadOnlyList<string> Normalize(IEnumerable<string> source)
    {
      return (source ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
    }

    public override string ToString()
    {
      return TaskId + " / " + Name;
    }
  }

  internal sealed class TaskRuleDefinition
  {
    public TaskRuleDefinition(
      string modelFileType,
      HBRTaskPlanItem item)
    {
      ModelFileType = modelFileType ?? string.Empty;
      Item = item ?? throw new ArgumentNullException(nameof(item));
    }

    public string ModelFileType { get; }
    public HBRTaskPlanItem Item { get; }
  }
}
