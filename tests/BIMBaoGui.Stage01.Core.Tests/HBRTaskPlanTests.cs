using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.TaskPlanning;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HBRTaskPlanTests
  {
    [Fact]
    public void CanonicalJson_RoundTripsAndVerifiesHash()
    {
      HBRFileContext context = TaskPlanCompilerTests.BuildContext(
        PlanningTargetRequirementPolicy.SiteModel,
        new System.Collections.Generic.Dictionary<string, bool> { ["site.green"] = true });
      HBRTaskPlan source = TaskPlanCompiler.Compile(context).Plan;
      string json = HBRTaskPlanCanonicalizer.ToJson(source);

      Assert.True(HBRTaskPlanCanonicalizer.TryParse(json, out HBRTaskPlan restored, out string error), error);
      Assert.Equal(source.TaskPlanHash, restored.TaskPlanHash);
      Assert.Equal(source.BuildSequence, restored.BuildSequence);
      Assert.Contains(restored.ActiveTasks, task => task.TaskId == "SITE.GREEN");
    }

    [Fact]
    public void RequiresRecompile_UsesSourceFileContextHash()
    {
      HBRFileContext context = TaskPlanCompilerTests.BuildContext(
        PlanningTargetRequirementPolicy.SiteModel,
        new System.Collections.Generic.Dictionary<string, bool>());
      HBRTaskPlan plan = TaskPlanCompiler.Compile(context).Plan;

      Assert.False(plan.RequiresRecompile(context.FileContextHash));
      Assert.True(plan.RequiresRecompile("different-context-hash"));
    }

    [Fact]
    public void TaskPlanHash_IsDeterministic()
    {
      HBRFileContext context = TaskPlanCompilerTests.BuildContext(
        PlanningTargetRequirementPolicy.SiteModel,
        new System.Collections.Generic.Dictionary<string, bool>
        {
          ["site.green"] = true,
          ["site.outdoor_parking"] = true
        });

      HBRTaskPlan first = TaskPlanCompiler.Compile(context).Plan;
      HBRTaskPlan second = TaskPlanCompiler.Compile(context).Plan;

      Assert.Equal(first.TaskPlanHash, second.TaskPlanHash);
      Assert.Equal(HBRTaskPlanCanonicalizer.ToJson(first), HBRTaskPlanCanonicalizer.ToJson(second));
    }
  }
}
