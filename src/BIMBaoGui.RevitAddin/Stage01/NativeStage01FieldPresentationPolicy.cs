using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal sealed class NativeStage01FieldPresentation
  {
    internal string FieldKey { get; set; } = string.Empty;
    internal string Identity { get; set; } = string.Empty;
    internal string Label { get; set; } = string.Empty;
    internal string CurrentValue { get; set; } = string.Empty;
    internal string Unit { get; set; } = string.Empty;
    internal string Source { get; set; } = string.Empty;
    internal bool InCurrentChecklist { get; set; }
    internal bool ReadOnly { get; set; }
    internal NativeStage01FieldOperationState WriteState { get; set; }
    internal NativeStage01FieldOperationState ReadbackState { get; set; }
    internal string IssueCode { get; set; } = string.Empty;
    internal string IssueMessage { get; set; } = string.Empty;
    internal string NavigationTarget { get; set; } = string.Empty;
  }

  internal static class NativeStage01FieldPresentationPolicy
  {
    internal const string TotalBuildingAreaIdentity =
      "IfcProject|Pset_登记信息属性集|总建筑面积";

    internal static NativeStage01FieldPresentation Build(
      NativeStage01FieldDefinition field,
      NativeStage01Model model,
      NativeStage01LiveEvidence live,
      IReadOnlyDictionary<string, NativeStage01FieldOutcome> outcomes,
      NativeWorkflowResultEnvelope stage02BResult)
    {
      if (field == null) throw new ArgumentNullException(nameof(field));
      model = model ?? new NativeStage01Model();
      live = live ?? new NativeStage01LiveEvidence();
      outcomes = outcomes
        ?? new Dictionary<string, NativeStage01FieldOutcome>(StringComparer.Ordinal);
      bool isStage02BReference = string.Equals(
        field.FieldKey,
        TotalBuildingAreaIdentity,
        StringComparison.Ordinal);
      var presentation = new NativeStage01FieldPresentation
      {
        FieldKey = field.FieldKey,
        Identity = field.FieldKey,
        Label = field.Label,
        Unit = field.CanonicalUnit,
        ReadOnly = field.ReadOnly || field.Deferred || isStage02BReference,
        InCurrentChecklist = IsInCurrentChecklist(field, model),
        Source = isStage02BReference
          ? "STAGE02B_REFERENCE"
          : "STAGE01_INPUT",
        NavigationTarget = isStage02BReference ? "02B" : "01"
      };

      NativeWorkflowItemEvidence stage02BItem = isStage02BReference
        ? (stage02BResult?.Items ?? Array.Empty<NativeWorkflowItemEvidence>())
          .FirstOrDefault(item => item != null
            && string.Equals(
              item.Identity,
              field.FieldKey,
              StringComparison.Ordinal)
            && item.WriteSucceeded
            && item.ReadbackSucceeded)
        : null;
      if (stage02BItem != null)
      {
        presentation.CurrentValue = stage02BItem.CurrentValue;
        presentation.Unit = string.IsNullOrWhiteSpace(stage02BItem.Unit)
          ? field.CanonicalUnit
          : stage02BItem.Unit;
        presentation.Source = "STAGE02B_REFERENCE";
        presentation.WriteState = NativeStage01FieldOperationState.Succeeded;
        presentation.ReadbackState = NativeStage01FieldOperationState.Succeeded;
        return presentation;
      }
      if (isStage02BReference)
      {
        presentation.CurrentValue = string.Empty;
        presentation.WriteState = NativeStage01FieldOperationState.NotAttempted;
        presentation.ReadbackState = NativeStage01FieldOperationState.NotAttempted;
        presentation.IssueCode = "STAGE02B_NOT_COMPLETED";
        presentation.IssueMessage = "02B 尚未形成成功写入并回读的当前总建筑面积。";
        return presentation;
      }

      string liveValue;
      if (TryGetLiveValue(field.FieldKey, live, out liveValue))
      {
        presentation.CurrentValue = liveValue;
        presentation.Source = "REVIT_LIVE";
        presentation.WriteState = NativeStage01FieldOperationState.NotAttempted;
        presentation.ReadbackState = NativeStage01FieldOperationState.Succeeded;
        NativeStage01FieldOutcome liveOutcome;
        if (outcomes.TryGetValue(field.FieldKey, out liveOutcome)
          && liveOutcome != null)
        {
          presentation.WriteState = liveOutcome.WriteState;
          presentation.ReadbackState = liveOutcome.ReadbackState;
          presentation.IssueCode = liveOutcome.ErrorCode;
          presentation.IssueMessage = liveOutcome.Message;
        }
        return presentation;
      }

      NativeStage01FieldOutcome outcome;
      if (outcomes.TryGetValue(field.FieldKey, out outcome) && outcome != null)
      {
        presentation.CurrentValue = outcome.CurrentValue;
        presentation.Unit = string.IsNullOrWhiteSpace(outcome.Unit)
          ? field.CanonicalUnit
          : outcome.Unit;
        presentation.Source = string.IsNullOrWhiteSpace(outcome.Source)
          ? "STAGE01"
          : outcome.Source;
        presentation.WriteState = outcome.WriteState;
        presentation.ReadbackState = outcome.ReadbackState;
        presentation.IssueCode = outcome.ErrorCode;
        presentation.IssueMessage = outcome.Message;
        return presentation;
      }

      presentation.CurrentValue = GetModelValue(field, model);
      return presentation;
    }

    private static bool IsInCurrentChecklist(
      NativeStage01FieldDefinition field,
      NativeStage01Model model)
    {
      string modelFileType = model.GetValue(NativeStage01Keys.ModelFileType);
      if (string.IsNullOrWhiteSpace(modelFileType)) return false;
      return NativeReportingRuleCatalog.Current.GetChecks(modelFileType)
        .Any(check => string.Equals(
              check.FieldKey,
              field.FieldKey,
              StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(field.PropertyId)
              && string.Equals(
                check.PropertyId,
                field.PropertyId,
                StringComparison.Ordinal)));
    }

    private static string GetModelValue(
      NativeStage01FieldDefinition field,
      NativeStage01Model model)
    {
      if (IsPlanningTarget(field))
      {
        NativePlanningTargetValue target;
        return model.PlanningTargets.TryGetValue(field.PropertyId, out target)
          ? target?.MvdText ?? string.Empty
          : string.Empty;
      }
      if (field.IsOrganization)
        return model.GetOrganizationValue(0, field.FieldKey);
      return model.GetValue(field.FieldKey);
    }

    internal static bool IsPlanningTarget(NativeStage01FieldDefinition field)
    {
      return field != null && string.Equals(
        field.SourceKind,
        "GH_planning_condition_input",
        StringComparison.Ordinal);
    }

    private static bool TryGetLiveValue(
      string fieldKey,
      NativeStage01LiveEvidence live,
      out string value)
    {
      value = string.Empty;
      if (string.Equals(fieldKey, NativeStage01Keys.ProjectName, StringComparison.Ordinal)
        && live.ProjectInformationAvailable) value = live.ProjectName;
      else if (string.Equals(fieldKey, NativeStage01Keys.ProjectNumber, StringComparison.Ordinal)
        && live.ProjectInformationAvailable) value = live.ProjectNumber;
      else if (string.Equals(fieldKey, NativeStage01Keys.BaseX, StringComparison.Ordinal)
        && live.ProjectPositionAvailable) value = live.BaseX;
      else if (string.Equals(fieldKey, NativeStage01Keys.BaseY, StringComparison.Ordinal)
        && live.ProjectPositionAvailable) value = live.BaseY;
      else if (string.Equals(fieldKey, NativeStage01Keys.BaseElevation, StringComparison.Ordinal)
        && live.ProjectPositionAvailable) value = live.BaseElevation;
      else if (string.Equals(fieldKey, NativeStage01Keys.TrueNorthAngle, StringComparison.Ordinal)
        && live.ProjectPositionAvailable) value = live.TrueNorthAngle;
      else if (string.Equals(fieldKey, NativeStage01Keys.Longitude, StringComparison.Ordinal)
        && live.GeoLocationAvailable) value = live.Longitude;
      else if (string.Equals(fieldKey, NativeStage01Keys.Latitude, StringComparison.Ordinal)
        && live.GeoLocationAvailable) value = live.Latitude;
      else if (string.Equals(fieldKey, NativeStage01Keys.LengthUnit, StringComparison.Ordinal)
        && live.UnitsAvailable) value = live.LengthUnit;
      else if (string.Equals(fieldKey, NativeStage01Keys.AreaUnit, StringComparison.Ordinal)
        && live.UnitsAvailable) value = live.AreaUnit;
      else if (string.Equals(fieldKey, NativeStage01Keys.AngleUnit, StringComparison.Ordinal)
        && live.UnitsAvailable) value = live.AngleUnit;
      else return false;
      return true;
    }
  }
}
