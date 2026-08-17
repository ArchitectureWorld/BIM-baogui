using System;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage02B
{
  internal enum NativeStage02BProjectionMode
  {
    ProjectInformation,
    VerifiedElementParameter,
    InternalStorageOnly
  }

  internal sealed class NativeStage02BOwnerDecision
  {
    internal bool InternalSaveAllowed { get; set; }
    internal bool ParameterProjectionAllowed { get; set; }
    internal NativeStage02BProjectionMode ProjectionMode { get; set; }
    internal NativeOfficialCarrierEvidenceStatus OfficialCarrierStatus { get; set; }
    internal string Code { get; set; } = string.Empty;
  }

  internal static class NativeStage02BOwnerPolicy
  {
    internal static NativeStage02BOwnerDecision Resolve(
      NativeStage02BMetricDefinition metric,
      NativeOfficialCarrierPolicy carrierPolicy,
      NativeOfficialProjectionCarrierDefinition projectionCarrier,
      NativeOfficialCarrierProbeRecord carrierProbe,
      NativeOfficialEvidenceRecord officialEvidence)
    {
      string entity = metric?.Property?.IfcEntity ?? string.Empty;
      if (metric == null || metric.Property == null || carrierPolicy == null
        || !string.Equals(entity, carrierPolicy.IfcEntity,
          StringComparison.Ordinal))
        return Blocked();

      NativeOfficialCarrierEvidenceStatus status = ResolveCarrierStatus(
        metric, carrierPolicy, projectionCarrier, carrierProbe);
      if (string.Equals(entity, "IfcProject", StringComparison.Ordinal))
      {
        return new NativeStage02BOwnerDecision
        {
          InternalSaveAllowed = true,
          ParameterProjectionAllowed = true,
          ProjectionMode = NativeStage02BProjectionMode.ProjectInformation,
          OfficialCarrierStatus = status,
          Code = status == NativeOfficialCarrierEvidenceStatus.Verified
            ? string.Empty : "OFFICIAL_CARRIER_PENDING_GOLDEN_RVT"
        };
      }
      if (string.Equals(entity, "IfcSite", StringComparison.Ordinal)
        || string.Equals(entity, "IfcSpatialZone", StringComparison.Ordinal))
      {
        return new NativeStage02BOwnerDecision
        {
          InternalSaveAllowed = true,
          ParameterProjectionAllowed = false,
          ProjectionMode = NativeStage02BProjectionMode.InternalStorageOnly,
          OfficialCarrierStatus = status,
          Code = status == NativeOfficialCarrierEvidenceStatus.Verified
            ? string.Empty : "OFFICIAL_CARRIER_PENDING_GOLDEN_RVT"
        };
      }
      return Blocked();
    }

    private static NativeOfficialCarrierEvidenceStatus ResolveCarrierStatus(
      NativeStage02BMetricDefinition metric,
      NativeOfficialCarrierPolicy carrierPolicy,
      NativeOfficialProjectionCarrierDefinition carrier,
      NativeOfficialCarrierProbeRecord probe)
    {
      if (metric.OfficialCarrierStatus != NativeOfficialCarrierEvidenceStatus.Verified
        || carrierPolicy.EvidenceStatus != NativeOfficialCarrierEvidenceStatus.Verified
        || carrier == null || probe == null
        || !string.Equals(carrier.CarrierId,
          metric.OfficialProjectionCarrierId, StringComparison.Ordinal)
        || !string.Equals(probe.ProbeId,
          metric.OfficialCarrierProbeRef, StringComparison.Ordinal)
        || !string.Equals(carrier.PropertyId, metric.PropertyId,
          StringComparison.Ordinal)
        || !string.Equals(probe.PropertyId, metric.PropertyId,
          StringComparison.Ordinal))
        return NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt;
      return NativeOfficialCarrierEvidenceStatus.Verified;
    }

    private static NativeStage02BOwnerDecision Blocked()
    {
      return new NativeStage02BOwnerDecision
      {
        ProjectionMode = NativeStage02BProjectionMode.InternalStorageOnly,
        OfficialCarrierStatus = NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt,
        Code = "UNSUPPORTED_METRIC_OWNER"
      };
    }
  }
}
