using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BIMBaoGui.RevitAddin.Stage02;

namespace BIMBaoGui.RevitAddin.Stage02B
{
  internal static class NativeStage02BAssignmentFreshnessPolicy
  {
    internal static NativeStage02SemanticAssignmentSnapshot Evaluate(
      NativeStage02SemanticAssignmentStorageDecision storage,
      string currentDocumentFingerprint,
      string currentRulePackageSha256,
      IEnumerable<NativeStage02ElementSnapshot> liveSnapshots)
    {
      string currentDocument = Clean(currentDocumentFingerprint);
      string persistedDocument = Clean(
        storage?.Payload?.DocumentFingerprint);
      NativeStage02SemanticAssignmentRecord[] records = (storage?.Payload
          ?.Assignments ?? Array.Empty<NativeStage02SemanticAssignmentRecord>())
        .Where(value => value != null)
        .Select(value => value.Clone())
        .ToArray();
      var liveByUniqueId = (liveSnapshots
          ?? Array.Empty<NativeStage02ElementSnapshot>())
        .Where(value => value != null
          && !string.IsNullOrWhiteSpace(value.UniqueId))
        .GroupBy(value => value.UniqueId, StringComparer.Ordinal)
        .ToDictionary(
          group => group.Key,
          group => group.Single(),
          StringComparer.Ordinal);

      bool current = storage != null
        && storage.State == NativeStage02SemanticAssignmentStorageState.Current
        && (storage.StaleElementUniqueIds?.Count ?? 0) == 0
        && currentDocument.Length > 0
        && string.Equals(
          currentDocument,
          persistedDocument,
          StringComparison.Ordinal)
        && records.All(record => IsCurrent(
          record,
          currentRulePackageSha256,
          liveByUniqueId));
      return new NativeStage02SemanticAssignmentSnapshot
      {
        Current = current,
        CurrentDocumentFingerprint = currentDocument,
        AssignmentDocumentFingerprint = persistedDocument,
        Assignments = new ReadOnlyCollection<NativeStage02SemanticAssignmentRecord>(
          records)
      };
    }

    private static bool IsCurrent(
      NativeStage02SemanticAssignmentRecord record,
      string currentRulePackageSha256,
      IReadOnlyDictionary<string, NativeStage02ElementSnapshot> liveByUniqueId)
    {
      NativeStage02ElementSnapshot live;
      return liveByUniqueId.TryGetValue(record.ElementUniqueId, out live)
        && string.Equals(
          record.RulePackageSha256,
          Clean(currentRulePackageSha256),
          StringComparison.Ordinal)
        && string.Equals(
          record.ElementSnapshotHash,
          NativeStage02ElementSnapshotCanonicalizer.Sha256(live),
          StringComparison.Ordinal);
    }

    private static string Clean(string value)
    {
      return (value ?? string.Empty).Trim();
    }
  }
}
