using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using BIMBaoGui.RevitAddin.Stage01;

namespace BIMBaoGui.RevitAddin.Rules
{
  internal enum NativeStage01FieldKind
  {
    Text,
    Number,
    Integer,
    Boolean,
    Enum,
    Guid,
    DateTime
  }

  internal sealed class NativeStage01FieldDefinition
  {
    internal string FieldKey { get; set; } = string.Empty;
    internal string PropertyId { get; set; } = string.Empty;
    internal string Label { get; set; } = string.Empty;
    internal string UiGroup { get; set; } = string.Empty;
    internal NativeStage01FieldKind Kind { get; set; }
    internal string SourceKind { get; set; } = string.Empty;
    internal bool WriteInStage01 { get; set; }
    internal bool Essential { get; set; }
    internal bool ReadOnly { get; set; }
    internal bool Deferred { get; set; }
    internal string DefaultStrategy { get; set; } = string.Empty;
    internal string DefaultValue { get; set; } = string.Empty;
    internal IReadOnlyList<string> AllowedValues { get; set; } =
      Array.Empty<string>();
    internal string IfcEntity { get; set; } = string.Empty;
    internal string IfcPropertySet { get; set; } = string.Empty;
    internal string IfcProperty { get; set; } = string.Empty;
    internal string DeclaredIfcType { get; set; } = string.Empty;
    internal string CanonicalUnit { get; set; } = string.Empty;
    internal Guid? ParameterGuid { get; set; }
    internal string ParameterName { get; set; } = string.Empty;
    internal string StorageType { get; set; } = string.Empty;
    internal string ParameterType { get; set; } = string.Empty;
    internal bool IsOrganization => string.Equals(
      IfcEntity,
      "IfcOrganization",
      StringComparison.Ordinal);
  }

  internal sealed class NativeConditionDefinition
  {
    internal string ConditionId { get; set; } = string.Empty;
    internal string DisplayName { get; set; } = string.Empty;
    internal string Group { get; set; } = string.Empty;
    internal bool DefaultActive { get; set; }
  }

  internal sealed class NativeModelProfile
  {
    internal string ProfileId { get; set; } = string.Empty;
  }

  internal sealed class NativeSpatialMapping
  {
    internal string SourceName { get; set; } = string.Empty;
    internal string FieldKey { get; set; } = string.Empty;
    internal string TargetName { get; set; } = string.Empty;
    internal string Unit { get; set; } = string.Empty;
  }

  internal sealed class NativeRuleCatalog
  {
    private static readonly Lazy<NativeRuleCatalog> LazyCurrent =
      new Lazy<NativeRuleCatalog>(LoadCurrent, true);

    private NativeRuleCatalog(
      RulePackageIdentity identity,
      IEnumerable<NativeStage01FieldDefinition> stage01Fields,
      IEnumerable<NativeConditionDefinition> conditions,
      IEnumerable<NativeModelProfile> modelProfiles,
      IEnumerable<NativeSpatialMapping> spatialMappings,
      string defaultActiveGroup)
    {
      Identity = identity ?? throw new ArgumentNullException(nameof(identity));
      NativeStage01FieldDefinition[] fields = (stage01Fields
        ?? Array.Empty<NativeStage01FieldDefinition>()).ToArray();
      var fieldsByKey = new Dictionary<string, NativeStage01FieldDefinition>(
        StringComparer.Ordinal);
      foreach (NativeStage01FieldDefinition field in fields)
      {
        if (field == null || string.IsNullOrWhiteSpace(field.FieldKey))
          throw new InvalidDataException("HBR Stage01 字段定义不完整。");
        if (fieldsByKey.ContainsKey(field.FieldKey))
          throw new InvalidDataException(
            "HBR Stage01 字段键重复：" + field.FieldKey);
        fieldsByKey.Add(field.FieldKey, field);
      }

      NativeConditionDefinition[] conditionArray = (conditions
        ?? Array.Empty<NativeConditionDefinition>()).ToArray();
      if (conditionArray.Select(value => value.ConditionId)
        .Distinct(StringComparer.Ordinal).Count() != conditionArray.Length)
      {
        throw new InvalidDataException("HBR conditionId 重复。");
      }

      NativeModelProfile[] profileArray = (modelProfiles
        ?? Array.Empty<NativeModelProfile>()).ToArray();
      if (profileArray.Select(value => value.ProfileId)
        .Distinct(StringComparer.Ordinal).Count() != profileArray.Length)
      {
        throw new InvalidDataException("HBR model profile 重复。");
      }

      if (string.IsNullOrWhiteSpace(defaultActiveGroup))
        throw new InvalidDataException("HBR Stage01 defaultActiveGroup 为空。");

      Stage01Fields = new ReadOnlyCollection<NativeStage01FieldDefinition>(
        fields);
      Stage01FieldsByKey =
        new ReadOnlyDictionary<string, NativeStage01FieldDefinition>(
          fieldsByKey);
      Conditions = new ReadOnlyCollection<NativeConditionDefinition>(
        conditionArray);
      ModelProfiles = new ReadOnlyCollection<NativeModelProfile>(
        profileArray);
      SpatialMappings = new ReadOnlyCollection<NativeSpatialMapping>(
        (spatialMappings ?? Array.Empty<NativeSpatialMapping>()).ToArray());
      DefaultActiveGroup = defaultActiveGroup;
    }

