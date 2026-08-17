using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage02B
{
  internal sealed class NativeStage02BWriteBatchDecision
  {
    internal IReadOnlyList<string> SuccessfulPropertyIds { get; set; } =
      Array.Empty<string>();
    internal IReadOnlyList<string> FailedPropertyIds { get; set; } =
      Array.Empty<string>();
    internal bool PartialSuccess { get; set; }
  }

  internal static class NativeStage02BWriteBatchPolicy
  {
    internal static NativeStage02BWriteBatchDecision Merge(
      IEnumerable<NativeStage02BMetricOutcome> outcomes)
    {
      NativeStage02BMetricOutcome[] all = (outcomes
          ?? Array.Empty<NativeStage02BMetricOutcome>())
        .Where(value => value != null)
        .ToArray();
      string duplicate = all
        .GroupBy(value => Clean(value.PropertyId), StringComparer.Ordinal)
        .Where(group => group.Key.Length == 0 || group.Count() > 1)
        .Select(group => group.Key)
        .FirstOrDefault();
      if (duplicate != null)
        throw new ArgumentException(
          "Stage02B metric outcomes contain an empty or duplicate propertyId.",
          nameof(outcomes));
      string[] successes = all.Where(value => value.Succeeded)
        .Select(value => Clean(value.PropertyId)).ToArray();
      string[] failures = all.Where(value => !value.Succeeded)
        .Select(value => Clean(value.PropertyId)).ToArray();
      return new NativeStage02BWriteBatchDecision
      {
        SuccessfulPropertyIds = new ReadOnlyCollection<string>(successes),
        FailedPropertyIds = new ReadOnlyCollection<string>(failures),
        PartialSuccess = successes.Length > 0 && failures.Length > 0
      };
    }

    internal static NativeStage02BWriteRequest BuildRetry(
      NativeStage02BWriteResult lastResult,
      IReadOnlyList<NativeStage02BMetricInput> currentInputs)
    {
      if (lastResult == null) throw new ArgumentNullException(nameof(lastResult));
      NativeStage02BMetricInput[] inputs = (currentInputs
          ?? Array.Empty<NativeStage02BMetricInput>())
        .Where(value => value != null)
        .ToArray();
      string duplicate = inputs.GroupBy(value => Clean(value.PropertyId),
          StringComparer.Ordinal)
        .Where(group => group.Key.Length == 0 || group.Count() > 1)
        .Select(group => group.Key).FirstOrDefault();
      if (duplicate != null)
        throw new ArgumentException(
          "Stage02B current inputs contain an empty or duplicate propertyId.",
          nameof(currentInputs));

      string[] failed = (lastResult.FailedPropertyIds ?? Array.Empty<string>())
        .Select(Clean).Where(value => value.Length > 0)
        .Distinct(StringComparer.Ordinal).ToArray();
      var byId = inputs.ToDictionary(
        value => Clean(value.PropertyId),
        value => value,
        StringComparer.Ordinal);
      if (failed.Any(value => !byId.ContainsKey(value)))
        throw new InvalidOperationException("RETRY_INPUT_MISSING");

      var failedSet = new HashSet<string>(failed, StringComparer.Ordinal);
      string[] catalogOrder = NativeStage02BMetricCatalog.Current
        .MetricsFor("总平模型")
        .Select(value => value.PropertyId)
        .Where(failedSet.Contains)
        .Concat(inputs.Select(value => Clean(value.PropertyId))
          .Where(value => failedSet.Contains(value)))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
      NativeStage02BMetricInput[] retry = catalogOrder
        .Select(value => new NativeStage02BMetricInput
        {
          PropertyId = value,
          RawValue = byId[value].RawValue ?? string.Empty
        }).ToArray();
      return new NativeStage02BWriteRequest
      {
        RunId = Guid.NewGuid().ToString("N"),
        Metrics = new ReadOnlyCollection<NativeStage02BMetricInput>(retry),
        PropertyIdsToRetry = new ReadOnlyCollection<string>(catalogOrder)
      };
    }

    private static string Clean(string value)
    {
      return (value ?? string.Empty).Trim();
    }
  }

  internal static class NativeStage02BResultCanonicalizer
  {
    internal static NativeWorkflowResultEnvelope Build(
      string runId,
      NativeWorkflowIdentity identity,
      NativeStage02BStorageSnapshot snapshot,
      IEnumerable<string> attemptedPropertyIds,
      IEnumerable<NativeStage02BMetricOutcome> outcomes,
      string updatedUtc)
    {
      if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
      NativeStage02BMetricDefinition[] metrics = NativeStage02BMetricCatalog
        .Current.MetricsFor(identity?.ModelFileType).ToArray();
      if (metrics.Length != 6)
        throw new InvalidOperationException("STAGE02B_METRIC_CATALOG_INVALID");
      NativeStage02BMetricRecord[] records = (snapshot.Records
          ?? Array.Empty<NativeStage02BMetricRecord>())
        .Where(value => value != null).ToArray();
      var expected = new HashSet<string>(
        metrics.Select(value => value.PropertyId), StringComparer.Ordinal);
      if (records.Length != metrics.Length
        || records.Select(value => value.PropertyId)
          .Distinct(StringComparer.Ordinal).Count() != metrics.Length
        || records.Any(value => !expected.Contains(value.PropertyId)))
        throw new InvalidOperationException("STAGE02B_FULL_READBACK_REQUIRED");
      if (records.Any(value => !NativeStage02BCanonicalizer.VerifyRecord(value)))
        throw new InvalidOperationException("STAGE02B_RECORD_HASH_MISMATCH");
      NativeStage02BStorageSnapshot resealed = NativeStage02BCanonicalizer
        .SealSnapshot(records);
      if (!string.Equals(resealed.CanonicalJson, snapshot.CanonicalJson,
          StringComparison.Ordinal)
        || !string.Equals(resealed.SnapshotHash, snapshot.SnapshotHash,
          StringComparison.Ordinal))
        throw new InvalidOperationException("STAGE02B_SNAPSHOT_HASH_MISMATCH");

      var attempted = new HashSet<string>((attemptedPropertyIds
          ?? Array.Empty<string>()).Select(Clean), StringComparer.Ordinal);
      NativeStage02BMetricOutcome[] allOutcomes = (outcomes
          ?? Array.Empty<NativeStage02BMetricOutcome>())
        .Where(value => value != null).ToArray();
      if (allOutcomes.GroupBy(value => Clean(value.PropertyId),
          StringComparer.Ordinal).Any(group => group.Count() > 1))
        throw new InvalidOperationException("STAGE02B_OUTCOME_DUPLICATE");
      var outcomeById = allOutcomes.ToDictionary(
        value => Clean(value.PropertyId), value => value, StringComparer.Ordinal);
      if (attempted.Any(value => !expected.Contains(value)
          || !outcomeById.ContainsKey(value))
        || outcomeById.Keys.Any(value => !attempted.Contains(value)))
        throw new InvalidOperationException("STAGE02B_OUTCOME_SET_MISMATCH");

      var recordById = records.ToDictionary(
        value => value.PropertyId, value => value, StringComparer.Ordinal);
      NativeWorkflowItemEvidence[] items = metrics.Select(metric =>
      {
        NativeStage02BMetricRecord record = recordById[metric.PropertyId];
        NativeStage02BMetricOutcome outcome;
        bool wasAttempted = attempted.Contains(metric.PropertyId);
        bool succeeded;
        bool readbackSucceeded;
        string errorCode;
        if (wasAttempted)
        {
          outcome = outcomeById[metric.PropertyId];
          succeeded = outcome.Succeeded;
          readbackSucceeded = outcome.ReadbackSucceeded && outcome.Succeeded;
          errorCode = succeeded ? string.Empty : Clean(outcome.ErrorCode);
        }
        else
        {
          succeeded = string.Equals(record.WriteStatus, "SUCCEEDED",
              StringComparison.Ordinal)
            && string.Equals(record.LastAttemptRunId,
              record.LastSuccessfulRunId, StringComparison.Ordinal);
          readbackSucceeded = succeeded && string.Equals(
            record.ReadbackStatus, "SUCCEEDED", StringComparison.Ordinal);
          errorCode = succeeded && readbackSucceeded
            ? string.Empty : Clean(record.ErrorCode);
        }
        return new NativeWorkflowItemEvidence
        {
          Identity = metric.Identity,
          CurrentValue = record.LastSuccessfulCanonicalValue ?? string.Empty,
          Unit = record.Unit ?? string.Empty,
          Source = string.IsNullOrWhiteSpace(record.Source)
            ? "MANUAL_INPUT" : record.Source,
          WriteSucceeded = succeeded,
          ReadbackSucceeded = readbackSucceeded,
          InputHash = record.ResultHash,
          UpdatedUtc = record.UpdatedUtc,
          ErrorCode = errorCode
        };
      }).ToArray();
      return NativeWorkflowResultCanonicalizer.Build(
        runId,
        "STAGE02B",
        "PROJECT_ACTUAL_METRICS",
        identity,
        snapshot.SnapshotHash,
        items,
        updatedUtc);
    }

    private static string Clean(string value)
    {
      return (value ?? string.Empty).Trim();
    }
  }
}
