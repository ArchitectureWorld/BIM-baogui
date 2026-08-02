using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace BIMBaoGui.Stage01.Rules
{
  public sealed class HbrRulePackage
  {
    internal HbrRulePackage(
      HbrRulePackageDto dto,
      int formatVersion,
      string rulePackageSha256)
    {
      dto = HbrDomain.Required(dto, "payload");
      FormatVersion = formatVersion;
      RulePackageSha256 = HbrDomain.NonBlank(
        rulePackageSha256,
        "header.sha256");
      SchemaVersion = HbrDomain.NonBlank(dto.schemaVersion, "schemaVersion");
      PackageId = HbrDomain.NonBlank(dto.packageId, "packageId");
      PackageVersion = HbrDomain.NonBlank(
        dto.packageVersion,
        "packageVersion");
      GuidNamespace = HbrDomain.GuidValue(
        dto.guidNamespace,
        "guidNamespace");
      EvidenceSources = HbrDomain.ConvertList(
        dto.evidenceSources,
        "evidenceSources",
        (item, path) => new HbrEvidenceSource(item, path));
      Properties = HbrDomain.ConvertList(
        dto.properties,
        "properties",
        (item, path) => new HbrRuleProperty(item, path));
      CarrierRoles = HbrDomain.ConvertList(
        dto.carrierRoles,
        "carrierRoles",
        (item, path) => new HbrCarrierRole(item, path));
      ModelProfiles = HbrDomain.ConvertList(
        dto.modelProfiles,
        "modelProfiles",
        (item, path) => new HbrModelProfile(item, path));
      Conditions = HbrDomain.ConvertList(
        dto.conditions,
        "conditions",
        (item, path) => new HbrConditionRule(item, path));
      Tasks = HbrDomain.ConvertList(
        dto.tasks,
        "tasks",
        (item, path) => new HbrTaskRule(item, path));
      LegacyAliases = HbrDomain.ConvertList(
        dto.legacyAliases,
        "legacyAliases",
        (item, path) => new HbrLegacyAlias(item, path));
      Stage01 = new HbrStage01Rules(
        HbrDomain.Required(dto.stage01, "stage01"),
        "stage01");
    }

    public int FormatVersion { get; }
    public string RulePackageSha256 { get; }
    public string SchemaVersion { get; }
    public string PackageId { get; }
    public string PackageVersion { get; }
    public Guid GuidNamespace { get; }
    public IReadOnlyList<HbrEvidenceSource> EvidenceSources { get; }
    public IReadOnlyList<HbrRuleProperty> Properties { get; }
    public IReadOnlyList<HbrCarrierRole> CarrierRoles { get; }
    public IReadOnlyList<HbrModelProfile> ModelProfiles { get; }
    public IReadOnlyList<HbrConditionRule> Conditions { get; }
    public IReadOnlyList<HbrTaskRule> Tasks { get; }
    public IReadOnlyList<HbrLegacyAlias> LegacyAliases { get; }
    public HbrStage01Rules Stage01 { get; }
  }

  public sealed class HbrEvidenceSource
  {
    internal HbrEvidenceSource(HbrEvidenceSourceDto dto, string path)
    {
      dto = HbrDomain.Required(dto, path);
      Source = HbrDomain.NonBlank(dto.source, path + ".source");
      Sha256 = dto.sha256;
      Sheet = dto.sheet;
      Range = dto.range;
      Count = dto.count;
    }

    public string Source { get; }
    public string Sha256 { get; }
    public string Sheet { get; }
    public string Range { get; }
    public int? Count { get; }
  }

  public sealed class HbrRuleProperty
  {
    internal HbrRuleProperty(HbrRulePropertyDto dto, string path)
    {
      dto = HbrDomain.Required(dto, path);
      PropertyId = HbrDomain.NonBlank(dto.propertyId, path + ".propertyId");
      CanonicalKey = HbrDomain.NonBlank(
        dto.canonicalKey,
        path + ".canonicalKey");
      ContractKind = HbrDomain.NonBlank(
        dto.contractKind,
        path + ".contractKind");
      ExtensionReason = dto.extensionReason;
      Source = new HbrPropertySource(
        HbrDomain.Required(dto.source, path + ".source"),
        path + ".source");
      Ifc = new HbrIfcProperty(
        HbrDomain.Required(dto.ifc, path + ".ifc"),
        path + ".ifc");
      Revit = new HbrRevitParameter(
        HbrDomain.Required(dto.revit, path + ".revit"),
        path + ".revit");
      OfficialPlugin = new HbrOfficialPluginProperty(
        HbrDomain.Required(dto.officialPlugin, path + ".officialPlugin"),
        path + ".officialPlugin");
      CarrierRoleIds = HbrDomain.FreezeStrings(
        dto.carrierRoleIds,
        path + ".carrierRoleIds");
      Requirement = new HbrRequirement(
        HbrDomain.Required(dto.requirement, path + ".requirement"),
        path + ".requirement");
      StageOwnership = HbrDomain.FreezeStrings(
        dto.stageOwnership,
        path + ".stageOwnership");
      Suggestion = new HbrSuggestion(
        HbrDomain.Required(dto.suggestion, path + ".suggestion"),
        path + ".suggestion");
      IfcWrite = new HbrIfcWrite(
        HbrDomain.Required(dto.ifcWrite, path + ".ifcWrite"),
        path + ".ifcWrite");
    }

    public string PropertyId { get; }
    public string CanonicalKey { get; }
    public string ContractKind { get; }
    public string ExtensionReason { get; }
    public HbrPropertySource Source { get; }
    public HbrIfcProperty Ifc { get; }
    public HbrRevitParameter Revit { get; }
    public HbrOfficialPluginProperty OfficialPlugin { get; }
    public IReadOnlyList<string> CarrierRoleIds { get; }
    public HbrRequirement Requirement { get; }
    public IReadOnlyList<string> StageOwnership { get; }
    public HbrSuggestion Suggestion { get; }
    public HbrIfcWrite IfcWrite { get; }
  }

  public sealed class HbrPropertySource
  {
    internal HbrPropertySource(HbrPropertySourceDto dto, string path)
    {
      Artifact = HbrDomain.String(dto.artifact, path + ".artifact");
      Sheet = HbrDomain.String(dto.sheet, path + ".sheet");
      Row = dto.row;
      RawEntityCn = HbrDomain.String(dto.rawEntityCn, path + ".rawEntityCn");
      RawEntityId = HbrDomain.String(dto.rawEntityId, path + ".rawEntityId");
      RawIfcElementOrType = HbrDomain.String(
        dto.rawIfcElementOrType,
        path + ".rawIfcElementOrType");
      RawPropertySetId = HbrDomain.String(
        dto.rawPropertySetId,
        path + ".rawPropertySetId");
      RawPropertySetName = HbrDomain.String(
        dto.rawPropertySetName,
        path + ".rawPropertySetName");
      RawProperty = HbrDomain.String(
        dto.rawProperty,
        path + ".rawProperty");
      RawValueKind = HbrDomain.String(
        dto.rawValueKind,
        path + ".rawValueKind");
      RawDeclaredType = HbrDomain.String(
        dto.rawDeclaredType,
        path + ".rawDeclaredType");
      RawUnit = HbrDomain.String(dto.rawUnit, path + ".rawUnit");
    }

    public string Artifact { get; }
    public string Sheet { get; }
    public int? Row { get; }
    public string RawEntityCn { get; }
    public string RawEntityId { get; }
    public string RawIfcElementOrType { get; }
    public string RawPropertySetId { get; }
    public string RawPropertySetName { get; }
    public string RawProperty { get; }
    public string RawValueKind { get; }
    public string RawDeclaredType { get; }
    public string RawUnit { get; }
  }

  public sealed class HbrIfcProperty
  {
    internal HbrIfcProperty(HbrIfcPropertyDto dto, string path)
    {
      Entity = HbrDomain.NonBlank(dto.entity, path + ".entity");
      PropertySet = HbrDomain.NonBlank(
        dto.propertySet,
        path + ".propertySet");
      Property = HbrDomain.NonBlank(dto.property, path + ".property");
      SourceUnit = dto.sourceUnit;
      DeclaredType = HbrDomain.NonBlank(
        dto.declaredType,
        path + ".declaredType");
      CanonicalUnit = dto.canonicalUnit;
      AllowedRuntimeTypes = HbrDomain.FreezeStrings(
        dto.allowedRuntimeTypes,
        path + ".allowedRuntimeTypes");
    }

    public string Entity { get; }
    public string PropertySet { get; }
    public string Property { get; }
    public string SourceUnit { get; }
    public string DeclaredType { get; }
    public string CanonicalUnit { get; }
    public IReadOnlyList<string> AllowedRuntimeTypes { get; }
  }

  public sealed class HbrRevitParameter
  {
    internal HbrRevitParameter(HbrRevitParameterDto dto, string path)
    {
      ParameterGuid = HbrDomain.GuidValue(
        dto.parameterGuid,
        path + ".parameterGuid");
      ParameterName = HbrDomain.NonBlank(
        dto.parameterName,
        path + ".parameterName");
      LegacyNames = HbrDomain.FreezeStrings(
        dto.legacyNames,
        path + ".legacyNames");
      Visible = dto.visible;
      UserModifiable = dto.userModifiable;
      BindingScope = HbrDomain.NonBlank(
        dto.bindingScope,
        path + ".bindingScope");
      StorageType = HbrDomain.NonBlank(
        dto.storageType,
        path + ".storageType");
      ParameterType = HbrDomain.NonBlank(
        dto.parameterType,
        path + ".parameterType");
    }

    public Guid ParameterGuid { get; }
    public string ParameterName { get; }
    public IReadOnlyList<string> LegacyNames { get; }
    public bool Visible { get; }
    public bool UserModifiable { get; }
    public string BindingScope { get; }
    public string StorageType { get; }
    public string ParameterType { get; }
  }

  public sealed class HbrOfficialPluginProperty
  {
    internal HbrOfficialPluginProperty(
      HbrOfficialPluginPropertyDto dto,
      string path)
    {
      InExtracted166 = dto.inExtracted166;
      EvidenceStatus = HbrDomain.NonBlank(
        dto.evidenceStatus,
        path + ".evidenceStatus");
      OriginalIdentity = dto.originalIdentity;
      LegacyProjection = dto.legacyProjection == null
        ? null
        : new HbrLegacyProjection(
          dto.legacyProjection,
          path + ".legacyProjection");
    }

    public bool InExtracted166 { get; }
    public string EvidenceStatus { get; }
    public string OriginalIdentity { get; }
    public HbrLegacyProjection LegacyProjection { get; }
  }

  public sealed class HbrLegacyProjection
  {
    internal HbrLegacyProjection(HbrLegacyProjectionDto dto, string path)
    {
      Category = HbrDomain.String(dto.category, path + ".category");
      Carrier = HbrDomain.String(dto.carrier, path + ".carrier");
      PersistenceMode = HbrDomain.String(
        dto.persistenceMode,
        path + ".persistenceMode");
      SharedParameterType = HbrDomain.String(
        dto.sharedParameterType,
        path + ".sharedParameterType");
      OfficialSourceParameterGroup = HbrDomain.String(
        dto.officialSourceParameterGroup,
        path + ".officialSourceParameterGroup");
      SourceParameterOverride = HbrDomain.String(
        dto.sourceParameterOverride,
        path + ".sourceParameterOverride");
    }

    public string Category { get; }
    public string Carrier { get; }
    public string PersistenceMode { get; }
    public string SharedParameterType { get; }
    public string OfficialSourceParameterGroup { get; }
    public string SourceParameterOverride { get; }
  }

  public sealed class HbrRequirement
  {
    internal HbrRequirement(HbrRequirementDto dto, string path)
    {
      Level = HbrDomain.NonBlank(dto.level, path + ".level");
      ConditionId = dto.conditionId;
    }

    public string Level { get; }
    public string ConditionId { get; }
  }

  public sealed class HbrSuggestion
  {
    internal HbrSuggestion(HbrSuggestionDto dto, string path)
    {
      Kind = HbrDomain.NonBlank(dto.kind, path + ".kind");
      Aliases = HbrDomain.FreezeStrings(dto.aliases, path + ".aliases");
    }

    public string Kind { get; }
    public IReadOnlyList<string> Aliases { get; }
  }

  public sealed class HbrIfcWrite
  {
    internal HbrIfcWrite(HbrIfcWriteDto dto, string path)
    {
      WriteStrategy = HbrDomain.NonBlank(
        dto.writeStrategy,
        path + ".writeStrategy");
      OwnerStrategy = HbrDomain.NonBlank(
        dto.ownerStrategy,
        path + ".ownerStrategy");
    }

    public string WriteStrategy { get; }
    public string OwnerStrategy { get; }
  }

  public sealed class HbrCarrierRole
  {
    internal HbrCarrierRole(HbrCarrierRoleDto dto, string path)
    {
      dto = HbrDomain.Required(dto, path);
      RoleId = HbrDomain.NonBlank(dto.roleId, path + ".roleId");
      DisplayName = HbrDomain.NonBlank(
        dto.displayName,
        path + ".displayName");
      ModelFileTypes = HbrDomain.FreezeStrings(
        dto.modelFileTypes,
        path + ".modelFileTypes");
      IfcEntity = HbrDomain.NonBlank(dto.ifcEntity, path + ".ifcEntity");
      RevitCategories = HbrDomain.FreezeStrings(
        dto.revitCategories,
        path + ".revitCategories");
      AllowedElementKinds = HbrDomain.FreezeStrings(
        dto.allowedElementKinds,
        path + ".allowedElementKinds");
      NameAliases = HbrDomain.FreezeStrings(
        dto.nameAliases,
        path + ".nameAliases");
      FamilyAliases = HbrDomain.FreezeStrings(
        dto.familyAliases,
        path + ".familyAliases");
      TypeAliases = HbrDomain.FreezeStrings(
        dto.typeAliases,
        path + ".typeAliases");
      Cardinality = new HbrCardinality(
        HbrDomain.Required(dto.cardinality, path + ".cardinality"));
      SelectionPolicy = HbrDomain.NonBlank(
        dto.selectionPolicy,
        path + ".selectionPolicy");
      IfcOwnerStrategy = HbrDomain.NonBlank(
        dto.ifcOwnerStrategy,
        path + ".ifcOwnerStrategy");
    }

    public string RoleId { get; }
    public string DisplayName { get; }
    public IReadOnlyList<string> ModelFileTypes { get; }
    public string IfcEntity { get; }
    public IReadOnlyList<string> RevitCategories { get; }
    public IReadOnlyList<string> AllowedElementKinds { get; }
    public IReadOnlyList<string> NameAliases { get; }
    public IReadOnlyList<string> FamilyAliases { get; }
    public IReadOnlyList<string> TypeAliases { get; }
    public HbrCardinality Cardinality { get; }
    public string SelectionPolicy { get; }
    public string IfcOwnerStrategy { get; }
  }

  public sealed class HbrCardinality
  {
    internal HbrCardinality(HbrCardinalityDto dto)
    {
      Min = dto.min;
      Max = dto.max;
    }

    public int Min { get; }
    public int? Max { get; }
  }

  public sealed class HbrModelProfile
  {
    internal HbrModelProfile(HbrModelProfileDto dto, string path)
    {
      dto = HbrDomain.Required(dto, path);
      ProfileId = HbrDomain.NonBlank(dto.profileId, path + ".profileId");
      TaskIds = HbrDomain.FreezeStrings(dto.taskIds, path + ".taskIds");
      ActivationRuleIds = HbrDomain.FreezeStrings(
        dto.activationRuleIds,
        path + ".activationRuleIds");
    }

    public string ProfileId { get; }
    public IReadOnlyList<string> TaskIds { get; }
    public IReadOnlyList<string> ActivationRuleIds { get; }
  }

  public sealed class HbrConditionRule
  {
    internal HbrConditionRule(HbrConditionRuleDto dto, string path)
    {
      dto = HbrDomain.Required(dto, path);
      ConditionId = HbrDomain.NonBlank(
        dto.conditionId,
        path + ".conditionId");
      DisplayName = HbrDomain.NonBlank(
        dto.displayName,
        path + ".displayName");
      Group = HbrDomain.String(dto.group, path + ".group");
      ActivationRuleId = dto.activationRuleId;
      EvidenceStatus = HbrDomain.String(
        dto.evidenceStatus,
        path + ".evidenceStatus");
      Source = HbrDomain.String(dto.source, path + ".source");
    }

    public string ConditionId { get; }
    public string DisplayName { get; }
    public string Group { get; }
    public string ActivationRuleId { get; }
    public string EvidenceStatus { get; }
    public string Source { get; }
  }

  public sealed class HbrTaskRule
  {
    internal HbrTaskRule(HbrTaskRuleDto dto, string path)
    {
      dto = HbrDomain.Required(dto, path);
      TaskId = HbrDomain.NonBlank(dto.taskId, path + ".taskId");
      ModelFileType = HbrDomain.NonBlank(
        dto.modelFileType,
        path + ".modelFileType");
      Name = HbrDomain.NonBlank(dto.name, path + ".name");
      ObjectCode = HbrDomain.NonBlank(dto.objectCode, path + ".objectCode");
      Requirement = HbrDomain.NonBlank(
        dto.requirement,
        path + ".requirement");
      ConditionId = dto.conditionId;
      Sequence = dto.sequence;
      SkeletonTask = dto.skeletonTask;
      AttributeRequirements = HbrDomain.FreezeStrings(
        dto.attributeRequirements,
        path + ".attributeRequirements");
      Dependencies = HbrDomain.FreezeStrings(
        dto.dependencies,
        path + ".dependencies");
      GeometryChecks = HbrDomain.FreezeStrings(
        dto.geometryChecks,
        path + ".geometryChecks");
      PropertyChecks = HbrDomain.FreezeStrings(
        dto.propertyChecks,
        path + ".propertyChecks");
      TargetComparisons = HbrDomain.FreezeStrings(
        dto.targetComparisons,
        path + ".targetComparisons");
      Source = HbrDomain.String(dto.source, path + ".source");
    }

    public string TaskId { get; }
    public string ModelFileType { get; }
    public string Name { get; }
    public string ObjectCode { get; }
    public string Requirement { get; }
    public string ConditionId { get; }
    public int Sequence { get; }
    public bool SkeletonTask { get; }
    public IReadOnlyList<string> AttributeRequirements { get; }
    public IReadOnlyList<string> Dependencies { get; }
    public IReadOnlyList<string> GeometryChecks { get; }
    public IReadOnlyList<string> PropertyChecks { get; }
    public IReadOnlyList<string> TargetComparisons { get; }
    public string Source { get; }
  }

  public sealed class HbrLegacyAlias
  {
    internal HbrLegacyAlias(HbrLegacyAliasDto dto, string path)
    {
      dto = HbrDomain.Required(dto, path);
      PropertyId = HbrDomain.NonBlank(dto.propertyId, path + ".propertyId");
      Alias = HbrDomain.NonBlank(dto.alias, path + ".alias");
    }

    public string PropertyId { get; }
    public string Alias { get; }
  }

  public sealed class HbrStage01Rules
  {
    internal HbrStage01Rules(HbrStage01RulesDto dto, string path)
    {
      FieldRefs = HbrDomain.ConvertList(
        dto.fieldRefs,
        path + ".fieldRefs",
        (item, itemPath) => new HbrStage01FieldRef(item, itemPath));
      InternalWorkflowFields = HbrDomain.ConvertList(
        dto.internalWorkflowFields,
        path + ".internalWorkflowFields",
        (item, itemPath) => new HbrInternalWorkflowField(item, itemPath));
      OfficialPluginCompatibility = new HbrOfficialPluginCompatibility(
        HbrDomain.Required(
          dto.officialPluginCompatibility,
          path + ".officialPluginCompatibility"),
        path + ".officialPluginCompatibility");
    }

    public IReadOnlyList<HbrStage01FieldRef> FieldRefs { get; }
    public IReadOnlyList<HbrInternalWorkflowField> InternalWorkflowFields { get; }
    public HbrOfficialPluginCompatibility OfficialPluginCompatibility { get; }
  }

  public sealed class HbrStage01FieldRef
  {
    internal HbrStage01FieldRef(HbrStage01FieldRefDto dto, string path)
    {
      dto = HbrDomain.Required(dto, path);
      FieldKey = HbrDomain.NonBlank(dto.fieldKey, path + ".fieldKey");
      PropertyId = HbrDomain.NonBlank(dto.propertyId, path + ".propertyId");
      SourceRow = dto.sourceRow;
      UiGroup = HbrDomain.NonBlank(dto.uiGroup, path + ".uiGroup");
      SourceKind = HbrDomain.NonBlank(dto.sourceKind, path + ".sourceKind");
      WriteInStage01 = dto.writeInStage01;
    }

    public string FieldKey { get; }
    public string PropertyId { get; }
    public int SourceRow { get; }
    public string UiGroup { get; }
    public string SourceKind { get; }
    public bool WriteInStage01 { get; }
  }

  public sealed class HbrInternalWorkflowField
  {
    internal HbrInternalWorkflowField(
      HbrInternalWorkflowFieldDto dto,
      string path)
    {
      dto = HbrDomain.Required(dto, path);
      FieldKey = HbrDomain.NonBlank(dto.fieldKey, path + ".fieldKey");
      Label = HbrDomain.NonBlank(dto.label, path + ".label");
      Type = HbrDomain.NonBlank(dto.type, path + ".type");
      UiGroup = HbrDomain.NonBlank(dto.uiGroup, path + ".uiGroup");
      SourceKind = HbrDomain.NonBlank(dto.sourceKind, path + ".sourceKind");
      AllowedValues = HbrDomain.FreezeStrings(
        dto.allowedValues,
        path + ".allowedValues");
      DefaultValue = dto.defaultValue;
    }

    public string FieldKey { get; }
    public string Label { get; }
    public string Type { get; }
    public string UiGroup { get; }
    public string SourceKind { get; }
    public IReadOnlyList<string> AllowedValues { get; }
    public string DefaultValue { get; }
  }

  public sealed class HbrOfficialPluginCompatibility
  {
    internal HbrOfficialPluginCompatibility(
      HbrOfficialPluginCompatibilityDto dto,
      string path)
    {
      EntityPolicies = HbrDomain.ConvertList(
        dto.entityPolicies,
        path + ".entityPolicies",
        (item, itemPath) => new HbrEntityPolicy(item, itemPath));
      Exceptions = HbrDomain.ConvertList(
        dto.exceptions,
        path + ".exceptions",
        (item, itemPath) => new HbrOfficialPluginException(item, itemPath));
    }

    public IReadOnlyList<HbrEntityPolicy> EntityPolicies { get; }
    public IReadOnlyList<HbrOfficialPluginException> Exceptions { get; }
  }

  public sealed class HbrEntityPolicy
  {
    internal HbrEntityPolicy(HbrEntityPolicyDto dto, string path)
    {
      dto = HbrDomain.Required(dto, path);
      IfcEntity = HbrDomain.NonBlank(dto.ifcEntity, path + ".ifcEntity");
      OfficialObjectMappingEvidence = HbrDomain.NonBlank(
        dto.officialObjectMappingEvidence,
        path + ".officialObjectMappingEvidence");
      RevitCarrier = HbrDomain.String(
        dto.revitCarrier,
        path + ".revitCarrier");
      WritePolicy = HbrDomain.NonBlank(
        dto.writePolicy,
        path + ".writePolicy");
      OfficialExportVerified = dto.officialExportVerified;
    }

    public string IfcEntity { get; }
    public string OfficialObjectMappingEvidence { get; }
    public string RevitCarrier { get; }
    public string WritePolicy { get; }
    public bool OfficialExportVerified { get; }
  }

  public sealed class HbrOfficialPluginException
  {
    internal HbrOfficialPluginException(
      HbrOfficialPluginExceptionDto dto,
      string path)
    {
      dto = HbrDomain.Required(dto, path);
      FieldKey = HbrDomain.NonBlank(dto.fieldKey, path + ".fieldKey");
      Reason = HbrDomain.NonBlank(dto.reason, path + ".reason");
    }

    public string FieldKey { get; }
    public string Reason { get; }
  }

  internal static class HbrDomain
  {
    internal static T Required<T>(T value, string path) where T : class
    {
      if (value == null)
        throw new InvalidDataException("HBRP payload missing " + path + ".");
      return value;
    }

    internal static string String(string value, string path)
    {
      if (value == null)
        throw new InvalidDataException("HBRP payload missing " + path + ".");
      return value;
    }

    internal static string NonBlank(string value, string path)
    {
      if (string.IsNullOrWhiteSpace(value))
        throw new InvalidDataException(
          "HBRP payload has empty " + path + ".");
      return value;
    }

    internal static Guid GuidValue(string value, string path)
    {
      Guid parsed;
      if (!Guid.TryParse(value, out parsed) || parsed == Guid.Empty)
        throw new InvalidDataException(
          "HBRP payload has invalid " + path + ": " + value + ".");
      return parsed;
    }

    internal static IReadOnlyList<string> FreezeStrings(
      IList<string> values,
      string path)
    {
      Required(values, path);
      var copy = new List<string>(values.Count);
      for (int index = 0; index < values.Count; index++)
        copy.Add(String(values[index], path + "[" + index + "]"));
      return new ReadOnlyCollection<string>(copy);
    }

    internal static IReadOnlyList<TResult> ConvertList<TSource, TResult>(
      IList<TSource> values,
      string path,
      Func<TSource, string, TResult> converter)
    {
      Required(values, path);
      var copy = new List<TResult>(values.Count);
      for (int index = 0; index < values.Count; index++)
        copy.Add(converter(values[index], path + "[" + index + "]"));
      return new ReadOnlyCollection<TResult>(copy);
    }
  }

  internal sealed class HbrRulePackageDto
  {
    public string schemaVersion { get; set; }
    public string packageId { get; set; }
    public string packageVersion { get; set; }
    public string guidNamespace { get; set; }
    public List<HbrEvidenceSourceDto> evidenceSources { get; set; }
    public List<HbrRulePropertyDto> properties { get; set; }
    public List<HbrCarrierRoleDto> carrierRoles { get; set; }
    public List<HbrModelProfileDto> modelProfiles { get; set; }
    public List<HbrConditionRuleDto> conditions { get; set; }
    public List<HbrTaskRuleDto> tasks { get; set; }
    public List<HbrLegacyAliasDto> legacyAliases { get; set; }
    public HbrStage01RulesDto stage01 { get; set; }
  }

  internal sealed class HbrEvidenceSourceDto
  {
    public string source { get; set; }
    public string sha256 { get; set; }
    public string sheet { get; set; }
    public string range { get; set; }
    public int? count { get; set; }
  }

  internal sealed class HbrRulePropertyDto
  {
    public string propertyId { get; set; }
    public string canonicalKey { get; set; }
    public string contractKind { get; set; }
    public string extensionReason { get; set; }
    public HbrPropertySourceDto source { get; set; }
    public HbrIfcPropertyDto ifc { get; set; }
    public HbrRevitParameterDto revit { get; set; }
    public HbrOfficialPluginPropertyDto officialPlugin { get; set; }
    public List<string> carrierRoleIds { get; set; }
    public HbrRequirementDto requirement { get; set; }
    public List<string> stageOwnership { get; set; }
    public HbrSuggestionDto suggestion { get; set; }
    public HbrIfcWriteDto ifcWrite { get; set; }
  }

  internal sealed class HbrPropertySourceDto
  {
    public string artifact { get; set; }
    public string sheet { get; set; }
    public int? row { get; set; }
    public string rawEntityCn { get; set; }
    public string rawEntityId { get; set; }
    public string rawIfcElementOrType { get; set; }
    public string rawPropertySetId { get; set; }
    public string rawPropertySetName { get; set; }
    public string rawProperty { get; set; }
    public string rawValueKind { get; set; }
    public string rawDeclaredType { get; set; }
    public string rawUnit { get; set; }
  }

  internal sealed class HbrIfcPropertyDto
  {
    public string entity { get; set; }
    public string propertySet { get; set; }
    public string property { get; set; }
    public string sourceUnit { get; set; }
    public string declaredType { get; set; }
    public string canonicalUnit { get; set; }
    public List<string> allowedRuntimeTypes { get; set; }
  }

  internal sealed class HbrRevitParameterDto
  {
    public string parameterGuid { get; set; }
    public string parameterName { get; set; }
    public List<string> legacyNames { get; set; }
    public bool visible { get; set; }
    public bool userModifiable { get; set; }
    public string bindingScope { get; set; }
    public string storageType { get; set; }
    public string parameterType { get; set; }
  }

  internal sealed class HbrOfficialPluginPropertyDto
  {
    public bool inExtracted166 { get; set; }
    public string evidenceStatus { get; set; }
    public string originalIdentity { get; set; }
    public HbrLegacyProjectionDto legacyProjection { get; set; }
  }

  internal sealed class HbrLegacyProjectionDto
  {
    public string category { get; set; }
    public string carrier { get; set; }
    public string persistenceMode { get; set; }
    public string sharedParameterType { get; set; }
    public string officialSourceParameterGroup { get; set; }
    public string sourceParameterOverride { get; set; }
  }

  internal sealed class HbrRequirementDto
  {
    public string level { get; set; }
    public string conditionId { get; set; }
  }

  internal sealed class HbrSuggestionDto
  {
    public string kind { get; set; }
    public List<string> aliases { get; set; }
  }

  internal sealed class HbrIfcWriteDto
  {
    public string writeStrategy { get; set; }
    public string ownerStrategy { get; set; }
  }

  internal sealed class HbrCarrierRoleDto
  {
    public string roleId { get; set; }
    public string displayName { get; set; }
    public List<string> modelFileTypes { get; set; }
    public string ifcEntity { get; set; }
    public List<string> revitCategories { get; set; }
    public List<string> allowedElementKinds { get; set; }
    public List<string> nameAliases { get; set; }
    public List<string> familyAliases { get; set; }
    public List<string> typeAliases { get; set; }
    public HbrCardinalityDto cardinality { get; set; }
    public string selectionPolicy { get; set; }
    public string ifcOwnerStrategy { get; set; }
  }

  internal sealed class HbrCardinalityDto
  {
    public int min { get; set; }
    public int? max { get; set; }
  }

  internal sealed class HbrModelProfileDto
  {
    public string profileId { get; set; }
    public List<string> taskIds { get; set; }
    public List<string> activationRuleIds { get; set; }
  }

  internal sealed class HbrConditionRuleDto
  {
    public string conditionId { get; set; }
    public string displayName { get; set; }
    public string group { get; set; }
    public string activationRuleId { get; set; }
    public string evidenceStatus { get; set; }
    public string source { get; set; }
  }

  internal sealed class HbrTaskRuleDto
  {
    public string taskId { get; set; }
    public string modelFileType { get; set; }
    public string name { get; set; }
    public string objectCode { get; set; }
    public string requirement { get; set; }
    public string conditionId { get; set; }
    public int sequence { get; set; }
    public bool skeletonTask { get; set; }
    public List<string> attributeRequirements { get; set; }
    public List<string> dependencies { get; set; }
    public List<string> geometryChecks { get; set; }
    public List<string> propertyChecks { get; set; }
    public List<string> targetComparisons { get; set; }
    public string source { get; set; }
  }

  internal sealed class HbrLegacyAliasDto
  {
    public string propertyId { get; set; }
    public string alias { get; set; }
  }

  internal sealed class HbrStage01RulesDto
  {
    public List<HbrStage01FieldRefDto> fieldRefs { get; set; }
    public List<HbrInternalWorkflowFieldDto> internalWorkflowFields { get; set; }
    public HbrOfficialPluginCompatibilityDto officialPluginCompatibility { get; set; }
  }

  internal sealed class HbrStage01FieldRefDto
  {
    public string fieldKey { get; set; }
    public string propertyId { get; set; }
    public int sourceRow { get; set; }
    public string uiGroup { get; set; }
    public string sourceKind { get; set; }
    public bool writeInStage01 { get; set; }
  }

  internal sealed class HbrInternalWorkflowFieldDto
  {
    public string fieldKey { get; set; }
    public string label { get; set; }
    public string type { get; set; }
    public string uiGroup { get; set; }
    public string sourceKind { get; set; }
    public List<string> allowedValues { get; set; }
    public string defaultValue { get; set; }
  }

  internal sealed class HbrOfficialPluginCompatibilityDto
  {
    public List<HbrEntityPolicyDto> entityPolicies { get; set; }
    public List<HbrOfficialPluginExceptionDto> exceptions { get; set; }
  }

  internal sealed class HbrEntityPolicyDto
  {
    public string ifcEntity { get; set; }
    public string officialObjectMappingEvidence { get; set; }
    public string revitCarrier { get; set; }
    public string writePolicy { get; set; }
    public bool officialExportVerified { get; set; }
  }

  internal sealed class HbrOfficialPluginExceptionDto
  {
    public string fieldKey { get; set; }
    public string reason { get; set; }
  }
}
