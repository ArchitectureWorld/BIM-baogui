using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal sealed class NativeStage01ViewModel
  {
    internal const string ConditionsGroup = "10_项目条件";
    private readonly NativeRuleCatalog _catalog;
    private NativeStage01Model _model;

    internal NativeStage01ViewModel(NativeRuleCatalog catalog)
    {
      _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
      _model = catalog.CreateDefaultStage01Model();
      var groups = new List<string>();
      groups.Add(ConditionsGroup);
      foreach (string group in catalog.Stage01Fields
        .Select(value => value.UiGroup)
        .Where(value => !string.IsNullOrWhiteSpace(value)))
      {
        if (!groups.Contains(group, StringComparer.Ordinal)) groups.Add(group);
      }
      Groups = new ReadOnlyCollection<string>(groups);
      ActiveGroup = ConditionsGroup;
    }

    internal NativeStage01Model Model => _model;
    internal IReadOnlyList<string> Groups { get; }
    internal string ActiveGroup { get; private set; }
    internal int OrganizationIndex { get; private set; }
    internal bool IsDirty { get; private set; }
    internal NativeStage01ValidationResult Validation { get; private set; }
    internal IReadOnlyList<NativeStage01FieldDefinition> ActiveFields =>
      FieldsForGroup(ActiveGroup);
    internal IReadOnlyList<NativeConditionDefinition> Conditions =>
      _catalog.Conditions;

    internal IReadOnlyList<NativeStage01FieldDefinition> FieldsForGroup(
      string group)
    {
      return _catalog.Stage01Fields
        .Where(value => string.Equals(
          value.UiGroup,
          group,
          StringComparison.Ordinal))
        .ToArray();
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
      _model = (model ?? _catalog.CreateDefaultStage01Model()).Clone();
      if (_model.Organizations.Count == 0)
      {
        _model.Organizations.Add(
          new Dictionary<string, string>(StringComparer.Ordinal));
      }
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
      if (_model.Organizations.Count <= 1)
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

    internal void MarkSaved()
    {
      IsDirty = false;
      Validation = NativeStage01Validator.Validate(_model, _catalog);
    }

    private void MarkEdited()
    {
      IsDirty = true;
      Validation = null;
    }
  }
}
