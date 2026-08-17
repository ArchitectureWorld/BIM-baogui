using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage02B
{
  internal sealed class NativeStage02BViewModel
  {
    private readonly IReadOnlyList<NativeStage02BMetricDefinition> _metrics;
    private readonly Dictionary<string, NativeStage02BMetricRecord> _records =
      new Dictionary<string, NativeStage02BMetricRecord>(StringComparer.Ordinal);
    private readonly Dictionary<string, NativeStage02BMetricOutcome> _outcomes =
      new Dictionary<string, NativeStage02BMetricOutcome>(StringComparer.Ordinal);

    internal NativeStage02BViewModel()
    {
      _metrics = NativeStage02BMetricCatalog.Current.MetricsFor("总平模型");
      Inputs = new ReadOnlyCollection<NativeStage02BMetricInput>(_metrics
        .Select(value => new NativeStage02BMetricInput
        {
          PropertyId = value.PropertyId
        }).ToArray());
    }

    internal IReadOnlyList<NativeStage02BMetricDefinition> Metrics => _metrics;
    internal IReadOnlyList<NativeStage02BMetricInput> Inputs { get; }
    internal NativeStage02BWriteResult LastWriteResult { get; private set; }

    internal NativeStage02BWriteRequest BuildSaveAllRequest()
    {
      return new NativeStage02BWriteRequest
      {
        RunId = Guid.NewGuid().ToString("N"),
        Metrics = new ReadOnlyCollection<NativeStage02BMetricInput>(Inputs
          .Select(CloneInput).ToArray()),
        PropertyIdsToRetry = Array.Empty<string>()
      };
    }

    internal NativeStage02BWriteRequest BuildRetryRequest(
      NativeStage02BWriteResult lastResult)
    {
      return NativeStage02BWriteBatchPolicy.BuildRetry(
        lastResult, Inputs.ToArray());
    }

    internal void ApplyRead(NativeStage02BReadResult result)
    {
      _records.Clear();
      foreach (NativeStage02BMetricRecord record in result?.Records
        ?? Array.Empty<NativeStage02BMetricRecord>())
      {
        if (record == null) continue;
        _records[record.PropertyId] = record;
      }
      foreach (NativeStage02BMetricInput input in Inputs)
      {
        if (string.IsNullOrWhiteSpace(input.RawValue)
          && _records.TryGetValue(input.PropertyId,
            out NativeStage02BMetricRecord record))
          input.RawValue = record.LastSuccessfulCanonicalValue;
      }
    }

    internal void ApplyWrite(NativeStage02BWriteResult result)
    {
      LastWriteResult = result;
      _outcomes.Clear();
      foreach (NativeStage02BMetricOutcome outcome in result?.MetricOutcomes
        ?? Array.Empty<NativeStage02BMetricOutcome>())
      {
        if (outcome == null) continue;
        _outcomes[outcome.PropertyId] = outcome;
        if (outcome.Record != null) _records[outcome.PropertyId] = outcome.Record;
      }
    }

    internal NativeStage02BMetricRecord RecordFor(string propertyId)
    {
      _records.TryGetValue(propertyId ?? string.Empty,
        out NativeStage02BMetricRecord value);
      return value;
    }

    internal NativeStage02BMetricOutcome OutcomeFor(string propertyId)
    {
      _outcomes.TryGetValue(propertyId ?? string.Empty,
        out NativeStage02BMetricOutcome value);
      return value;
    }

    private static NativeStage02BMetricInput CloneInput(
      NativeStage02BMetricInput value)
    {
      return new NativeStage02BMetricInput
      {
        PropertyId = value?.PropertyId ?? string.Empty,
        RawValue = value?.RawValue ?? string.Empty
      };
    }
  }
}
