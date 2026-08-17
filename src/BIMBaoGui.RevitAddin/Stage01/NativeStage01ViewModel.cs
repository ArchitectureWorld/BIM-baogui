using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal sealed class NativeStage01ViewModel
  {
    internal const string ConditionsGroup = "10_项目条件";
    internal const string RegistrationGroup = "项目登记信息";
    internal const string LocationGroup = "项目位置与坐标";
    internal const string PlanningGroup = "规划目标与限值";
    internal const string OtherInputsGroup = "其他项目输入";
    private readonly NativeRuleCatalog _catalog;
    private NativeStage01Model _model;

    internal NativeStage01ViewModel(NativeRuleCatalog catalog)
    {
      _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
      _model = catalog.CreateDefaultStage01Model();
      var groups = new List<string>();
      groups.Add(ConditionsGroup);
      groups.Add(RegistrationGroup);
      groups.Add(LocationGroup);
      groups.Add(PlanningGroup);
      groups.Add(OtherInputsGroup);
      Groups = new ReadOnlyCollection<string>(groups);
      ActiveGroup = ConditionsGroup;
    }

    internal NativeStage01Model Model => _model;
    internal IReadOnlyList<string> Groups { get; }
    internal string ActiveGroup { get; private set; }
    internal int OrganizationIndex { get; private set; }
    internal int OrganizationDisplayIndex =>
      _model.Organizations.Count == 0 ? 0 : OrganizationIndex + 1;
    internal bool IsDirty { get; private set; }
    internal NativeStage01ValidationResult Validation { get; private set; }
    internal NativeStage01LiveEvidence LiveEvidence { get; private set; } =
      new NativeStage01LiveEvidence();
    internal IReadOnlyList<NativeStage01Drift> Drifts { get; private set; } =
      Array.Empty<NativeStage01Drift>();
    internal IReadOnlyDictionary<string, NativeStage01FieldOutcome>
      FieldOutcomes { get; private set; } =
        new ReadOnlyDictionary<string, NativeStage01FieldOutcome>(
          new Dictionary<string, NativeStage01FieldOutcome>(StringComparer.Ordinal));
    internal NativeWorkflowResultEnvelope WorkflowResult { get; private set; }
    internal NativeWorkflowResultEnvelope Stage02BResult { get; private set; }
    internal bool RequiresMigrationConfirmation { get; private set; }
    internal string SourcePayloadVersion { get; private set; } = string.Empty;
    internal NativeStage01StorageState StorageState { get; private set; } =
      NativeStage01StorageState.NoRecord;
    internal IReadOnlyList<NativeStage01FieldDefinition> ActiveFields =>
      FieldsForGroup(ActiveGroup);
    internal IReadOnlyList<NativeConditionDefinition> Conditions =>
      _catalog.Conditions;

    internal IReadOnlyList<NativeStage01FieldDefinition> FieldsForGroup(
      string group)
    {
      return _catalog.Stage01Fields
        .Where(value => string.Equals(GroupForField(value), group, StringComparison.Ordinal))
        .ToArray();
    }

    internal static string GroupForField(NativeStage01FieldDefinition field)
    {
      if (field == null) return OtherInputsGroup;
      if (NativeStage01FieldPresentationPolicy.IsPlanningTarget(field))
        return PlanningGroup;
      if (IsLocationField(field)) return LocationGroup;
      if (string.Equals(field.UiGroup, "01_文件与项目身份", StringComparison.Ordinal)
        || string.Equals(field.UiGroup, "07_登记信息", StringComparison.Ordinal))
      {
        return RegistrationGroup;
      }
      return OtherInputsGroup;
    }

    private static bool IsLocationField(NativeStage01FieldDefinition field)
    {
      return string.Equals(field.UiGroup, "02_坐标与高程", StringComparison.Ordinal)
        || string.Equals(field.FieldKey, NativeStage01Keys.Longitude, StringComparison.Ordinal)
        || string.Equals(field.FieldKey, NativeStage01Keys.Latitude, StringComparison.Ordinal)
        || string.Equals(field.FieldKey, NativeStage01Keys.TrueNorthAngle, StringComparison.Ordinal)
        || string.Equals(field.FieldKey, NativeStage01Keys.LengthUnit, StringComparison.Ordinal)
        || string.Equals(field.FieldKey, NativeStage01Keys.AreaUnit, StringComparison.Ordinal)
        || string.Equals(field.FieldKey, NativeStage01Keys.AngleUnit, StringComparison.Ordinal);
    }

    internal void SetActiveGroup(string group)
    {
      if (string.IsNullOrWhiteSpace(group)
        || !Groups.Contains(group, StringComparer.Ordinal))
        return;
      ActiveGroup = group;
    }

    internal string GetFieldValue(NativeStage01FieldDefinition field)
    {
      if (field == null) return string.Empty;
      if (NativeStage01FieldPresentationPolicy.IsPlanningTarget(field))
      {
        NativePlanningTargetValue target;
        return _model.PlanningTargets.TryGetValue(field.PropertyId, out target)
          ? target?.MvdText ?? string.Empty
          : string.Empty;
      }
      return field.IsOrganization
        ? _model.GetOrganizationValue(OrganizationIndex, field.FieldKey)
        : _model.GetValue(field.FieldKey);
    }

    internal void SetFieldValue(
      NativeStage01FieldDefinition field,
      string value)
    {
      if (field == null || field.ReadOnly || field.Deferred) return;
      if (field.IsOrganization)
        _model.SetOrganizationValue(OrganizationIndex, field.FieldKey, value);
      else
        _model.SetValue(field.FieldKey, value);
      MarkEdited();
    }

    internal NativePlanningTargetValue GetPlanningTarget(
      NativeStage01FieldDefinition field)
    {
      if (!NativeStage01FieldPresentationPolicy.IsPlanningTarget(field))
        return null;
      NativePlanningTargetValue target;
      return _model.PlanningTargets.TryGetValue(field.PropertyId, out target)
        ? target
        : null;
    }

    internal void SetPlanningTarget(
      NativeStage01FieldDefinition field,
      string @operator,
      string value1,
      string value2,
      string unit)
    {
      if (!NativeStage01FieldPresentationPolicy.IsPlanningTarget(field))
        throw new ArgumentException("Field is not a planning target.", nameof(field));
      string first = (value1 ?? string.Empty).Trim();
      string second = (value2 ?? string.Empty).Trim();
      if (first.Length == 0 && second.Length == 0)
      {
        _model.PlanningTargets.Remove(field.PropertyId);
        MarkEdited();
        return;
      }
      string normalizedOperator = NormalizePlanningOperator(@operator);
      _model.PlanningTargets[field.PropertyId] = new NativePlanningTargetValue(
        normalizedOperator,
        first,
        second,
        unit ?? string.Empty,
        "USER_INPUT",
        FormatPlanningTarget(normalizedOperator, first, second));
      _model.Values.Remove(field.FieldKey);
      MarkEdited();
    }

    private static string NormalizePlanningOperator(string value)
    {
      switch ((value ?? string.Empty).Trim())
      {
        case "≤":
        case "LessOrEqual": return "LessOrEqual";
        case "≥":
        case "GreaterOrEqual": return "GreaterOrEqual";
        case "=":
        case "Equal": return "Equal";
        case "区间":
        case "Range": return "Range";
        default: throw new ArgumentException("Unsupported planning operator.", nameof(value));
      }
    }

    private static string FormatPlanningTarget(
      string @operator,
      string value1,
      string value2)
    {
      switch (@operator)
      {
        case "LessOrEqual": return "≤" + value1;
        case "GreaterOrEqual": return "≥" + value1;
        case "Equal": return "=" + value1;
        case "Range": return value1 + "–" + value2;
        default: return value1;
      }
    }

    internal void SetCondition(string conditionId, bool value)
    {
      NativeProjectConditionDeclarationPolicy.SetActualCondition(
        _model,
        _catalog,
        conditionId,
        value);
      MarkEdited();
    }

    internal bool GetCondition(string conditionId)
    {
      return _model.GetCondition(conditionId);
    }

    internal void SetNoConditions(bool value)
    {
      NativeProjectConditionDeclarationPolicy.SetNoConditions(
        _model,
        _catalog,
        value);
      MarkEdited();
    }

    internal bool GetNoConditions()
    {
      return _model.GetCondition(
        NativeProjectConditionDeclarationPolicy.NoneConditionId);
    }

    internal NativeProjectConditionDeclarationDecision
      GetConditionDeclarationDecision()
    {
      return NativeProjectConditionDeclarationPolicy.Evaluate(
        _model,
        _catalog);
    }

    internal int GetMissingRequiredCount(string group)
    {
      if (string.Equals(group, ConditionsGroup, StringComparison.Ordinal))
        return GetConditionDeclarationDecision().IsValid ? 0 : 1;
      return FieldsForGroup(group)
        .Where(NativeStage01Validator.IsRequired)
        .Count(field => string.IsNullOrWhiteSpace(GetFieldValue(field)));
    }

    internal int GetOptionalFieldCount(string group)
    {
      return FieldsForGroup(group)
        .Count(field => !NativeStage01Validator.IsRequired(field));
    }

    internal int GetFilledOptionalFieldCount(string group)
    {
      return FieldsForGroup(group)
        .Where(field => !NativeStage01Validator.IsRequired(field))
        .Count(field => !string.IsNullOrWhiteSpace(GetFieldValue(field)));
    }

    internal bool HasOptionalValidationError(string group)
    {
      if (Validation == null) return false;
      var optionalKeys = new HashSet<string>(
        FieldsForGroup(group)
          .Where(field => !NativeStage01Validator.IsRequired(field))
          .Select(field => field.FieldKey),
        StringComparer.Ordinal);
      return Validation.Messages.Any(message =>
        optionalKeys.Contains(message.FieldKey));
    }

    internal NativeStage01ValidationResult Validate()
    {
      Validation = NativeStage01Validator.Validate(_model, _catalog);
      return Validation;
    }

    internal NativeStage01FieldPresentation GetFieldPresentation(
      NativeStage01FieldDefinition field)
    {
      return NativeStage01FieldPresentationPolicy.Build(
        field,
        _model,
        LiveEvidence,
        FieldOutcomes,
        Stage02BResult);
    }

    internal IReadOnlyList<NativeStage01ValidationMessage>
      ValidationMessagesForField(string fieldKey)
    {
      return Validation == null
        ? Array.Empty<NativeStage01ValidationMessage>()
        : Validation.Messages.Where(value => string.Equals(
          value.FieldKey,
          fieldKey,
          StringComparison.Ordinal)).ToArray();
    }

    internal void LoadModel(NativeStage01Model model)
    {
      LoadModelCore(model);
      LiveEvidence = new NativeStage01LiveEvidence();
      Drifts = Array.Empty<NativeStage01Drift>();
      FieldOutcomes = EmptyOutcomes();
      WorkflowResult = null;
      Stage02BResult = null;
      RequiresMigrationConfirmation = false;
      SourcePayloadVersion = string.Empty;
      StorageState = NativeStage01StorageState.NoRecord;
    }

    internal void LoadReadResult(NativeStage01ReadResult result)
    {
      LoadModelCore(result?.Model);
      LiveEvidence = result?.LiveEvidence?.Clone()
        ?? new NativeStage01LiveEvidence();
      Drifts = new ReadOnlyCollection<NativeStage01Drift>((result?.Drifts
        ?? Array.Empty<NativeStage01Drift>()).ToArray());
      WorkflowResult = result?.WorkflowResult;
      Stage02BResult = result?.Stage02BResult;
      FieldOutcomes = OutcomesFromWorkflow(WorkflowResult);
      RequiresMigrationConfirmation =
        result != null && result.RequiresMigrationConfirmation;
      SourcePayloadVersion = result?.SourcePayloadVersion ?? string.Empty;
      StorageState = result?.StorageDecision?.State
        ?? NativeStage01StorageState.NoRecord;
    }

    private void LoadModelCore(NativeStage01Model model)
    {
      _model = (model ?? _catalog.CreateDefaultStage01Model()).Clone();
      OrganizationIndex = 0;
      ActiveGroup = ConditionsGroup;
      Validation = null;
      IsDirty = false;
    }

    internal void AddOrganization()
    {
      _model.Organizations.Add(
        new Dictionary<string, string>(StringComparer.Ordinal));
      OrganizationIndex = _model.Organizations.Count - 1;
      MarkEdited();
    }

    internal void RemoveCurrentOrganization()
    {
      if (_model.Organizations.Count == 0) return;
      if (_model.Organizations.Count == 1)
      {
        _model.Organizations[0].Clear();
        OrganizationIndex = 0;
      }
      else
      {
        _model.Organizations.RemoveAt(OrganizationIndex);
        OrganizationIndex = Math.Max(
          0,
          Math.Min(OrganizationIndex, _model.Organizations.Count - 1));
      }
      MarkEdited();
    }

    internal void MoveOrganization(int delta)
    {
      if (_model.Organizations.Count == 0) return;
      OrganizationIndex = (
        OrganizationIndex + delta + _model.Organizations.Count)
        % _model.Organizations.Count;
    }

    internal void MarkSaved(NativeStage01WriteResult result = null)
    {
      IsDirty = false;
      RequiresMigrationConfirmation = false;
      SourcePayloadVersion = NativeStage01Canonicalizer.PayloadSchemaVersion;
      StorageState = NativeStage01StorageState.Current;
      Drifts = Array.Empty<NativeStage01Drift>();
      WorkflowResult = result?.WorkflowResult;
      if (result != null)
      {
        FieldOutcomes = new ReadOnlyDictionary<string, NativeStage01FieldOutcome>(
          (result.FieldOutcomes ?? Array.Empty<NativeStage01FieldOutcome>())
            .Where(value => value != null)
            .GroupBy(value => value.FieldKey, StringComparer.Ordinal)
            .ToDictionary(
              group => group.Key,
              group => group.Last(),
              StringComparer.Ordinal));
      }
      LiveEvidence = new NativeStage01LiveEvidence
      {
        ProjectInformationAvailable = true,
        ProjectName = _model.GetValue(NativeStage01Keys.ProjectName),
        ProjectNumber = _model.GetValue(NativeStage01Keys.ProjectNumber),
        ProjectPositionAvailable = true,
        BaseX = _model.GetValue(NativeStage01Keys.BaseX),
        BaseY = _model.GetValue(NativeStage01Keys.BaseY),
        BaseElevation = _model.GetValue(NativeStage01Keys.BaseElevation),
        TrueNorthAngle = _model.GetValue(NativeStage01Keys.TrueNorthAngle),
        GeoLocationAvailable = !string.IsNullOrWhiteSpace(
            _model.GetValue(NativeStage01Keys.Longitude))
          && !string.IsNullOrWhiteSpace(
            _model.GetValue(NativeStage01Keys.Latitude)),
        Longitude = _model.GetValue(NativeStage01Keys.Longitude),
        Latitude = _model.GetValue(NativeStage01Keys.Latitude),
        UnitsAvailable = true,
        LengthUnit = _model.GetValue(NativeStage01Keys.LengthUnit),
        AreaUnit = _model.GetValue(NativeStage01Keys.AreaUnit),
        AngleUnit = _model.GetValue(NativeStage01Keys.AngleUnit)
      };
      Validation = NativeStage01Validator.Validate(_model, _catalog);
    }

    private static IReadOnlyDictionary<string, NativeStage01FieldOutcome>
      OutcomesFromWorkflow(NativeWorkflowResultEnvelope result)
    {
      if (result == null) return EmptyOutcomes();
      return new ReadOnlyDictionary<string, NativeStage01FieldOutcome>(
        (result.Items ?? Array.Empty<NativeWorkflowItemEvidence>())
          .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Identity))
          .GroupBy(item => item.Identity, StringComparer.Ordinal)
          .ToDictionary(
            group => group.Key,
            group =>
            {
              NativeWorkflowItemEvidence item = group.Last();
              return new NativeStage01FieldOutcome
              {
                FieldKey = item.Identity,
                Identity = item.Identity,
                CurrentValue = item.CurrentValue,
                Unit = item.Unit,
                Source = item.Source,
                WriteState = ToOperationState(
                  item.WriteSucceeded,
                  item.ErrorCode),
                ReadbackState = ToOperationState(
                  item.ReadbackSucceeded,
                  item.ErrorCode),
                ErrorCode = item.ErrorCode
              };
            },
            StringComparer.Ordinal));
    }

    private static NativeStage01FieldOperationState ToOperationState(
      bool succeeded,
      string errorCode)
    {
      if (succeeded) return NativeStage01FieldOperationState.Succeeded;
      return string.IsNullOrWhiteSpace(errorCode)
        ? NativeStage01FieldOperationState.NotAttempted
        : NativeStage01FieldOperationState.Failed;
    }

    private static IReadOnlyDictionary<string, NativeStage01FieldOutcome>
      EmptyOutcomes()
    {
      return new ReadOnlyDictionary<string, NativeStage01FieldOutcome>(
        new Dictionary<string, NativeStage01FieldOutcome>(StringComparer.Ordinal));
    }

    private void MarkEdited()
    {
      IsDirty = true;
      Validation = null;
    }
  }
}
