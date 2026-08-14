using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal sealed class NativeStage02SemanticAssignmentReadResult
  {
    internal NativeStage02SemanticAssignmentStorageState State { get; set; }
    internal string Message { get; set; } = string.Empty;
    internal IReadOnlyDictionary<string, NativeStage02SemanticAssignmentRecord>
      AssignmentsByElement { get; set; } =
        new ReadOnlyDictionary<string, NativeStage02SemanticAssignmentRecord>(
          new Dictionary<string, NativeStage02SemanticAssignmentRecord>(
            StringComparer.Ordinal));
    internal IReadOnlyList<string> StaleElementUniqueIds { get; set; } =
      Array.Empty<string>();
  }

  internal static class NativeStage02SemanticAssignmentRevitService
  {
    internal static NativeStage02SemanticAssignmentReadResult Read(
      Document document)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      string[] existingUniqueIds = new FilteredElementCollector(document)
        .WhereElementIsNotElementType()
        .Select(element => element.UniqueId)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      NativeStage02SemanticAssignmentStorageDecision decision =
        NativeStage02SemanticAssignmentStoragePolicy.Evaluate(
          NativeStage02SemanticAssignmentStorage.Read(document),
          existingUniqueIds);
      var byElement = new Dictionary<string, NativeStage02SemanticAssignmentRecord>(
        StringComparer.Ordinal);
      if (decision.Payload != null)
      {
        foreach (NativeStage02SemanticAssignmentRecord record in
          decision.Payload.Assignments ?? Array.Empty<NativeStage02SemanticAssignmentRecord>())
        {
          if (record == null || string.IsNullOrWhiteSpace(record.ElementUniqueId))
            continue;
          byElement[record.ElementUniqueId] = record.Clone();
        }
      }
      return new NativeStage02SemanticAssignmentReadResult
      {
        State = decision.State,
        Message = decision.Message,
        AssignmentsByElement =
          new ReadOnlyDictionary<string, NativeStage02SemanticAssignmentRecord>(
            byElement),
        StaleElementUniqueIds = decision.StaleElementUniqueIds
          ?? Array.Empty<string>()
      };
    }
  }
}
