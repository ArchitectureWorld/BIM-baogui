using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal sealed class NativeStage01ConditionSchemaReconciliation
  {
    internal NativeStage01ConditionSchemaReconciliation(
      IEnumerable<string> addedConditionIds)
    {
      AddedConditionIds = new ReadOnlyCollection<string>((addedConditionIds
        ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray());
    }

    internal IReadOnlyList<string> AddedConditionIds { get; }
    internal bool Changed => AddedConditionIds.Count > 0;
  }

  internal static class NativeStage01ConditionSchemaPolicy
  {
    internal static NativeStage01ConditionSchemaReconciliation Reconcile(
      NativeStage01Model model,
      NativeRuleCatalog catalog)
    {
      if (model == null) throw new ArgumentNullException(nameof(model));
      if (catalog == null) throw new ArgumentNullException(nameof(catalog));

      var added = new List<string>();
      foreach (NativeConditionDefinition condition in catalog.Conditions
        .OrderBy(value => value.ConditionId, StringComparer.Ordinal))
      {
        if (model.Conditions.ContainsKey(condition.ConditionId)) continue;
        model.SetCondition(condition.ConditionId, false);
        added.Add(condition.ConditionId);
      }
      return new NativeStage01ConditionSchemaReconciliation(added);
    }
  }
}
