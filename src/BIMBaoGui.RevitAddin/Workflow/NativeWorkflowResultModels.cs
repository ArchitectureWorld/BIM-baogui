using System;
using System.Collections.Generic;

namespace BIMBaoGui.RevitAddin.Workflow
{
  internal sealed class NativeWorkflowIdentity
  {
    internal string DocumentFingerprint { get; set; } = string.Empty;
    internal string ModelFileType { get; set; } = string.Empty;
    internal string RulePackageId { get; set; } = string.Empty;
    internal string RulePackageVersion { get; set; } = string.Empty;
    internal string RulePackageSha256 { get; set; } = string.Empty;
  }

  internal sealed class NativeWorkflowItemEvidence
  {
    internal string Identity { get; set; } = string.Empty;
    internal string CurrentValue { get; set; } = string.Empty;
    internal string Unit { get; set; } = string.Empty;
    internal string Source { get; set; } = string.Empty;
    internal bool WriteSucceeded { get; set; }
    internal bool ReadbackSucceeded { get; set; }
    internal string InputHash { get; set; } = string.Empty;
    internal string UpdatedUtc { get; set; } = string.Empty;
    internal string StableHash { get; set; } = string.Empty;
    internal string ErrorCode { get; set; } = string.Empty;
  }

  internal sealed class NativeWorkflowResultEnvelope
  {
    internal const string CurrentSchemaVersion =
      "HBR_NATIVE_WORKFLOW_RESULT_V1";

    internal string SchemaVersion { get; set; } = CurrentSchemaVersion;
    internal string RunId { get; set; } = string.Empty;
    internal string SourceFeature { get; set; } = string.Empty;
    internal string SourceFunction { get; set; } = string.Empty;
    internal NativeWorkflowIdentity Identity { get; set; }
    internal string InputSnapshotHash { get; set; } = string.Empty;
    internal string UpdatedUtc { get; set; } = string.Empty;
    internal IReadOnlyList<NativeWorkflowItemEvidence> Items { get; set; } =
      Array.Empty<NativeWorkflowItemEvidence>();
    internal string CanonicalJson { get; set; } = string.Empty;
    internal string ResultHash { get; set; } = string.Empty;
  }
}
