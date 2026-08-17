using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace BIMBaoGui.RevitAddin.Rules
{
  internal static class NativeRuntimeStatuses
  {
    internal const string Supported = "SUPPORTED";
    internal const string NotImplemented = "NOT_IMPLEMENTED";
    internal const string UnclassifiedRequirement =
      "UNCLASSIFIED_REQUIREMENT";
    internal const string OfficialEvidenceOnly = "OFFICIAL_EVIDENCE_ONLY";
  }

  internal sealed class NativeRuntimeStatusDecision
  {
    internal string Status { get; set; } = string.Empty;
    internal string BlockCode { get; set; } = string.Empty;
    internal string BlockReason { get; set; } = string.Empty;
  }

  internal sealed class NativeCarrierRoleDefinition
  {
    internal string RoleId { get; set; } = string.Empty;
    internal string DisplayName { get; set; } = string.Empty;
    internal IReadOnlyList<string> ModelFileTypes { get; set; } =
      Array.Empty<string>();
    internal string IfcEntity { get; set; } = string.Empty;
    internal IReadOnlyList<string> RevitCategories { get; set; } =
      Array.Empty<string>();
    internal IReadOnlyList<string> AllowedElementKinds { get; set; } =
      Array.Empty<string>();
    internal IReadOnlyList<string> NameAliases { get; set; } =
      Array.Empty<string>();
    internal IReadOnlyList<string> FamilyAliases { get; set; } =
      Array.Empty<string>();
    internal IReadOnlyList<string> TypeAliases { get; set; } =
      Array.Empty<string>();
    internal int CardinalityMin { get; set; }
    internal int? CardinalityMax { get; set; }
    internal string SelectionPolicy { get; set; } = string.Empty;
    internal string IfcOwnerStrategy { get; set; } = string.Empty;
  }

  internal sealed class NativeStage02PropertyDefinition
  {
    internal string PropertyId { get; set; } = string.Empty;
    internal string ContractKind { get; set; } = string.Empty;
    internal string IfcEntity { get; set; } = string.Empty;
    internal string IfcPropertySet { get; set; } = string.Empty;
    internal string IfcProperty { get; set; } = string.Empty;
    internal string DeclaredIfcType { get; set; } = string.Empty;
    internal string CanonicalUnit { get; set; } = string.Empty;
    internal Guid ParameterGuid { get; set; }
    internal string ParameterName { get; set; } = string.Empty;
    internal IReadOnlyList<string> LegacyParameterNames { get; set; } =
      Array.Empty<string>();
    internal string BindingScope { get; set; } = string.Empty;
    internal string StorageType { get; set; } = string.Empty;
    internal string ParameterType { get; set; } = string.Empty;
    internal bool Visible { get; set; }
    internal bool UserModifiable { get; set; }
    internal IReadOnlyList<string> CarrierRoleIds { get; set; } =
      Array.Empty<string>();
    internal string RequirementLevel { get; set; } = string.Empty;
    internal string ConditionId { get; set; } = string.Empty;
    internal IReadOnlyList<string> StageOwnership { get; set; } =
      Array.Empty<string>();
    internal string SuggestionKind { get; set; } = string.Empty;
    internal IReadOnlyList<string> SuggestionAliases { get; set; } =
      Array.Empty<string>();
    internal string WriteStrategy { get; set; } = string.Empty;
    internal string OwnerStrategy { get; set; } = string.Empty;
    internal string OfficialPropertyEvidenceStatus { get; set; } = string.Empty;
    internal bool OfficialExportVerified { get; set; }
    internal string OfficialCarrierCandidate { get; set; } = string.Empty;
    internal NativeRuntimeStatusDecision RuntimeDecision { get; set; }
  }

  internal sealed class NativeStage02RuleCatalog
  {
    private static readonly Lazy<NativeStage02RuleCatalog> LazyCurrent =
      new Lazy<NativeStage02RuleCatalog>(LoadCurrent, true);

    private NativeStage02RuleCatalog(
      RulePackageIdentity identity,
      IEnumerable<NativeCarrierRoleDefinition> roles,
      IEnumerable<NativeStage02PropertyDefinition> properties)
    {
      Identity = identity ?? throw new ArgumentNullException(nameof(identity));
      NativeCarrierRoleDefinition[] roleArray = (roles
        ?? Array.Empty<NativeCarrierRoleDefinition>())
        .OrderBy(value => value.RoleId, StringComparer.Ordinal)
        .ToArray();
      NativeStage02PropertyDefinition[] propertyArray = (properties
        ?? Array.Empty<NativeStage02PropertyDefinition>())
        .OrderBy(value => value.PropertyId, StringComparer.Ordinal)
        .ToArray();

      var rolesById = new Dictionary<string, NativeCarrierRoleDefinition>(
        StringComparer.Ordinal);
      foreach (NativeCarrierRoleDefinition role in roleArray)
      {
        if (role == null || string.IsNullOrWhiteSpace(role.RoleId)
          || rolesById.ContainsKey(role.RoleId))
          throw new InvalidDataException("HBR carrier role 无效或重复。" );
        rolesById.Add(role.RoleId, role);
      }

      var propertiesById =
        new Dictionary<string, NativeStage02PropertyDefinition>(
          StringComparer.Ordinal);
      var propertiesByGuid =
        new Dictionary<Guid, NativeStage02PropertyDefinition>();
      foreach (NativeStage02PropertyDefinition property in propertyArray)
      {
        if (property == null || string.IsNullOrWhiteSpace(property.PropertyId)
          || property.ParameterGuid == Guid.Empty
          || propertiesById.ContainsKey(property.PropertyId)
          || propertiesByGuid.ContainsKey(property.ParameterGuid))
          throw new InvalidDataException("HBR Stage02 property 无效或重复。" );
        foreach (string roleId in property.CarrierRoleIds)
        {
          if (!rolesById.ContainsKey(roleId))
            throw new InvalidDataException(
              "HBR property 引用了未知 carrier role：" + roleId);
        }
        propertiesById.Add(property.PropertyId, property);
        propertiesByGuid.Add(property.ParameterGuid, property);
      }

      CarrierRoles = new ReadOnlyCollection<NativeCarrierRoleDefinition>(
        roleArray);
      CarrierRolesById =
        new ReadOnlyDictionary<string, NativeCarrierRoleDefinition>(rolesById);
      Properties = new ReadOnlyCollection<NativeStage02PropertyDefinition>(
        propertyArray);
      PropertiesById =
        new ReadOnlyDictionary<string, NativeStage02PropertyDefinition>(
          propertiesById);
      PropertiesByParameterGuid =
        new ReadOnlyDictionary<Guid, NativeStage02PropertyDefinition>(
          propertiesByGuid);
      AllRevitCategories = new ReadOnlyCollection<string>(roleArray
        .SelectMany(value => value.RevitCategories)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray());
    }

    internal static NativeStage02RuleCatalog Current => LazyCurrent.Value;

    internal RulePackageIdentity Identity { get; }
    internal IReadOnlyList<NativeCarrierRoleDefinition> CarrierRoles { get; }
    internal IReadOnlyDictionary<string, NativeCarrierRoleDefinition>
      CarrierRolesById { get; }
    internal IReadOnlyList<NativeStage02PropertyDefinition> Properties { get; }
    internal IReadOnlyDictionary<string, NativeStage02PropertyDefinition>
      PropertiesById { get; }
    internal IReadOnlyDictionary<Guid, NativeStage02PropertyDefinition>
      PropertiesByParameterGuid { get; }
    internal IReadOnlyList<string> AllRevitCategories { get; }

    internal IReadOnlyList<NativeStage02PropertyDefinition> PropertiesForRole(
      string roleId)
    {
      return Properties.Where(value => value.CarrierRoleIds.Any(role =>
        string.Equals(role, roleId, StringComparison.Ordinal))).ToArray();
    }

    private static NativeStage02RuleCatalog LoadCurrent()
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
          "HBR 规则包无法投影为原生 Stage02 目录。",
          exception);
      }
      if (dto == null || dto.runtimeSupport == null)
        throw new InvalidDataException("HBR 规则包缺少 runtimeSupport。" );
      if (!string.Equals(
          dto.packageId,
          envelope.Identity.PackageId,
          StringComparison.Ordinal)
        || !string.Equals(
          dto.packageVersion,
          envelope.Identity.PackageVersion,
          StringComparison.Ordinal))
        throw new InvalidDataException("HBR package identity 投影不一致。" );

      IReadOnlyDictionary<string, string> ownerStatuses = BuildStatusMap(
        dto.runtimeSupport.ownerStrategies,
        value => value.ownerStrategy,
        "runtimeSupport.ownerStrategies");
      IReadOnlyDictionary<string, string> requirementStatuses = BuildStatusMap(
        dto.runtimeSupport.requirementLevels,
        value => value.level,
        "runtimeSupport.requirementLevels");
      string[] precedence = dto.runtimeSupport.statusPrecedence
        ?? Array.Empty<string>();
      string[] expectedPrecedence =
      {
        NativeRuntimeStatuses.NotImplemented,
        NativeRuntimeStatuses.UnclassifiedRequirement,
        NativeRuntimeStatuses.OfficialEvidenceOnly,
        NativeRuntimeStatuses.Supported
      };
      if (!precedence.SequenceEqual(expectedPrecedence, StringComparer.Ordinal))
        throw new InvalidDataException("HBR runtime status precedence 无效。" );

      NativeCarrierRoleDefinition[] roles = (dto.carrierRoles
        ?? Array.Empty<CarrierRoleDto>())
        .Select(value => new NativeCarrierRoleDefinition
        {
          RoleId = Required(value.roleId, "carrierRoles.roleId"),
          DisplayName = value.displayName ?? string.Empty,
          ModelFileTypes = Freeze(value.modelFileTypes),
          IfcEntity = Required(value.ifcEntity, "carrierRoles.ifcEntity"),
          RevitCategories = Freeze(value.revitCategories),
          AllowedElementKinds = Freeze(value.allowedElementKinds),
          NameAliases = Freeze(value.nameAliases),
          FamilyAliases = Freeze(value.familyAliases),
          TypeAliases = Freeze(value.typeAliases),
          CardinalityMin = value.cardinality == null
            ? 0
            : value.cardinality.min,
          CardinalityMax = value.cardinality == null
            ? null
            : value.cardinality.max,
          SelectionPolicy = value.selectionPolicy ?? string.Empty,
          IfcOwnerStrategy = value.ifcOwnerStrategy ?? string.Empty
        })
        .ToArray();

      NativeStage02PropertyDefinition[] properties = (dto.properties
        ?? Array.Empty<PropertyDto>())
        .Where(value => value != null
          && (value.stageOwnership ?? Array.Empty<string>()).Any(stage =>
            string.Equals(stage, "STAGE02", StringComparison.Ordinal)))
        .Select(value => MapProperty(
          value,
          ownerStatuses,
          requirementStatuses,
          precedence))
        .ToArray();
      return new NativeStage02RuleCatalog(
        envelope.Identity,
        roles,
        properties);
    }

    private static NativeStage02PropertyDefinition MapProperty(
      PropertyDto value,
      IReadOnlyDictionary<string, string> ownerStatuses,
      IReadOnlyDictionary<string, string> requirementStatuses,
      IReadOnlyList<string> precedence)
    {
      if (value.ifc == null || value.revit == null
        || value.requirement == null || value.ifcWrite == null)
        throw new InvalidDataException("HBR Stage02 property 结构不完整。" );
      if (!Guid.TryParse(value.revit.parameterGuid, out Guid parameterGuid))
        throw new InvalidDataException(
          "HBR parameterGuid 无效：" + value.propertyId);

      NativeRuntimeStatusDecision runtime = DecideRuntime(
        value.ifcWrite.ownerStrategy,
        value.requirement.level,
        ownerStatuses,
        requirementStatuses,
        precedence);
      return new NativeStage02PropertyDefinition
      {
        PropertyId = Required(value.propertyId, "properties.propertyId"),
        ContractKind = value.contractKind ?? string.Empty,
        IfcEntity = Required(value.ifc.entity, "properties.ifc.entity"),
        IfcPropertySet = Required(
          value.ifc.propertySet,
          "properties.ifc.propertySet"),
        IfcProperty = Required(
          value.ifc.property,
          "properties.ifc.property"),
        DeclaredIfcType = Required(
          value.ifc.declaredType,
          "properties.ifc.declaredType"),
        CanonicalUnit = value.ifc.canonicalUnit ?? string.Empty,
        ParameterGuid = parameterGuid,
        ParameterName = Required(
          value.revit.parameterName,
          "properties.revit.parameterName"),
        LegacyParameterNames = Freeze(value.revit.legacyNames),
        BindingScope = Required(
          value.revit.bindingScope,
          "properties.revit.bindingScope"),
        StorageType = Required(
          value.revit.storageType,
          "properties.revit.storageType"),
        ParameterType = Required(
          value.revit.parameterType,
          "properties.revit.parameterType"),
        Visible = value.revit.visible,
        UserModifiable = value.revit.userModifiable,
        CarrierRoleIds = Freeze(value.carrierRoleIds),
        RequirementLevel = Required(
          value.requirement.level,
          "properties.requirement.level"),
        ConditionId = value.requirement.conditionId ?? string.Empty,
        StageOwnership = Freeze(value.stageOwnership),
        SuggestionKind = value.suggestion == null
          ? string.Empty
          : value.suggestion.kind ?? string.Empty,
        SuggestionAliases = value.suggestion == null
          ? Array.Empty<string>()
          : Freeze(value.suggestion.aliases),
        WriteStrategy = value.ifcWrite.writeStrategy ?? string.Empty,
        OwnerStrategy = value.ifcWrite.ownerStrategy ?? string.Empty,
        OfficialPropertyEvidenceStatus = value.officialPlugin == null
          ? "NO_OFFICIAL_PLUGIN_EVIDENCE"
          : Required(
            value.officialPlugin.evidenceStatus,
            "properties.officialPlugin.evidenceStatus"),
        OfficialExportVerified = false,
        OfficialCarrierCandidate = value.officialPlugin == null
          || value.officialPlugin.legacyProjection == null
          ? string.Empty
          : value.officialPlugin.legacyProjection.carrier ?? string.Empty,
        RuntimeDecision = runtime
      };
    }

    private static NativeRuntimeStatusDecision DecideRuntime(
      string ownerStrategy,
      string requirementLevel,
      IReadOnlyDictionary<string, string> ownerStatuses,
      IReadOnlyDictionary<string, string> requirementStatuses,
      IReadOnlyList<string> precedence)
    {
      if (!ownerStatuses.TryGetValue(
          ownerStrategy ?? string.Empty,
          out string ownerStatus))
        throw new InvalidDataException(
          "HBR property 使用未知 owner strategy：" + ownerStrategy);
      if (!requirementStatuses.TryGetValue(
          requirementLevel ?? string.Empty,
          out string requirementStatus))
        throw new InvalidDataException(
          "HBR property 使用未知 requirement level：" + requirementLevel);
      string status = precedence.FirstOrDefault(value =>
        string.Equals(value, ownerStatus, StringComparison.Ordinal)
        || string.Equals(value, requirementStatus, StringComparison.Ordinal));
      switch (status)
      {
        case NativeRuntimeStatuses.Supported:
          return new NativeRuntimeStatusDecision
          {
            Status = status,
            BlockCode = "SUPPORTED",
            BlockReason = "当前原生运行策略具备基础支持。"
          };
        case NativeRuntimeStatuses.NotImplemented:
          return new NativeRuntimeStatusDecision
          {
            Status = status,
            BlockCode = "OWNER_STRATEGY_NOT_IMPLEMENTED",
            BlockReason = "当前 IFC owner strategy 尚未实现："
              + ownerStrategy
          };
        case NativeRuntimeStatuses.UnclassifiedRequirement:
          return new NativeRuntimeStatusDecision
          {
            Status = status,
            BlockCode = "REQUIREMENT_LEVEL_UNCLASSIFIED",
            BlockReason = "字段 requirement.level 为 UNCLASSIFIED。"
          };
        case NativeRuntimeStatuses.OfficialEvidenceOnly:
          return new NativeRuntimeStatusDecision
          {
            Status = status,
            BlockCode = "OFFICIAL_EVIDENCE_ONLY",
            BlockReason = "字段仅用于官方证据对账。"
          };
        default:
          throw new InvalidDataException("HBR property 缺少 runtime status。" );
      }
    }

    private static IReadOnlyDictionary<string, string> BuildStatusMap<T>(
      IEnumerable<T> values,
      Func<T, string> keySelector,
      string path)
      where T : StatusDto
    {
      var result = new Dictionary<string, string>(StringComparer.Ordinal);
      foreach (T value in values ?? Array.Empty<T>())
      {
        string key = keySelector(value) ?? string.Empty;
        string status = value.status ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key)
          || string.IsNullOrWhiteSpace(status)
          || result.ContainsKey(key))
          throw new InvalidDataException("HBR 状态映射无效：" + path);
        result.Add(key, status);
      }
      if (result.Count == 0)
        throw new InvalidDataException("HBR 状态映射为空：" + path);
      return new ReadOnlyDictionary<string, string>(result);
    }

    private static string Required(string value, string path)
    {
      if (string.IsNullOrWhiteSpace(value))
        throw new InvalidDataException("HBR 字段为空：" + path);
      return value;
    }

    private static IReadOnlyList<string> Freeze(IEnumerable<string> values)
    {
      return new ReadOnlyCollection<string>((values ?? Array.Empty<string>())
        .Select(value => value ?? string.Empty)
        .ToArray());
    }

    private sealed class RulePackageDto
    {
      public string packageId { get; set; }
      public string packageVersion { get; set; }
      public RuntimeSupportDto runtimeSupport { get; set; }
      public CarrierRoleDto[] carrierRoles { get; set; }
      public PropertyDto[] properties { get; set; }
    }

    private sealed class RuntimeSupportDto
    {
      public string[] statusPrecedence { get; set; }
      public OwnerStatusDto[] ownerStrategies { get; set; }
      public RequirementStatusDto[] requirementLevels { get; set; }
    }

    private abstract class StatusDto
    {
      public string status { get; set; }
    }

    private sealed class OwnerStatusDto : StatusDto
    {
      public string ownerStrategy { get; set; }
    }

    private sealed class RequirementStatusDto : StatusDto
    {
      public string level { get; set; }
    }

    private sealed class CarrierRoleDto
    {
      public string roleId { get; set; }
      public string displayName { get; set; }
      public string[] modelFileTypes { get; set; }
      public string ifcEntity { get; set; }
      public string[] revitCategories { get; set; }
      public string[] allowedElementKinds { get; set; }
      public string[] nameAliases { get; set; }
      public string[] familyAliases { get; set; }
      public string[] typeAliases { get; set; }
      public CardinalityDto cardinality { get; set; }
      public string selectionPolicy { get; set; }
      public string ifcOwnerStrategy { get; set; }
    }

    private sealed class CardinalityDto
    {
      public int min { get; set; }
      public int? max { get; set; }
    }

    private sealed class PropertyDto
    {
      public string propertyId { get; set; }
      public string contractKind { get; set; }
      public IfcDto ifc { get; set; }
      public RevitDto revit { get; set; }
      public string[] carrierRoleIds { get; set; }
      public RequirementDto requirement { get; set; }
      public string[] stageOwnership { get; set; }
      public SuggestionDto suggestion { get; set; }
      public IfcWriteDto ifcWrite { get; set; }
      public OfficialPluginDto officialPlugin { get; set; }
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
      public string[] legacyNames { get; set; }
      public bool visible { get; set; }
      public bool userModifiable { get; set; }
      public string bindingScope { get; set; }
      public string storageType { get; set; }
      public string parameterType { get; set; }
    }

    private sealed class RequirementDto
    {
      public string level { get; set; }
      public string conditionId { get; set; }
    }

    private sealed class SuggestionDto
    {
      public string kind { get; set; }
      public string[] aliases { get; set; }
    }

    private sealed class IfcWriteDto
    {
      public string writeStrategy { get; set; }
      public string ownerStrategy { get; set; }
    }

    private sealed class OfficialPluginDto
    {
      public string evidenceStatus { get; set; }
      public LegacyProjectionDto legacyProjection { get; set; }
    }

    private sealed class LegacyProjectionDto
    {
      public string carrier { get; set; }
    }
  }
}