    internal static NativeRuleCatalog Current => LazyCurrent.Value;

    internal RulePackageIdentity Identity { get; }
    internal IReadOnlyList<NativeStage01FieldDefinition> Stage01Fields
    {
      get;
    }
    internal IReadOnlyDictionary<string, NativeStage01FieldDefinition>
      Stage01FieldsByKey { get; }
    internal IReadOnlyList<NativeConditionDefinition> Conditions { get; }
    internal IReadOnlyList<NativeModelProfile> ModelProfiles { get; }
    internal IReadOnlyList<NativeSpatialMapping> SpatialMappings { get; }
    internal string DefaultActiveGroup { get; }

    internal NativeStage01Model CreateDefaultStage01Model()
    {
      var model = new NativeStage01Model
      {
        ActiveGroup = DefaultActiveGroup
      };
      foreach (NativeStage01FieldDefinition field in Stage01Fields)
      {
        switch (field.DefaultStrategy)
        {
          case "NONE":
            break;
          case "STATIC":
            SetDefault(model, field, field.DefaultValue);
            break;
          case "NEW_GUID":
            if (!string.Equals(
              field.FieldKey,
              NativeStage01Keys.FileGuid,
              StringComparison.Ordinal))
            {
              throw new InvalidDataException(
                "HBR NEW_GUID 仅允许用于 FileGuid：" + field.FieldKey);
            }
            SetDefault(model, field, Guid.NewGuid().ToString("D"));
            break;
          default:
            throw new InvalidDataException(
              "未知 HBR Stage01 默认策略：" + field.DefaultStrategy);
        }
      }
      foreach (NativeConditionDefinition condition in Conditions)
        model.SetCondition(condition.ConditionId, false);
      model.SetCondition(
        NativeProjectConditionDeclarationPolicy.NoneConditionId,
        false);

      // Payload schema is a persistence protocol version, not the product UI version.
      model.SetValue(
        NativeStage01Keys.WorkflowVersion,
        NativeStage01Canonicalizer.PayloadSchemaVersion);
      return model;
    }

    private static void SetDefault(
      NativeStage01Model model,
      NativeStage01FieldDefinition field,
      string value)
    {
      if (field.IsOrganization)
        model.SetOrganizationValue(0, field.FieldKey, value ?? string.Empty);
      else
        model.SetValue(field.FieldKey, value ?? string.Empty);
    }

    private static NativeRuleCatalog LoadCurrent()
    {
      RulePackageEnvelope envelope =
        RulePackageIdentityReader.ReadEmbeddedEnvelope();
      var serializer = new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 512
      };
      RulePackageDto dto;
      try
      {
        dto = serializer.Deserialize<RulePackageDto>(envelope.PayloadJson);
      }
      catch (Exception exception) when (
        exception is ArgumentException
        || exception is InvalidOperationException)
      {
        throw new InvalidDataException(
          "HBR 规则包无法投影为原生 Revit 规则目录。",
          exception);
      }
      if (dto == null || dto.stage01 == null)
        throw new InvalidDataException("HBR 规则包缺少 stage01。");
      if (!string.Equals(
          dto.packageId,
          envelope.Identity.PackageId,
          StringComparison.Ordinal)
        || !string.Equals(
          dto.packageVersion,
          envelope.Identity.PackageVersion,
          StringComparison.Ordinal))
      {
        throw new InvalidDataException("HBR package identity 投影不一致。");
      }

      var propertiesById = new Dictionary<string, RulePropertyDto>(
        StringComparer.Ordinal);
      foreach (RulePropertyDto property in dto.properties
        ?? Array.Empty<RulePropertyDto>())
      {
        if (property == null || string.IsNullOrWhiteSpace(property.propertyId)
          || propertiesById.ContainsKey(property.propertyId))
        {
          throw new InvalidDataException("HBR propertyId 无效或重复。");
        }
        propertiesById.Add(property.propertyId, property);
      }

