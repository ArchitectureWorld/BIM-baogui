using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.Stage01.Mvd
{
  internal sealed class HbrIfcFieldInspector
  {
    public HbrIfcFieldInspectionResult Inspect(
      IfcStepDocument document,
      HbrIfcEnrichmentValue value)
    {
      return InspectMany(document, new[] { value }).Fields.Single();
    }

    public HbrIfcBatchInspectionResult InspectMany(
      IfcStepDocument document,
      IEnumerable<HbrIfcEnrichmentValue> values)
    {
      return InspectMany(document, values, null);
    }

    internal HbrIfcBatchInspectionResult InspectMany(
      IfcStepDocument document,
      IEnumerable<HbrIfcEnrichmentValue> values,
      IHbrIfcOperationObserver observer)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (values == null) throw new ArgumentNullException(nameof(values));
      HbrIfcEnrichmentValue[] materialized = values.ToArray();
      return HbrIfcEnricher.InspectExistingFields(
        document,
        materialized,
        observer);
    }
  }
}
