using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.Stage01.TaskPlanning
{
  public sealed class HBRTaskPlan
  {
    public HBRTaskPlan(
      string schemaVersion,
      string fileContextHash,
      string modelFileType,
      string skeletonPath,
      IEnumerable<HBRTaskPlanItem> activeTasks,
      IEnumerable<HBRTaskPlanItem> notApplicableTasks,
      string taskPlanHash)
    {
      SchemaVersion = schemaVersion ?? string.Empty;
      FileContextHash = fileContextHash ?? string.Empty;
      ModelFileType = modelFileType ?? string.Empty;
      SkeletonPath = skeletonPath ?? string.Empty;
      ActiveTasks = Normalize(activeTasks);
      NotApplicableTasks = Normalize(notApplicableTasks);
      RequiredObjects = ActiveTasks
        .Where(item => item.Requirement == HBRTaskRequirement.Required && !item.SkeletonTask)
        .Select(item => item.ObjectCode)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
      ConditionalObjects = ActiveTasks
        .Where(item => item.Requirement == HBRTaskRequirement.Conditional)
        .Select(item => item.ObjectCode)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
      NotApplicableObjects = NotApplicableTasks
        .Select(item => item.ObjectCode)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
      BuildSequence = ActiveTasks.Select(item => item.TaskId).ToArray();
      SkeletonTasks = ActiveTasks.Where(item => item.SkeletonTask).Select(item => item.TaskId).ToArray();
      AttributeRequirements = ActiveTasks.SelectMany(item => item.AttributeRequirements).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
      GeometryChecks = ActiveTasks.SelectMany(item => item.GeometryChecks).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
      PropertyChecks = ActiveTasks.SelectMany(item => item.PropertyChecks).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
      TargetComparisons = ActiveTasks.SelectMany(item => item.TargetComparisons).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
      TaskPlanHash = taskPlanHash ?? string.Empty;
    }

    public string SchemaVersion { get; }
    public string FileContextHash { get; }
    public string ModelFileType { get; }
    public string SkeletonPath { get; }
    public IReadOnlyList<HBRTaskPlanItem> ActiveTasks { get; }
    public IReadOnlyList<HBRTaskPlanItem> NotApplicableTasks { get; }
    public IReadOnlyList<string> RequiredObjects { get; }
    public IReadOnlyList<string> ConditionalObjects { get; }
    public IReadOnlyList<string> NotApplicableObjects { get; }
    public IReadOnlyList<string> AttributeRequirements { get; }
    public IReadOnlyList<string> BuildSequence { get; }
    public IReadOnlyList<string> SkeletonTasks { get; }
    public IReadOnlyList<string> GeometryChecks { get; }
    public IReadOnlyList<string> PropertyChecks { get; }
    public IReadOnlyList<string> TargetComparisons { get; }
    public string TaskPlanHash { get; }

    public bool IsValid => !string.IsNullOrWhiteSpace(FileContextHash)
      && !string.IsNullOrWhiteSpace(ModelFileType)
      && !string.IsNullOrWhiteSpace(TaskPlanHash)
      && ActiveTasks.Count > 0;

    public bool RequiresRecompile(string currentFileContextHash)
    {
      return !string.Equals(FileContextHash, currentFileContextHash ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    internal HBRTaskPlan WithHash(string hash)
    {
      return new HBRTaskPlan(
        SchemaVersion,
        FileContextHash,
        ModelFileType,
        SkeletonPath,
        ActiveTasks,
        NotApplicableTasks,
        hash);
    }

    public override string ToString()
    {
      return "HBR_TaskPlan / " + ModelFileType + " / " + ActiveTasks.Count + " 项激活任务";
    }

    private static IReadOnlyList<HBRTaskPlanItem> Normalize(IEnumerable<HBRTaskPlanItem> source)
    {
      return (source ?? Array.Empty<HBRTaskPlanItem>())
        .Where(item => item != null)
        .GroupBy(item => item.TaskId, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(item => item.Sequence)
        .ThenBy(item => item.TaskId, StringComparer.Ordinal)
        .ToArray();
    }
  }
}
