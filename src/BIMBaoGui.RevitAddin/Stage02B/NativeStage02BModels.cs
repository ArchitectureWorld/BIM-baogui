using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BIMBaoGui.RevitAddin.Issues;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage02B
{
  internal sealed class NativeStage02BMetricInput
  {
    internal string PropertyId { get; set; } = string.Empty;
    internal string RawValue { get; set; } = string.Empty;
  }

  internal sealed class NativeStage02BWriteRequest
  {
    internal string RunId { get; set; } = string.Empty;
    internal IReadOnlyList<NativeStage02BMetricInput> Metrics { get; set; } =
      Array.Empty<NativeStage02BMetricInput>();
    internal IReadOnlyList<string> PropertyIdsToRetry { get; set; } =
      Array.Empty<string>();

    internal NativeStage02BWriteRequest Clone()
    {
      var metrics = new List<NativeStage02BMetricInput>();
      foreach (NativeStage02BMetricInput input in Metrics ??
        Array.Empty<NativeStage02BMetricInput>())
      {
        if (input == null) continue;
        metrics.Add(new NativeStage02BMetricInput
        {
          PropertyId = input.PropertyId ?? string.Empty,
          RawValue = input.RawValue ?? string.Empty
        });
      }
      var retries = new List<string>();
      foreach (string propertyId in PropertyIdsToRetry ?? Array.Empty<string>())
        retries.Add(propertyId ?? string.Empty);
      return new NativeStage02BWriteRequest
      {
        RunId = RunId ?? string.Empty,
        Metrics = new ReadOnlyCollection<NativeStage02BMetricInput>(metrics),
        PropertyIdsToRetry = new ReadOnlyCollection<string>(retries)
      };
    }
  }

  internal sealed class NativeStage02BMetricRecord
  {
    internal string PropertyId { get; set; } = string.Empty;
    internal string Identity { get; set; } = string.Empty;
    internal string Unit { get; set; } = string.Empty;
    internal string Source { get; set; } = "MANUAL_INPUT";
    internal string RequestedCanonicalValue { get; set; } = string.Empty;
    internal string LastSuccessfulCanonicalValue { get; set; } = string.Empty;
    internal string LastAttemptRunId { get; set; } = string.Empty;
    internal string LastSuccessfulRunId { get; set; } = string.Empty;
    internal string WriteStatus { get; set; } = string.Empty;
    internal string ReadbackStatus { get; set; } = string.Empty;
    internal string ProjectionStatus { get; set; } = string.Empty;
    internal NativeOfficialCarrierEvidenceStatus OfficialCarrierStatus { get; set; }
    internal string OfficialProjectionCarrierId { get; set; } = string.Empty;
    internal string OfficialCarrierProbeRef { get; set; } = string.Empty;
    internal string OfficialEvidenceRef { get; set; } = string.Empty;
    internal NativeWorkflowIdentity IdentityContext { get; set; }
    internal string UpdatedUtc { get; set; } = string.Empty;
    internal string ResultHash { get; set; } = string.Empty;
    internal string ErrorCode { get; set; } = string.Empty;
  }

  internal sealed class NativeStage02BMetricOutcome
  {
    internal string PropertyId { get; set; } = string.Empty;
    internal string Identity { get; set; } = string.Empty;
    internal string RequestedCanonicalValue { get; set; } = string.Empty;
    internal string PersistedCanonicalValue { get; set; } = string.Empty;
    internal bool Succeeded { get; set; }
    internal bool InternalWriteSucceeded { get; set; }
    internal bool ParameterWriteSucceeded { get; set; }
    internal bool ReadbackSucceeded { get; set; }
    internal string ProjectionStatus { get; set; } = string.Empty;
    internal NativeOfficialCarrierEvidenceStatus OfficialCarrierStatus { get; set; }
    internal string OfficialProjectionCarrierId { get; set; } = string.Empty;
    internal string OfficialEvidenceRef { get; set; } = string.Empty;
    internal string ErrorCode { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
    internal NativeStage02BMetricRecord Record { get; set; }
  }

  internal sealed class NativeStage02BWriteResult
  {
    internal string RunId { get; set; } = string.Empty;
    internal IReadOnlyList<NativeStage02BMetricOutcome> MetricOutcomes { get; set; } =
      Array.Empty<NativeStage02BMetricOutcome>();
    internal IReadOnlyList<string> FailedPropertyIds { get; set; } =
      Array.Empty<string>();
    internal bool PartialSuccess { get; set; }
    internal NativeWorkflowResultEnvelope WorkflowResult { get; set; }
    internal string TechnicalErrorCode { get; set; } = string.Empty;
  }

  internal sealed class NativeStage02BStorageSnapshot
  {
    internal string SchemaVersion { get; set; } = "HBR_NATIVE_STAGE02B_METRICS_V1";
    internal IReadOnlyList<NativeStage02BMetricRecord> Records { get; set; } =
      Array.Empty<NativeStage02BMetricRecord>();
    internal string CanonicalJson { get; set; } = string.Empty;
    internal string SnapshotHash { get; set; } = string.Empty;
  }

  internal sealed class NativeStage02BReadResult
  {
    internal NativeWorkflowIdentity Identity { get; set; }
    internal IReadOnlyList<NativeStage02BMetricRecord> Records { get; set; } =
      Array.Empty<NativeStage02BMetricRecord>();
    internal NativeWorkflowResultEnvelope WorkflowResult { get; set; }
    internal IReadOnlyList<NativeIssueRecord> Issues { get; set; } =
      Array.Empty<NativeIssueRecord>();
  }
}
