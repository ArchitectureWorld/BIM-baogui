using System;
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

      bool exportReady = record.OfficialCarrierStatus ==
          NativeOfficialCarrierEvidenceStatus.Verified
        && !string.IsNullOrWhiteSpace(record.OfficialProjectionCarrierId)
        && !string.IsNullOrWhiteSpace(record.OfficialCarrierProbeRef);
      return new NativeStage02BCurrentResultDecision
      {
        Current = true,
        ExportReady = exportReady,
        CurrentCanonicalValue = record.LastSuccessfulCanonicalValue,
        Code = exportReady ? "CURRENT_EXPORT_READY" : "CURRENT_PENDING_GOLDEN_RVT"
      };
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
