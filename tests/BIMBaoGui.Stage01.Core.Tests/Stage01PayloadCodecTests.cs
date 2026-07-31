using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage01PayloadCodecTests
  {
    [Fact]
    public void RoundTrip_RestoresStructuredPlanningTargets()
    {
      var source = new Stage01Model();
      AddTarget(source, PlanningTargetCatalog.FloorAreaRatioCode, PlanningTargetOperator.LessOrEqual, "2.00");
      string payload = CanonicalPayload.Build(source);
      var restored = new Stage01Model();

      Assert.True(Stage01PayloadCodec.TryApply(payload, restored, out string error), error);
      PlanningTargetValue value = restored.GetPlanningTarget(PlanningTargetCatalog.FloorAreaRatioCode);
      Assert.NotNull(value);
      Assert.Equal("≤2.00", value.ToMvdText());
    }

    [Fact]
    public void LegacyValues_RestoreStructuredPlanningTargetWhenParseable()
    {
      string fieldKey = PlanningTargetCatalog.Get(PlanningTargetCatalog.GreenRateCode).MvdFieldKey;
      string payload = "{\"schemaVersion\":\"0.4.0\",\"workflowVersion\":\"0.4.0\",\"values\":{\"" + fieldKey + "\":\"≥35%\"},\"conditions\":{},\"organizations\":[]}";
      var restored = new Stage01Model();

      Assert.True(Stage01PayloadCodec.TryApply(payload, restored, out string error), error);
      Assert.Equal("≥35%", restored.GetPlanningTarget(PlanningTargetCatalog.GreenRateCode).ToMvdText());
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