      var fields = new List<NativeStage01FieldDefinition>();
      foreach (Stage01FieldRefDto source in dto.stage01.fieldRefs
        ?? Array.Empty<Stage01FieldRefDto>())
      {
        if (source == null
          || !propertiesById.TryGetValue(
            source.propertyId ?? string.Empty,
            out RulePropertyDto property)
          || property.ifc == null
          || property.revit == null)
        {
          throw new InvalidDataException(
            "HBR Stage01 fieldRef 引用了未知或不完整的 propertyId："
            + (source == null ? string.Empty : source.propertyId));
        }
        if (!Guid.TryParse(
          property.revit.parameterGuid,
          out Guid parameterGuid))
        {
          throw new InvalidDataException(
            "HBR Revit parameterGuid 无效：" + property.propertyId);
        }
        fields.Add(new NativeStage01FieldDefinition
        {
          FieldKey = Required(source.fieldKey, "stage01.fieldRefs.fieldKey"),
          PropertyId = property.propertyId,
          Label = Required(property.ifc.property, "properties.ifc.property"),
          UiGroup = Required(source.uiGroup, "stage01.fieldRefs.uiGroup"),
          Kind = MapIfcKind(property.ifc.declaredType),
          SourceKind = source.sourceKind ?? string.Empty,
          WriteInStage01 = source.writeInStage01,
          Essential = source.essential,
          ReadOnly = !source.writeInStage01
            || string.Equals(
              source.sourceKind,
              "later_model_calculation_or_external_value",
              StringComparison.Ordinal),
          Deferred = !source.writeInStage01,
          DefaultStrategy = Required(
            source.defaultStrategy,
            "stage01.fieldRefs.defaultStrategy"),
          DefaultValue = source.defaultValue ?? string.Empty,
          AllowedValues = Array.Empty<string>(),
          IfcEntity = Required(property.ifc.entity, "properties.ifc.entity"),
          IfcPropertySet = Required(
            property.ifc.propertySet,
            "properties.ifc.propertySet"),
          IfcProperty = property.ifc.property,
          DeclaredIfcType = property.ifc.declaredType ?? string.Empty,
          CanonicalUnit = property.ifc.canonicalUnit ?? string.Empty,
          ParameterGuid = parameterGuid,
          ParameterName = property.revit.parameterName ?? string.Empty,
          StorageType = property.revit.storageType ?? string.Empty,
          ParameterType = property.revit.parameterType ?? string.Empty
        });
      }

      foreach (Stage01InternalFieldDto source in
        dto.stage01.internalWorkflowFields
        ?? Array.Empty<Stage01InternalFieldDto>())
      {
        if (source == null)
          throw new InvalidDataException("HBR internalWorkflowField 为空。");
        string sourceKind = source.sourceKind ?? string.Empty;
        fields.Add(new NativeStage01FieldDefinition
        {
          FieldKey = Required(
            source.fieldKey,
            "stage01.internalWorkflowFields.fieldKey"),
          Label = Required(
            source.label,
            "stage01.internalWorkflowFields.label"),
          UiGroup = Required(
            source.uiGroup,
            "stage01.internalWorkflowFields.uiGroup"),
          Kind = MapInternalKind(source.type),
          SourceKind = sourceKind,
          WriteInStage01 = !string.Equals(
            sourceKind,
            "Revit_scan",
            StringComparison.Ordinal),
          Essential = source.essential,
          ReadOnly = string.Equals(
              sourceKind,
              "system_generated",
              StringComparison.Ordinal)
            || string.Equals(
              sourceKind,
              "system_rule",
              StringComparison.Ordinal)
            || string.Equals(
              sourceKind,
              "Revit_scan",
              StringComparison.Ordinal),
          Deferred = false,
          DefaultStrategy = Required(
            source.defaultStrategy,
            "stage01.internalWorkflowFields.defaultStrategy"),
          DefaultValue = source.defaultValue ?? string.Empty,
          AllowedValues = FreezeStrings(source.allowedValues),
          IfcEntity = "Workflow",
          IfcPropertySet = "HBR",
          IfcProperty = source.label
        });
      }

      NativeConditionDefinition[] conditions = (dto.conditions
        ?? Array.Empty<ConditionDto>())
        .Select(value => new NativeConditionDefinition
        {
          ConditionId = Required(value.conditionId, "conditions.conditionId"),
          DisplayName = value.displayName ?? string.Empty,
          Group = value.group ?? string.Empty,
          DefaultActive = value.defaultActive
        })
        .ToArray();
      NativeModelProfile[] profiles = (dto.modelProfiles
        ?? Array.Empty<ModelProfileDto>())
        .Select(value => new NativeModelProfile
        {
          ProfileId = Required(value.profileId, "modelProfiles.profileId")
        })
        .ToArray();
      NativeSpatialMapping[] spatialMappings = (dto.stage01.spatialMappings
        ?? Array.Empty<SpatialMappingDto>())
        .Select(value => new NativeSpatialMapping
        {
          SourceName = Required(value.sourceName, "spatialMappings.sourceName"),
          FieldKey = Required(value.fieldKey, "spatialMappings.fieldKey"),
          TargetName = Required(value.targetName, "spatialMappings.targetName"),
          Unit = value.unit ?? string.Empty
        })
        .ToArray();

