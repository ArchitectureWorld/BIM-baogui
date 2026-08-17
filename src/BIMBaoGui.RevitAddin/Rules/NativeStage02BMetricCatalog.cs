using System;
using System.Collections.Generic;

namespace BIMBaoGui.RevitAddin.Rules
{
  internal sealed class NativeStage02BMetricDefinition
  {
    internal string PropertyId { get; set; } = string.Empty;
    internal string Identity { get; set; } = string.Empty;
    internal int Sequence { get; set; }
    internal string Source { get; set; } = string.Empty;
    internal NativeStage02PropertyDefinition Property { get; set; }
    internal bool OfficialExportVerified { get; set; }
    internal NativeOfficialCarrierEvidenceStatus OfficialCarrierStatus { get; set; }
    internal string OfficialProjectionCarrierId { get; set; } = string.Empty;
    internal string OfficialCarrierProbeRef { get; set; } = string.Empty;
    internal string OfficialEvidenceRef { get; set; } = string.Empty;
  }

  internal sealed class NativeStage02BMetricCatalog
  {
    private static readonly Lazy<NativeStage02BMetricCatalog> LazyCurrent =
      new Lazy<NativeStage02BMetricCatalog>(
        () => new NativeStage02BMetricCatalog(NativeReportingRuleCatalog.Current),
        true);

    private readonly NativeReportingRuleCatalog _reporting;

    private NativeStage02BMetricCatalog(NativeReportingRuleCatalog reporting)
    {
      _reporting = reporting ?? throw new ArgumentNullException(nameof(reporting));
    }

    internal static NativeStage02BMetricCatalog Current => LazyCurrent.Value;

    internal IReadOnlyList<NativeStage02BMetricDefinition> MetricsFor(
      string modelFileType)
    {
      return string.Equals(modelFileType, "总平模型", StringComparison.Ordinal)
        ? _reporting.Stage02BMetrics
        : Array.Empty<NativeStage02BMetricDefinition>();
    }
  }
}
