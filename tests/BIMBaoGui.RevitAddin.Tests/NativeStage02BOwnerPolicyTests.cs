using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage02B;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02BOwnerPolicyTests
  {
    [Fact]
    public void IfcProjectAllowsOnlyProjectInformationParameterProjection()
    {
      NativeStage02BMetricDefinition metric = Metric(
        "ca21e324-046b-5bfd-84c8-0d3470082303");

      NativeStage02BOwnerDecision decision = NativeStage02BOwnerPolicy.Resolve(
        metric, Policy("IfcProject"), null, null, null);

      Assert.True(decision.InternalSaveAllowed);
      Assert.True(decision.ParameterProjectionAllowed);
      Assert.Equal(NativeStage02BProjectionMode.ProjectInformation,
        decision.ProjectionMode);
      Assert.Equal(NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt,
        decision.OfficialCarrierStatus);
    }

    [Theory]
    [InlineData("93e51676-237e-56a8-8f28-2da845422e2e", "IfcSite")]
    [InlineData("c62cfd5f-2a50-5230-9c5d-4037c39061bf", "IfcSpatialZone")]
    public void SiteAndSpatialZoneAreInternalStorageOnly(
      string propertyId,
      string ifcEntity)
    {
      NativeStage02BOwnerDecision decision = NativeStage02BOwnerPolicy.Resolve(
        Metric(propertyId), Policy(ifcEntity), null, null, null);

      Assert.True(decision.InternalSaveAllowed);
      Assert.False(decision.ParameterProjectionAllowed);
      Assert.Equal(NativeStage02BProjectionMode.InternalStorageOnly,
        decision.ProjectionMode);
      Assert.Equal("OFFICIAL_CARRIER_PENDING_GOLDEN_RVT", decision.Code);
    }

    [Fact]
    public void CrossPropertyCarrierOrProbeIsFailClosedPerProperty()
    {
      NativeStage02BMetricDefinition metric = Metric(
        "201a00ac-3672-5ded-83d2-ed96f81bfabf");
      var carrier = new NativeOfficialProjectionCarrierDefinition
      {
        CarrierId = "carrier-other", PropertyId = "93e51676-237e-56a8-8f28-2da845422e2e"
      };
      var probe = new NativeOfficialCarrierProbeRecord
      {
        ProbeId = "probe-other", PropertyId = "93e51676-237e-56a8-8f28-2da845422e2e"
      };

      NativeStage02BOwnerDecision decision = NativeStage02BOwnerPolicy.Resolve(
        metric, Policy("IfcSite"), carrier, probe, null);

      Assert.Equal(NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt,
        decision.OfficialCarrierStatus);
      Assert.Equal("OFFICIAL_CARRIER_PENDING_GOLDEN_RVT", decision.Code);
    }

    private static NativeStage02BMetricDefinition Metric(string propertyId)
    {
      return NativeStage02BMetricCatalog.Current.MetricsFor("总平模型")
        .Single(value => value.PropertyId == propertyId);
    }

    private static NativeOfficialCarrierPolicy Policy(string ifcEntity)
    {
      return new NativeOfficialCarrierPolicy
      {
        IfcEntity = ifcEntity,
        EvidenceStatus = NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt
      };
    }
  }
}
