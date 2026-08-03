using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.Rules;

namespace BIMBaoGui.Stage01.Infrastructure
{
  internal sealed class Stage01RegistryProvider
  {
    private static readonly Lazy<Stage01RegistryProvider> LazyInstance =
      new Lazy<Stage01RegistryProvider>(() =>
        FromDatabase(HbrRuleDatabase.Current));

    private readonly Dictionary<string, FieldDefinition> _fieldByKey;
    private readonly IReadOnlyList<FieldDefault> _defaults;
    private readonly IReadOnlyList<ConditionDefault> _conditionDefaults;
    private readonly string _defaultActiveGroup;

    private Stage01RegistryProvider(
      IReadOnlyList<FieldDefinition> fields,
      IReadOnlyList<ConditionDefinition> conditions,
      IReadOnlyList<FieldDefault> defaults,
      IReadOnlyList<ConditionDefault> conditionDefaults,
      string defaultActiveGroup)
    {
      Fields = fields ?? throw new ArgumentNullException(nameof(fields));
      Conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
      _defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
      _conditionDefaults = conditionDefaults
        ?? throw new ArgumentNullException(nameof(conditionDefaults));
      _defaultActiveGroup = string.IsNullOrWhiteSpace(defaultActiveGroup)
        ? throw new InvalidDataException("HBR Stage01 defaultActiveGroup is empty.")
        : defaultActiveGroup;
      _fieldByKey = new Dictionary<string, FieldDefinition>(StringComparer.Ordinal);
      foreach (FieldDefinition field in fields)
      {
        if (field == null || string.IsNullOrWhiteSpace(field.Key))
          throw new InvalidDataException("HBR Stage01 field is incomplete.");
        if (_fieldByKey.ContainsKey(field.Key))
          throw new InvalidDataException("HBR Stage01 field key is duplicated: " + field.Key);
        _fieldByKey.Add(field.Key, field);
      }
      Groups = fields
        .Select(field => field.Group)
        .Where(group => !string.IsNullOrWhiteSpace(group))
        .Distinct(StringComparer.Ordinal)
        .ToList();
    }

    public static Stage01RegistryProvider Instance => LazyInstance.Value;
    public IReadOnlyList<FieldDefinition> Fields { get; }
    public IReadOnlyList<string> Groups { get; }
    public IReadOnlyList<ConditionDefinition> Conditions { get; }

    internal static Stage01RegistryProvider FromDatabase(
      HbrRuleDatabase database)
    {
      if (database == null) throw new ArgumentNullException(nameof(database));

      var fields = new List<FieldDefinition>();
      var defaults = new List<FieldDefault>();
      foreach (HbrInternalWorkflowField source in
        database.Package.Stage01.InternalWorkflowFields)
      {
        fields.Add(MapInternal(source));
        defaults.Add(MapDefault(
          source.FieldKey,
          source.DefaultStrategy,
          source.DefaultValue));
      }
      foreach (HbrStage01FieldRef source in database.Package.Stage01.FieldRefs)
      {
        if (!database.PropertiesById.TryGetValue(
          source.PropertyId,
          out HbrRuleProperty property))
          throw new InvalidDataException(
            "HBR Stage01 field references unknown propertyId: "
            + source.PropertyId);
        fields.Add(MapProperty(source, property));
        defaults.Add(MapDefault(
          source.FieldKey,
          source.DefaultStrategy,
          source.DefaultValue));
      }

      var conditions = database.Package.Conditions
        .Select(condition => new ConditionDefinition(
          condition.ConditionId,
          condition.DisplayName,
          condition.Group))
        .ToList();
      var conditionDefaults = database.Package.Conditions
        .Select(condition => new ConditionDefault(
          condition.ConditionId,
          condition.DefaultActive))
        .ToList();
      return new Stage01RegistryProvider(
        fields,
        conditions,
        defaults,
        conditionDefaults,
        database.Package.Stage01.DefaultActiveGroup);
    }

    public FieldDefinition GetField(string key)
    {
      return key != null && _fieldByKey.TryGetValue(
        key,
        out FieldDefinition value)
        ? value
        : null;
    }

    public IReadOnlyList<FieldDefinition> FieldsForGroup(
      string group,
      bool showAll)
    {
      IEnumerable<FieldDefinition> query = Fields.Where(field =>
        string.Equals(field.Group, group, StringComparison.Ordinal));
      if (!showAll)
        query = query.Where(field =>
          field.Essential || PlanningTargetCatalog.IsManagedMvdField(field.Key));
      return query.ToList();
    }

