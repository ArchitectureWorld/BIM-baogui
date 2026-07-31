using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;

namespace BIMBaoGui.Stage01.TaskPlanning
{
  internal sealed class TaskPlanCompilationResult
  {
    public TaskPlanCompilationResult(HBRTaskPlan plan, IEnumerable<string> blockers)
    {
      Plan = plan;
      Blockers = (blockers ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
    }

    public HBRTaskPlan Plan { get; }
    public IReadOnlyList<string> Blockers { get; }
    public bool Success => Plan != null && Blockers.Count == 0;
  }

  internal static class TaskPlanCompiler
  {
    public const string SchemaVersion = "0.5.0";

    public static TaskPlanCompilationResult Compile(HBRFileContext context)
    {
      var blockers = ValidateContext(context);
      if (blockers.Count > 0)
        return new TaskPlanCompilationResult(null, blockers);

      IReadOnlyList<TaskRuleDefinition> rules = TaskRuleCatalog.ForModelType(context.ModelFileType);
      if (rules.Count == 0)
        return new TaskPlanCompilationResult(null, new[] { "当前模型类型没有可用的任务规则：" + context.ModelFileType });

      var active = new List<HBRTaskPlanItem>();
      var notApplicable = new List<HBRTaskPlanItem>();
      foreach (TaskRuleDefinition rule in rules)
      {
        HBRTaskPlanItem item = rule.Item;
        if (item.Requirement == HBRTaskRequirement.Required)
        {
          active.Add(item);
          continue;
        }

        bool enabled = !string.IsNullOrWhiteSpace(item.ConditionKey)
          && context.ProjectConditions.TryGetValue(item.ConditionKey, out bool selected)
          && selected;
        if (enabled) active.Add(item);
        else notApplicable.Add(item);
      }

      string skeletonPath = ResolveSkeletonPath(context.ModelFileType);
      var provisional = new HBRTaskPlan(
        SchemaVersion,
        context.FileContextHash,
        context.ModelFileType,
        skeletonPath,
        active,
        notApplicable,
        string.Empty);
      return new TaskPlanCompilationResult(
        provisional.WithHash(HBRTaskPlanCanonicalizer.ComputeHash(provisional)),
        Array.Empty<string>());
    }

    private static IReadOnlyList<string> ValidateContext(HBRFileContext context)
    {
      var blockers = new List<string>();
      if (context == null)
      {
        blockers.Add("请连接 01 文件初始化的“文件上下文”输出。");
        return blockers;
      }
      if (!context.InitializationPassed)
        blockers.Add("文件初始化尚未通过，请先完成 01 文件初始化的写入与回读。");
      if (string.IsNullOrWhiteSpace(context.FileGuid))
        blockers.Add("文件上下文缺少报规文件唯一 ID。");
      if (string.IsNullOrWhiteSpace(context.RevitDocumentFingerprint))
        blockers.Add("文件上下文缺少 Revit 文档指纹。");
      if (string.IsNullOrWhiteSpace(context.FileContextHash))
        blockers.Add("文件上下文缺少哈希值。");
      else
      {
        string expected = HBRFileContextCanonicalizer.ComputeHash(context);
        if (!string.Equals(expected, context.FileContextHash, StringComparison.OrdinalIgnoreCase))
          blockers.Add("文件上下文哈希无效，请重新运行 01 文件初始化。");
      }
      if (!string.Equals(context.SchemaVersion, HBRFileContextFactory.SchemaVersion, StringComparison.Ordinal))
        blockers.Add("文件上下文版本不兼容：" + context.SchemaVersion + "，当前需要 " + HBRFileContextFactory.SchemaVersion + "。");
      if (!string.Equals(context.RulePackVersion, HBRFileContextFactory.RulePackVersion, StringComparison.Ordinal))
        blockers.Add("规则包版本不兼容：" + context.RulePackVersion + "，当前需要 " + HBRFileContextFactory.RulePackVersion + "。");
      if (TaskRuleCatalog.ForModelType(context.ModelFileType).Count == 0)
        blockers.Add("不支持的模型文件类型：" + context.ModelFileType);
      return blockers;
    }

    private static string ResolveSkeletonPath(string modelFileType)
    {
      if (string.Equals(modelFileType, PlanningTargetRequirementPolicy.SiteModel, StringComparison.Ordinal))
        return "总平";
      if (string.Equals(modelFileType, PlanningTargetRequirementPolicy.AboveGroundModel, StringComparison.Ordinal))
        return "单体建筑—地上";
      if (string.Equals(modelFileType, PlanningTargetRequirementPolicy.UndergroundModel, StringComparison.Ordinal))
        return "单体建筑—地下";
      return string.Empty;
    }
  }
}
