using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage02B;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02BValuePolicyTests
  {
    [Theory]
    [InlineData("ca21e324-046b-5bfd-84c8-0d3470082303", "12345.6", true)]
    [InlineData("ca21e324-046b-5bfd-84c8-0d3470082303", "0", false)]
    [InlineData("93e51676-237e-56a8-8f28-2da845422e2e", "0.25", true)]
    [InlineData("201a00ac-3672-5ded-83d2-ed96f81bfabf", "NaN", false)]
    [InlineData("c62cfd5f-2a50-5230-9c5d-4037c39061bf", "120", true)]
    [InlineData("84df74c2-a7e5-5a98-a5e0-4458e49a3973", "1.5", false)]
    public void MetricValuesFollowRuleTypes(string propertyId, string raw, bool valid)
    {
      NativeStage02BMetricDefinition metric = NativeStage02BMetricCatalog.Current
        .MetricsFor("总平模型")
        .Single(value => value.PropertyId == propertyId);

      NativeStage02BValueDecision decision = NativeStage02BValuePolicy.Validate(
        metric, raw);

      Assert.Equal(valid, decision.Accepted);
    }

    [Fact]
    public void DoublesUseInvariantG17WithoutPercentageScaling()
    {
      NativeStage02BMetricDefinition metric = NativeStage02BMetricCatalog.Current
        .MetricsFor("总平模型")
        .Single(value => value.PropertyId == "93e51676-237e-56a8-8f28-2da845422e2e");

      NativeStage02BValueDecision decision = NativeStage02BValuePolicy.Validate(
        metric, "0.25");

      Assert.True(decision.Accepted);
      Assert.Equal("0.25", decision.CanonicalValue);
    }
  }
}
