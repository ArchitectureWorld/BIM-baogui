using System;
using System.IO;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage02B
{
  internal sealed class NativeStage02BCurrentResultDecision
  {
    internal bool Current { get; set; }
    internal bool ExportReady { get; set; }
    internal string CurrentCanonicalValue { get; set; } = string.Empty;
    internal string Code { get; set; } = string.Empty;
  }

  internal static class NativeStage02BCurrentResultPolicy
  {
    internal static NativeStage02BCurrentResultDecision Evaluate(
      NativeStage02BMetricRecord record,
      NativeWorkflowIdentity currentIdentity)
    {
      if (!NativeStage02BCanonicalizer.VerifyRecord(record))
        return Reject("RECORD_HASH_MISMATCH");
      if (!IdentityMatches(record.IdentityContext, currentIdentity))
        return Reject("WORKFLOW_IDENTITY_MISMATCH");
      if (!string.Equals(record.LastAttemptRunId, record.LastSuccessfulRunId,
          StringComparison.Ordinal)
        || !string.Equals(record.WriteStatus, "SUCCEEDED", StringComparison.Ordinal)
        || !string.Equals(record.ReadbackStatus, "SUCCEEDED", StringComparison.Ordinal))
        return Reject("LATEST_ATTEMPT_FAILED");
      if (string.IsNullOrWhiteSpace(record.LastSuccessfulCanonicalValue))
        return Reject("CURRENT_VALUE_MISSING");

      bool exportReady = HasResolvedPropertyEvidence(record);
      bool claimedEvidence = record.OfficialCarrierStatus ==
        NativeOfficialCarrierEvidenceStatus.Verified;
      return new NativeStage02BCurrentResultDecision
      {
        Current = true,
        ExportReady = exportReady,
        CurrentCanonicalValue = record.LastSuccessfulCanonicalValue,
        Code = exportReady ? "CURRENT_EXPORT_READY" : claimedEvidence
          ? "CURRENT_PROPERTY_EVIDENCE_UNRESOLVED"
          : "CURRENT_PENDING_GOLDEN_RVT"
      };
    }

    private static bool HasResolvedPropertyEvidence(NativeStage02BMetricRecord record)
    {
      if (record.OfficialCarrierStatus != NativeOfficialCarrierEvidenceStatus.Verified
        || string.IsNullOrWhiteSpace(record.OfficialProjectionCarrierId)
        || string.IsNullOrWhiteSpace(record.OfficialCarrierProbeRef))
        return false;
      NativeStage02BMetricDefinition metric = NativeStage02BMetricCatalog.Current
        .MetricsFor(record.IdentityContext?.ModelFileType)
        .SingleOrDefault(value => string.Equals(value.PropertyId,
          record.PropertyId, StringComparison.Ordinal));
      if (metric == null
        || metric.OfficialCarrierStatus != NativeOfficialCarrierEvidenceStatus.Verified
        || string.IsNullOrWhiteSpace(metric.OfficialProjectionCarrierId)
        || string.IsNullOrWhiteSpace(metric.OfficialCarrierProbeRef)
        || !string.Equals(record.OfficialProjectionCarrierId,
          metric.OfficialProjectionCarrierId, StringComparison.Ordinal)
        || !string.Equals(record.OfficialCarrierProbeRef,
          metric.OfficialCarrierProbeRef, StringComparison.Ordinal))
        return false;
      try
      {
        NativeReportingRuleCatalog catalog = NativeReportingRuleCatalog.Current;
        NativeOfficialProjectionCarrierDefinition carrier =
          catalog.GetProjectionCarrier(metric.OfficialProjectionCarrierId);
        NativeOfficialCarrierProbeRecord probe = catalog.GetCarrierProbe(
          metric.OfficialCarrierProbeRef);
        return string.Equals(carrier.CarrierId, metric.OfficialProjectionCarrierId,
            StringComparison.Ordinal)
          && string.Equals(probe.ProbeId, metric.OfficialCarrierProbeRef,
            StringComparison.Ordinal)
          && string.Equals(carrier.PropertyId, record.PropertyId,
            StringComparison.Ordinal)
          && string.Equals(probe.PropertyId, record.PropertyId,
            StringComparison.Ordinal);
      }
      catch (InvalidDataException)
      {
        return false;
      }
    }

    private static NativeStage02BCurrentResultDecision Reject(string code)
    {
      return new NativeStage02BCurrentResultDecision { Code = code };
    }

    private static bool IdentityMatches(NativeWorkflowIdentity recorded,
      NativeWorkflowIdentity current)
    {
      return recorded != null && current != null
        && string.Equals(recorded.DocumentFingerprint, current.DocumentFingerprint,
          StringComparison.Ordinal)
        && string.Equals(recorded.ModelFileType, current.ModelFileType,
          StringComparison.Ordinal)
        && string.Equals(recorded.RulePackageId, current.RulePackageId,
          StringComparison.Ordinal)
        && string.Equals(recorded.RulePackageVersion, current.RulePackageVersion,
          StringComparison.Ordinal)
        && string.Equals(recorded.RulePackageSha256, current.RulePackageSha256,
          StringComparison.Ordinal);
    }
  }
}
