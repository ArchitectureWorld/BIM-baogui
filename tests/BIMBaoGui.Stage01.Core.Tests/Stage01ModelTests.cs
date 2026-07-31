using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage01ModelTests
  {
    [Fact]
    public void Clone_IsIndependentFromOriginal()
    {
      var original = new Stage01Model();
      original.SetValue("field", "before");
      original.SetCondition("condition", true);
      original.SetOrganizationValue("organization", "before");
      Assert.True(PlanningTargetValue.TryCreate(
        PlanningTargetCatalog.GreenRateCode,
        PlanningTargetOperator.GreaterOrEqual,
        "35",
        null,
        PlanningTargetUnit.Percent,
        "项目初始化",
        out PlanningTargetValue target,
        out string error), error);
      original.SetPlanningTarget(target);

      Stage01Model clone = original.Clone();
      clone.SetValue("field", "after");
      clone.SetCondition("condition", false);
      clone.SetOrganizationValue("organization", "after");
      clone.RemovePlanningTarget(PlanningTargetCatalog.GreenRateCode);

      Assert.Equal("before", original.GetValue("field"));
      Assert.True(original.GetCondition("condition"));
      Assert.Equal("before", original.GetOrganizationValue("organization"));
      Assert.NotNull(original.GetPlanningTarget(PlanningTargetCatalog.GreenRateCode));
      Assert.Null(clone.GetPlanningTarget(PlanningTargetCatalog.GreenRateCode));
    }
  }
}