    public Stage01Model CreateDefaultModel()
    {
      var model = new Stage01Model { ActiveGroup = _defaultActiveGroup };
      foreach (FieldDefault definition in _defaults)
      {
        switch (definition.Strategy)
        {
          case "NONE":
            break;
          case "STATIC":
            model.SetValue(definition.FieldKey, definition.Value);
            break;
          case "NEW_GUID":
            if (!string.Equals(
              definition.FieldKey,
              Stage01Keys.FileGuid,
              StringComparison.Ordinal))
              throw new InvalidDataException(
                "HBR NEW_GUID is only valid for FileGuid: "
                + definition.FieldKey);
            model.SetValue(
              definition.FieldKey,
              Guid.NewGuid().ToString("D"));
            break;
          default:
            throw new InvalidDataException(
              "Unknown HBR Stage01 default strategy: "
              + definition.Strategy);
        }
      }
      foreach (ConditionDefault condition in _conditionDefaults)
        model.SetCondition(condition.ConditionId, condition.Value);
      return model;
    }

    private static FieldDefinition MapInternal(HbrInternalWorkflowField source)
    {
      return new FieldDefinition
      {
        Key = source.FieldKey,
        Label = source.Label,
        Group = source.UiGroup,
        Kind = ParseKind(source.Type),
        ReadOnly = source.SourceKind == "system_generated"
          || source.SourceKind == "system_rule"
          || source.SourceKind == "Revit_scan",
        Essential = source.Essential,
        Deferred = false,
        Source = source.SourceKind,
        Entity = "Workflow",
        Pset = "HBR",
        AllowedValues = source.AllowedValues
      };
    }

    private static FieldDefinition MapProperty(
      HbrStage01FieldRef source,
      HbrRuleProperty property)
    {
      bool structuredPlanningTarget =
        PlanningTargetCatalog.IsManagedMvdField(source.FieldKey);
      return new FieldDefinition
      {
        Key = source.FieldKey,
        Label = property.Ifc.Property,
        Group = source.UiGroup,
        Kind = structuredPlanningTarget
          ? FieldKind.Text
          : ParseIfcKind(property.Ifc.DeclaredType),
        ReadOnly = structuredPlanningTarget
          ? false
          : !source.WriteInStage01
            || source.SourceKind == "later_model_calculation_or_external_value",
        Essential = source.Essential,
        Deferred = structuredPlanningTarget ? false : !source.WriteInStage01,
        Source = structuredPlanningTarget
          ? "structured_planning_target"
          : source.SourceKind,
        Entity = property.Ifc.Entity,
        Pset = property.Ifc.PropertySet,
        AllowedValues = Array.Empty<string>()
      };
    }

    private static FieldDefault MapDefault(
      string fieldKey,
      string strategy,
      string value)
    {
      switch (strategy)
      {
        case "NONE":
          return new FieldDefault(fieldKey, strategy, value);
        case "STATIC":
          if (value == null)
            throw new InvalidDataException(
              "HBR STATIC default has null value: " + fieldKey);
          return new FieldDefault(fieldKey, strategy, value);
        case "NEW_GUID":
          if (!string.Equals(fieldKey, Stage01Keys.FileGuid, StringComparison.Ordinal))
            throw new InvalidDataException(
              "HBR NEW_GUID is only valid for FileGuid: " + fieldKey);
          return new FieldDefault(fieldKey, strategy, value);
        default:
          throw new InvalidDataException(
            "Unknown HBR Stage01 default strategy: " + strategy);
      }
    }

    private static FieldKind ParseKind(string value)
    {
      switch ((value ?? string.Empty).Trim().ToLowerInvariant())
      {
        case "number": return FieldKind.Number;
        case "integer": return FieldKind.Integer;
        case "boolean": return FieldKind.Boolean;
        case "enum": return FieldKind.Enum;
        case "guid": return FieldKind.Guid;
        default: return FieldKind.Text;
      }
    }

    private static FieldKind ParseIfcKind(string value)
    {
      string normalized = (value ?? string.Empty).ToLowerInvariant();
      if (normalized.Contains("real") || normalized.Contains("measure"))
        return FieldKind.Number;
      if (normalized.Contains("integer")) return FieldKind.Integer;
      if (normalized.Contains("boolean")) return FieldKind.Boolean;
      if (normalized.Contains("date")) return FieldKind.DateTime;
      return FieldKind.Text;
    }

    private sealed class FieldDefault
    {
      internal FieldDefault(string fieldKey, string strategy, string value)
      {
        FieldKey = fieldKey;
        Strategy = strategy;
        Value = value;
      }

      internal string FieldKey { get; }
      internal string Strategy { get; }
      internal string Value { get; }
    }

    private sealed class ConditionDefault
    {
      internal ConditionDefault(string conditionId, bool value)
      {
        ConditionId = conditionId;
        Value = value;
      }

      internal string ConditionId { get; }
      internal bool Value { get; }
    }
  }
}
