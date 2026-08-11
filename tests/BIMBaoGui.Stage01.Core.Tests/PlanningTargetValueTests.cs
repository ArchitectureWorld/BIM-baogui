using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class PlanningTargetValueTests
  {
    [Theory]
    [InlineData(PlanningTargetCatalog.BuildingDensityCode, PlanningTargetOperator.LessOrEqual, "30", null, PlanningTargetUnit.Percent, "≤30%")]
    [InlineData(PlanningTargetCatalog.FloorAreaRatioCode, PlanningTargetOperator.LessOrEqual, "2.00", null, PlanningTargetUnit.Ratio, "≤2.00")]
    [InlineData(PlanningTargetCatalog.GreenRateCode, PlanningTargetOperator.GreaterOrEqual, "35", null, PlanningTargetUnit.Percent, "≥35%")]
    public void ToMvdText_IsStable(
      string metricCode,
      PlanningTargetOperator @operator,
      string value1,
      string value2,
      PlanningTargetUnit unit,
      string expected)
    {
      Assert.True(PlanningTargetValue.TryCreate(
        metricCode,
        @operator,
        value1,
        value2,
        unit,
        "项目初始化",
        out PlanningTargetValue target,
        out string error), error);

      Assert.Equal(expected, target.ToMvdText());
    }

    [Theory]
    [InlineData("-1", "百分比必须位于 0 到 100。")]
    [InlineData("101", "百分比必须位于 0 到 100。")]
    [InlineData("abc", "应填写数值，例如 30。")]
    public void PercentTarget_RejectsInvalidValues(string value, string expected)
    {
      Assert.False(PlanningTargetValue.TryCreate(
        PlanningTargetCatalog.BuildingDensityCode,
        PlanningTargetOperator.LessOrEqual,
        value,
        null,
        PlanningTargetUnit.Percent,
        "项目初始化",
        out _,
        out string error));

      Assert.Equal(expected, error);
    }

    [Fact]
    public void Range_RejectsUpperLimitBelowLowerLimit()
    {
      Assert.False(PlanningTargetValue.TryCreate(
        PlanningTargetCatalog.FloorAreaRatioCode,
        PlanningTargetOperator.Range,
        "2.00",
        "1.00",
        PlanningTargetUnit.Ratio,
        "项目初始化",
        out _,
        out string error));

      Assert.Equal("区间上限不得小于下限。", error);
    }
  }
}
