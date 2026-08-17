using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage02B;
using BIMBaoGui.RevitAddin.Workflow;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02BResultCanonicalizerTests
  {
    [Fact]
    public void Retry_envelope_rebuilds_all_six_and_marks_latest_failure()
    {
      NativeWorkflowIdentity identity = NativeStage02BCanonicalizerTests.Identity();
      NativeStage02BMetricDefinition[] metrics = NativeStage02BMetricCatalog
        .Current.MetricsFor("总平模型").ToArray();
      NativeStage02BStorageSnapshot first = NativeStage02BCanonicalizer
        .SealSnapshot(metrics.Select((metric, index) => Success(
          metric, "run-old", (index + 1).ToString(), identity,
          "2026-08-14T00:00:00.0000000Z")));
      NativeWorkflowResultEnvelope oldEnvelope = NativeStage02BResultCanonicalizer
        .Build("run-old", identity, first, metrics.Select(value => value.PropertyId),
          metrics.Select(value => new NativeStage02BMetricOutcome
          {
            PropertyId = value.PropertyId,
            Succeeded = true,
            ReadbackSucceeded = true
          }).ToArray(), "2026-08-14T00:00:00.0000000Z");

      NativeStage02BMetricDefinition b = metrics[1];
      NativeStage02BMetricDefinition d = metrics[3];
      NativeStage02BMetricRecord bSuccess = Success(
        b, "run-retry", "22", identity,
        "2026-08-14T01:00:00.0000000Z");
      NativeStage02BMetricRecord oldD = first.Records.Single(value =>
        value.PropertyId == d.PropertyId);
      NativeStage02BMetricRecord dFailure = NativeStage02BCanonicalizer.SealRecord(
        new NativeStage02BMetricRecord
        {
          PropertyId = oldD.PropertyId,
          Identity = oldD.Identity,
          Unit = oldD.Unit,
          Source = oldD.Source,
          RequestedCanonicalValue = "44",
          LastSuccessfulCanonicalValue = oldD.LastSuccessfulCanonicalValue,
          LastAttemptRunId = "run-retry",
          LastSuccessfulRunId = oldD.LastSuccessfulRunId,
          WriteStatus = "FAILED",
          ReadbackStatus = "FAILED",
          ProjectionStatus = oldD.ProjectionStatus,
          OfficialCarrierStatus = oldD.OfficialCarrierStatus,
          IdentityContext = identity,
          UpdatedUtc = "2026-08-14T01:00:00.0000000Z",
          ErrorCode = "READBACK_FAILED"
        });
      NativeStage02BStorageSnapshot retrySnapshot =
        NativeStage02BStoragePolicy.Merge(
          NativeStage02BStoragePolicy.Merge(first, bSuccess), dFailure);

      NativeWorkflowResultEnvelope retry = NativeStage02BResultCanonicalizer
        .Build("run-retry", identity, retrySnapshot,
          new[] { b.PropertyId, d.PropertyId },
          new[]
          {
            new NativeStage02BMetricOutcome
            {
              PropertyId = b.PropertyId,
              Succeeded = true,
              ReadbackSucceeded = true
            },
            new NativeStage02BMetricOutcome
            {
              PropertyId = d.PropertyId,
              Succeeded = false,
              ReadbackSucceeded = false,
              ErrorCode = "READBACK_FAILED"
            }
          }, "2026-08-14T01:00:00.0000000Z");

      Assert.Equal(6, retry.Items.Count);
      Assert.Equal("22", retry.Items.Single(value =>
        value.Identity == b.Identity).CurrentValue);
      NativeWorkflowItemEvidence failed = retry.Items.Single(value =>
        value.Identity == d.Identity);
      Assert.False(failed.WriteSucceeded);
      Assert.False(failed.ReadbackSucceeded);
      Assert.Equal("READBACK_FAILED", failed.ErrorCode);
      foreach (NativeStage02BMetricDefinition untouched in new[]
        { metrics[0], metrics[2], metrics[4], metrics[5] })
      {
        Assert.Equal(
          oldEnvelope.Items.Single(value => value.Identity == untouched.Identity)
            .StableHash,
          retry.Items.Single(value => value.Identity == untouched.Identity)
            .StableHash);
      }
    }

    [Fact]
    public void Envelope_rejects_missing_or_unknown_metric_records()
    {
      NativeWorkflowIdentity identity = NativeStage02BCanonicalizerTests.Identity();
      NativeStage02BMetricDefinition[] metrics = NativeStage02BMetricCatalog
        .Current.MetricsFor("总平模型").ToArray();
      NativeStage02BStorageSnapshot missing = NativeStage02BCanonicalizer
        .SealSnapshot(metrics.Take(5).Select((metric, index) => Success(
          metric, "run", index.ToString(), identity,
          "2026-08-14T00:00:00.0000000Z")));

      Assert.Throws<InvalidOperationException>(() =>
        NativeStage02BResultCanonicalizer.Build(
          "run", identity, missing, Array.Empty<string>(),
          Array.Empty<NativeStage02BMetricOutcome>(),
          "2026-08-14T00:00:00.0000000Z"));
    }

    private static NativeStage02BMetricRecord Success(
      NativeStage02BMetricDefinition metric,
      string runId,
      string value,
      NativeWorkflowIdentity identity,
      string updatedUtc)
    {
      return NativeStage02BCanonicalizer.SealRecord(
        new NativeStage02BMetricRecord
        {
          PropertyId = metric.PropertyId,
          Identity = metric.Identity,
          Unit = metric.Property.CanonicalUnit,
          Source = "MANUAL_INPUT",
          RequestedCanonicalValue = value,
          LastSuccessfulCanonicalValue = value,
          LastAttemptRunId = runId,
          LastSuccessfulRunId = runId,
          WriteStatus = "SUCCEEDED",
          ReadbackStatus = "SUCCEEDED",
          ProjectionStatus = metric.Property.IfcEntity == "IfcProject"
            ? "PROJECT_INFORMATION" : "BLOCKED_PENDING_GOLDEN_RVT",
          OfficialCarrierStatus = metric.OfficialCarrierStatus,
          IdentityContext = identity,
          UpdatedUtc = updatedUtc
        });
    }
  }
}
