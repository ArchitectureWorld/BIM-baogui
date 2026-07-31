using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.TaskPlanning;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class TaskPlanCompilerTests
  {
    [Fact]
    public void SiteContext_CompilesRequiredAndSelectedConditionalTasks()
    {
      HBRFileContext context = BuildContext(
        PlanningTargetRequirementPolicy.SiteModel,
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
          ["site.green"] = true,
          ["site.fire_lane"] = false
        });

      TaskPlanCompilationResult result = TaskPlanCompiler.Compile(context);

      Assert.True(result.Success, string.Join("; ", result.Blockers));
      Assert.Equal("总平", result.Plan.SkeletonPath);
      Assert.Contains(result.Plan.ActiveTasks, task => task.TaskId == "SITE.TOTAL_LAND");
      Assert.Contains(result.Plan.ActiveTasks, task => task.TaskId == "SITE.GREEN");
      Assert.DoesNotContain(result.Plan.ActiveTasks, task => task.TaskId == "SITE.FIRE_LANE");
      Assert.Contains(result.Plan.NotApplicableTasks, task => task.TaskId == "SITE.FIRE_LANE");
    }

    [Theory]
    [InlineData(PlanningTargetRequirementPolicy.AboveGroundModel, "单体建筑—地上", "ABOVE.BODY")]
    [InlineData(PlanningTargetRequirementPolicy.UndergroundModel, "单体建筑—地下", "UNDERGROUND.BODY")]
    public void BuildingContext_RoutesToDedicatedTaskPath(string modelFileType, string path, string requiredTask)
    {
      TaskPlanCompilationResult result = TaskPlanCompiler.Compile(BuildContext(modelFileType, new Dictionary<string, bool>()));

      Assert.True(result.Success, string.Join("; ", result.Blockers));
      Assert.Equal(path, result.Plan.SkeletonPath);
      Assert.Contains(result.Plan.ActiveTasks, task => task.TaskId == requiredTask);
      Assert.DoesNotContain(result.Plan.ActiveTasks, task => task.TaskId.StartsWith("SITE.", StringComparison.Ordinal));
    }

    [Fact]
    public void ConditionChange_ChangesTaskPlanHash()
    {
      HBRTaskPlan first = TaskPlanCompiler.Compile(BuildContext(
        PlanningTargetRequirementPolicy.SiteModel,
        new Dictionary<string, bool> { ["site.green"] = false })).Plan;
      HBRTaskPlan second = TaskPlanCompiler.Compile(BuildContext(
        PlanningTargetRequirementPolicy.SiteModel,
        new Dictionary<string, bool> { ["site.green"] = true })).Plan;

      Assert.NotEqual(first.TaskPlanHash, second.TaskPlanHash);
      Assert.NotEqual(first.FileContextHash, second.FileContextHash);
    }

    [Fact]
    public void UninitializedContext_IsBlocked()
    {
      HBRFileContext ready = BuildContext(PlanningTargetRequirementPolicy.SiteModel, new Dictionary<string, bool>());
      var provisional = new HBRFileContext(
        ready.SchemaVersion,
        ready.WorkflowVersion,
        ready.FileGuid,
        ready.RevitDocumentFingerprint,
        ready.RevitDocumentTitle,
        ready.ProjectNumber,
        ready.ProjectName,
        ready.SubitemCode,
        ready.SubitemName,
        ready.ModelFileType,
        ready.ModelScope,
        ready.SpatialReference,
        ready.PlanningTargets.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        ready.ProjectConditions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        ready.ActivatedRuleIds,
        ready.NotApplicableRuleIds,
        false,
        ready.RulePackVersion,
        ready.SourcePayloadHash,
        string.Empty);
      HBRFileContext uninitialized = provisional.WithHash(HBRFileContextCanonicalizer.ComputeHash(provisional));

      TaskPlanCompilationResult result = TaskPlanCompiler.Compile(uninitialized);

      Assert.False(result.Success);
      Assert.Contains(result.Blockers, message => message.Contains("初始化尚未通过"));
    }

    internal static HBRFileContext BuildContext(string modelFileType, IDictionary<string, bool> conditions)
    {
      var targets = new Dictionary<string, PlanningTargetValue>(StringComparer.Ordinal);
      if (modelFileType == PlanningTargetRequirementPolicy.SiteModel)
      {
        targets[PlanningTargetCatalog.BuildingDensityCode] = Target(PlanningTargetCatalog.BuildingDensityCode, PlanningTargetOperator.LessOrEqual, "30");
        targets[PlanningTargetCatalog.FloorAreaRatioCode] = Target(PlanningTargetCatalog.FloorAreaRatioCode, PlanningTargetOperator.LessOrEqual, "2.00");
        targets[PlanningTargetCatalog.GreenRateCode] = Target(PlanningTargetCatalog.GreenRateCode, PlanningTargetOperator.GreaterOrEqual, "35");
      }
      RuleActivationResult activation = RuleActivationCatalog.Compile(modelFileType, conditions);
      var provisional = new HBRFileContext(
        HBRContextVersions.FileContextSchema,
        "0.5.0",
        "file-guid",
        "document-fingerprint",
        "测试模型.rvt",
        "P-001",
        "测试项目",
        "S-01",
        "测试子项",
        modelFileType,
        "报规模型",
        new HBRSpatialReference("CGCS2000", "1985国家高程基准", 1m, 2m, 3m, 0m, "m", "m²", "°"),
        targets,
        conditions,
        activation.Activated,
        activation.NotApplicable,
        true,
        HBRContextVersions.RulePack,
        "payload-hash",
        string.Empty);
      return provisional.WithHash(HBRFileContextCanonicalizer.ComputeHash(provisional));
    }

    private static PlanningTargetValue Target(string metricCode, PlanningTargetOperator op, string value)
    {
      PlanningTargetDefinition definition = PlanningTargetCatalog.Get(metricCode);
      Assert.True(PlanningTargetValue.TryCreate(metricCode, op, value, null, definition.Unit, "项目初始化", out PlanningTargetValue target, out string error), error);
      return target;
    }
  }
}
