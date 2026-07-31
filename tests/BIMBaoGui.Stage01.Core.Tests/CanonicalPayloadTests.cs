using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class CanonicalPayloadTests
  {
    [Fact]
    public void Build_IsDeterministicRegardlessOfInsertionOrder()
    {
      var first = new Stage01Model();
      first.SetValue("b", "2");
      first.SetValue("a", "1");
      first.SetCondition("z", true);
      first.SetCondition("a", false);
      AddTarget(first, PlanningTargetCatalog.GreenRateCode, PlanningTargetOperator.GreaterOrEqual, "35");
      AddTarget(first, PlanningTargetCatalog.FloorAreaRatioCode, PlanningTargetOperator.LessOrEqual, "2.00");

      var second = new Stage01Model();
      second.SetValue("a", "1");
      second.SetValue("b", "2");
      second.SetCondition("a", false);
      second.SetCondition("z", true);
      AddTarget(second, PlanningTargetCatalog.FloorAreaRatioCode, PlanningTargetOperator.LessOrEqual, "2.00");
      AddTarget(second, PlanningTargetCatalog.GreenRateCode, PlanningTargetOperator.GreaterOrEqual, "35");

      Assert.Equal(CanonicalPayload.Build(first), CanonicalPayload.Build(second));
      Assert.Equal(CanonicalPayload.Sha256(CanonicalPayload.Build(first)), CanonicalPayload.Sha256(CanonicalPayload.Build(second)));
    }

    [Fact]
    public void Build_DoesNotIncludeVolatileInitializationStatus()
    {
      var first = new Stage01Model();
      first.SetValue(Stage01Keys.ProjectNumber, "P-001");
      first.SetValue(Stage01Keys.InitializationStatus, "待提交");
      var second = first.Clone();
      second.SetValue(Stage01Keys.InitializationStatus, "初始化通过");

      Assert.Equal(CanonicalPayload.Build(first), CanonicalPayload.Build(second));
    }

    [Fact]
    public void Build_ContainsStructuredPlanningTargetNode()
    {
      var model = new Stage01Model();
      AddTarget(model, PlanningTargetCatalog.BuildingDensityCode, PlanningTargetOperator.LessOrEqual, "30");

      string payload = CanonicalPayload.Build(model);

      Assert.Contains("planningTargets", payload);
      Assert.Contains("mvdText", payload);
      Assert.Contains("≤30%", payload);
    }

    private static void AddTarget(Stage01Model model, string metricCode, PlanningTargetOperator op, string value)
    {
      PlanningTargetDefinition definition = PlanningTargetCatalog.Get(metricCode);
      Assert.True(PlanningTargetValue.TryCreate(
        metricCode,
        op,
        value,
        null,
        definition.Unit,
        "项目初始化",
        out PlanningTargetValue target,
        out string error), error);
      model.SetPlanningTarget(target);
    }
  }
}
