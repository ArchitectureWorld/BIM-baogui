using System;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal enum NativeProjectConditionDeclarationState
  {
    Missing,
    ActualConditionsSelected,
    NoConditionsSelected,
    Conflict
  }

  internal sealed class NativeProjectConditionDeclarationDecision
  {
    internal NativeProjectConditionDeclarationState State { get; set; }
    internal int SelectedActualCount { get; set; }
    internal bool NoConditionsSelected { get; set; }
    internal bool IsValid =>
      State == NativeProjectConditionDeclarationState.ActualConditionsSelected
      || State == NativeProjectConditionDeclarationState.NoConditionsSelected;
  }

  internal static class NativeProjectConditionDeclarationPolicy
  {
    internal const string NoneConditionId =
      "workflow.project_conditions.none";
    internal const string NoneDisplayName =
      "无上述项目条件（已确认）";

    internal static NativeProjectConditionDeclarationDecision Evaluate(
      NativeStage01Model model,
      NativeRuleCatalog catalog)
    {
      if (model == null) throw new ArgumentNullException(nameof(model));
      if (catalog == null) throw new ArgumentNullException(nameof(catalog));

      int selectedActualCount = catalog.Conditions.Count(condition =>
        model.GetCondition(condition.ConditionId));
      bool noConditionsSelected = model.GetCondition(NoneConditionId);
      NativeProjectConditionDeclarationState state;
      if (selectedActualCount > 0 && noConditionsSelected)
        state = NativeProjectConditionDeclarationState.Conflict;
      else if (selectedActualCount > 0)
        state = NativeProjectConditionDeclarationState.ActualConditionsSelected;
      else if (noConditionsSelected)
        state = NativeProjectConditionDeclarationState.NoConditionsSelected;
      else
        state = NativeProjectConditionDeclarationState.Missing;

      return new NativeProjectConditionDeclarationDecision
      {
        State = state,
        SelectedActualCount = selectedActualCount,
        NoConditionsSelected = noConditionsSelected
      };
    }

    internal static void SetActualCondition(
      NativeStage01Model model,
      NativeRuleCatalog catalog,
      string conditionId,
      bool selected)
    {
      if (model == null) throw new ArgumentNullException(nameof(model));
      if (catalog == null) throw new ArgumentNullException(nameof(catalog));
      if (string.IsNullOrWhiteSpace(conditionId)
        || !catalog.Conditions.Any(condition => string.Equals(
          condition.ConditionId,
          conditionId,
          StringComparison.Ordinal)))
      {
        throw new ArgumentException(
          "conditionId 不是当前 HBR 数据库中的实际项目条件。",
          nameof(conditionId));
      }

      model.SetCondition(conditionId, selected);
      if (selected)
        model.SetCondition(NoneConditionId, false);
    }

    internal static void SetNoConditions(
      NativeStage01Model model,
      NativeRuleCatalog catalog,
      bool selected)
    {
      if (model == null) throw new ArgumentNullException(nameof(model));
      if (catalog == null) throw new ArgumentNullException(nameof(catalog));

      model.SetCondition(NoneConditionId, selected);
      if (!selected) return;
      foreach (NativeConditionDefinition condition in catalog.Conditions)
        model.SetCondition(condition.ConditionId, false);
    }
  }
}