      return new NativeRuleCatalog(
        envelope.Identity,
        fields,
        conditions,
        profiles,
        spatialMappings,
        Required(
          dto.stage01.defaultActiveGroup,
          "stage01.defaultActiveGroup"));
    }

    private static NativeStage01FieldKind MapIfcKind(string value)
    {
      string normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
      if (normalized.Contains("BOOLEAN"))
        return NativeStage01FieldKind.Boolean;
      if (normalized.Contains("INTEGER"))
        return NativeStage01FieldKind.Integer;
      if (normalized.Contains("REAL") || normalized.Contains("MEASURE"))
        return NativeStage01FieldKind.Number;
      if (normalized.Contains("DATE"))
        return NativeStage01FieldKind.DateTime;
      return NativeStage01FieldKind.Text;
    }

    private static NativeStage01FieldKind MapInternalKind(string value)
    {
      switch ((value ?? string.Empty).Trim().ToLowerInvariant())
      {
        case "number": return NativeStage01FieldKind.Number;
        case "integer": return NativeStage01FieldKind.Integer;
        case "boolean": return NativeStage01FieldKind.Boolean;
        case "enum": return NativeStage01FieldKind.Enum;
        case "guid": return NativeStage01FieldKind.Guid;
        case "datetime": return NativeStage01FieldKind.DateTime;
        default: return NativeStage01FieldKind.Text;
      }
    }

    private static string Required(string value, string path)
    {
      if (string.IsNullOrWhiteSpace(value))
        throw new InvalidDataException("HBR 字段为空：" + path);
      return value;
    }

    private static IReadOnlyList<string> FreezeStrings(IEnumerable<string> values)
    {
      return new ReadOnlyCollection<string>((values
        ?? Array.Empty<string>())
        .Select(value => value ?? string.Empty)
        .ToArray());
    }

    private sealed class RulePackageDto
    {
      public string packageId { get; set; }
      public string packageVersion { get; set; }
      public RulePropertyDto[] properties { get; set; }
      public ModelProfileDto[] modelProfiles { get; set; }
      public ConditionDto[] conditions { get; set; }
      public Stage01Dto stage01 { get; set; }
    }

    private sealed class RulePropertyDto
    {
      public string propertyId { get; set; }
      public IfcDto ifc { get; set; }
      public RevitDto revit { get; set; }
    }

    private sealed class IfcDto
    {
      public string entity { get; set; }
      public string propertySet { get; set; }
      public string property { get; set; }
      public string declaredType { get; set; }
      public string canonicalUnit { get; set; }
    }

    private sealed class RevitDto
    {
      public string parameterGuid { get; set; }
      public string parameterName { get; set; }
      public string storageType { get; set; }
      public string parameterType { get; set; }
    }

    private sealed class ModelProfileDto
    {
      public string profileId { get; set; }
    }

    private sealed class ConditionDto
    {
      public string conditionId { get; set; }
      public string displayName { get; set; }
      public string group { get; set; }
      public bool defaultActive { get; set; }
    }

    private sealed class Stage01Dto
    {
      public Stage01FieldRefDto[] fieldRefs { get; set; }
      public Stage01InternalFieldDto[] internalWorkflowFields { get; set; }
      public SpatialMappingDto[] spatialMappings { get; set; }
      public string defaultActiveGroup { get; set; }
    }

    private sealed class Stage01FieldRefDto
    {
      public string fieldKey { get; set; }
      public string propertyId { get; set; }
      public string uiGroup { get; set; }
      public string sourceKind { get; set; }
      public bool writeInStage01 { get; set; }
      public bool essential { get; set; }
      public string defaultStrategy { get; set; }
      public string defaultValue { get; set; }
    }

    private sealed class Stage01InternalFieldDto
    {
      public string fieldKey { get; set; }
      public string label { get; set; }
      public string type { get; set; }
      public string uiGroup { get; set; }
      public string sourceKind { get; set; }
      public string[] allowedValues { get; set; }
      public bool essential { get; set; }
      public string defaultStrategy { get; set; }
      public string defaultValue { get; set; }
    }

    private sealed class SpatialMappingDto
    {
      public string sourceName { get; set; }
      public string fieldKey { get; set; }
      public string targetName { get; set; }
      public string unit { get; set; }
    }
  }
}
