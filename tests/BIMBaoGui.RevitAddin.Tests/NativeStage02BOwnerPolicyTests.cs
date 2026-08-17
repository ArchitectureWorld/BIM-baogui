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
    public void EmptyCarrierAndProbeRefsCannotUpgradeVerifiedInputs()
    {
      NativeStage02BMetricDefinition metric = VerifiedMetric(
        "201a00ac-3672-5ded-83d2-ed96f81bfabf", string.Empty, string.Empty);
      var carrier = new NativeOfficialProjectionCarrierDefinition
      {
        CarrierId = string.Empty, PropertyId = metric.PropertyId
      };
      var probe = new NativeOfficialCarrierProbeRecord
      {
        ProbeId = string.Empty, PropertyId = metric.PropertyId
      };

      NativeStage02BOwnerDecision decision = NativeStage02BOwnerPolicy.Resolve(
        metric, VerifiedPolicy("IfcSite"), carrier, probe, null);

      Assert.Equal(NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt,
        decision.OfficialCarrierStatus);
      Assert.Equal("OFFICIAL_CARRIER_PENDING_GOLDEN_RVT", decision.Code);
    }

    [Theory]
    [InlineData("wrong-carrier", "expected-probe", "201a00ac-3672-5ded-83d2-ed96f81bfabf", "201a00ac-3672-5ded-83d2-ed96f81bfabf")]
    [InlineData("expected-carrier", "wrong-probe", "201a00ac-3672-5ded-83d2-ed96f81bfabf", "201a00ac-3672-5ded-83d2-ed96f81bfabf")]
    [InlineData("expected-carrier", "expected-probe", "93e51676-237e-56a8-8f28-2da845422e2e", "201a00ac-3672-5ded-83d2-ed96f81bfabf")]
    [InlineData("expected-carrier", "expected-probe", "201a00ac-3672-5ded-83d2-ed96f81bfabf", "93e51676-237e-56a8-8f28-2da845422e2e")]
    public void VerifiedInputsWithBrokenCarrierOrProbeForeignKeyRemainPending(
      string carrierId,
      string probeId,
      string carrierPropertyId,
      string probePropertyId)
    {
      NativeStage02BMetricDefinition metric = VerifiedMetric(
        "201a00ac-3672-5ded-83d2-ed96f81bfabf",
        "expected-carrier", "expected-probe");
      var carrier = new NativeOfficialProjectionCarrierDefinition
      {
        CarrierId = carrierId, PropertyId = carrierPropertyId
      };
      var probe = new NativeOfficialCarrierProbeRecord
      {
        ProbeId = probeId, PropertyId = probePropertyId
      };

      NativeStage02BOwnerDecision decision = NativeStage02BOwnerPolicy.Resolve(
        metric, VerifiedPolicy("IfcSite"), carrier, probe, null);

      Assert.Equal(NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt,
        decision.OfficialCarrierStatus);
      Assert.Equal("OFFICIAL_CARRIER_PENDING_GOLDEN_RVT", decision.Code);
    }

    [Fact]
    public void CompleteFutureEvidenceForTheSamePropertyCanBeVerified()
    {
      NativeStage02BMetricDefinition metric = VerifiedMetric(
        "201a00ac-3672-5ded-83d2-ed96f81bfabf",
        "carrier-future", "probe-future");
      var carrier = new NativeOfficialProjectionCarrierDefinition
      {
        CarrierId = "carrier-future", PropertyId = metric.PropertyId
      };
      var probe = new NativeOfficialCarrierProbeRecord
      {
        ProbeId = "probe-future", PropertyId = metric.PropertyId
      };

      NativeStage02BOwnerDecision decision = NativeStage02BOwnerPolicy.Resolve(
        metric, VerifiedPolicy("IfcSite"), carrier, probe, null);

      Assert.Equal(NativeOfficialCarrierEvidenceStatus.Verified,
        decision.OfficialCarrierStatus);
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

    private static NativeStage02BMetricDefinition VerifiedMetric(
      string propertyId,
      string carrierId,
      string probeId)
    {
      NativeStage02BMetricDefinition metric = Metric(propertyId);
      return new NativeStage02BMetricDefinition
      {
        PropertyId = metric.PropertyId,
        Identity = metric.Identity,
        Sequence = metric.Sequence,
        Source = metric.Source,
        Property = metric.Property,
        OfficialCarrierStatus = NativeOfficialCarrierEvidenceStatus.Verified,
        OfficialProjectionCarrierId = carrierId,
        OfficialCarrierProbeRef = probeId
      };
    }

    private static NativeOfficialCarrierPolicy VerifiedPolicy(string ifcEntity)
    {
      NativeOfficialCarrierPolicy policy = Policy(ifcEntity);
      policy.EvidenceStatus = NativeOfficialCarrierEvidenceStatus.Verified;
      return policy;
    }
  }
}
