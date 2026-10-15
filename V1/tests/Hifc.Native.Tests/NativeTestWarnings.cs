using System.Collections.Generic;
using Autodesk.Revit.DB;

// Only attached to the rollback-only acceptance fixture transactions.
// Warnings are recorded, errors remain untouched and cannot be auto-accepted.
public sealed class NativeTestWarnings : IFailuresPreprocessor
{
    public static readonly List<string> Observed = new List<string>();
    public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
    {
        foreach (var failure in accessor.GetFailureMessages())
        {
            if (failure.GetSeverity() != FailureSeverity.Warning) continue;
            Observed.Add(failure.GetDescriptionText());
            accessor.DeleteWarning(failure);
        }
        return FailureProcessingResult.Continue;
    }
}
