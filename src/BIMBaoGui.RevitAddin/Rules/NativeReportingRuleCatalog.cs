using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace BIMBaoGui.RevitAddin.Rules
{
  internal enum NativeReportingSourceStage
  {
    Unknown,
    Stage01,
    Stage02A,
    Stage02B,
    CrossStage,
    ExportPreparation
  }

  internal enum NativeReportingCheckKind
  {
    Unknown,
    Stage01Field,
    PlanningTarget,
    SemanticRole,
    AttributeRequirement,
    Geometry,
    PropertyConsistency,
    TargetComparison,
    Stage02BMetric,
    System
  }

  internal enum NativeOfficialCarrierEvidenceStatus
  {
    Unknown,
    Verified,
    PendingGoldenRvt,
    InternalOnly
  }

  internal sealed class NativeReportingCheckDefinition
  {
    internal string CheckId { get; set; } = string.Empty;
    internal string ModelFileType { get; set; } = string.Empty;
    internal int Sequence { get; set; }
    internal string DisplayName { get; set; } = string.Empty;
    internal NativeReportingSourceStage SourceStage { get; set; }
    internal NativeReportingCheckKind CheckKind { get; set; }
    internal string ApplicableBasis { get; set; } = string.Empty;
    internal string ConditionId { get; set; } = string.Empty;
    internal string TaskId { get; set; } = string.Empty;
    internal string FieldKey { get; set; } = string.Empty;
    internal string PropertyId { get; set; } = string.Empty;
    internal string InternalDefinitionSource { get; set; } = string.Empty;
    internal NativeOfficialCarrierEvidenceStatus InternalCarrierStatus { get; set; }
    internal string RoleId { get; set; } = string.Empty;
    internal string RuleText { get; set; } = string.Empty;
    internal string TargetKey { get; set; } = string.Empty;
    internal string Unit { get; set; } = string.Empty;
    internal string RemediationTarget { get; set; } = string.Empty;
    internal NativeOfficialCarrierEvidenceStatus OfficialCarrierStatus { get; set; }
    internal string OfficialProjectionCarrierId { get; set; } = string.Empty;
    internal string OfficialCarrierProbeRef { get; set; } = string.Empty;
    internal string OfficialEvidenceRef { get; set; } = string.Empty;
  }

  internal sealed class NativeReportingSemanticRole
  {
    internal string RoleId { get; set; } = string.Empty;
    internal string TaskId { get; set; } = string.Empty;
    internal string DisplayName { get; set; } = string.Empty;
    internal IReadOnlyList<string> CandidateAliases { get; set; } =
      Array.Empty<string>();
    internal NativeOfficialCarrierEvidenceStatus InternalCarrierStatus { get; set; }
    internal NativeOfficialCarrierEvidenceStatus OfficialCarrierStatus { get; set; }
    internal IReadOnlyList<NativeReportingAttributeMapping> AttributeMappings
    {
      get;
      set;
    } = Array.Empty<NativeReportingAttributeMapping>();
  }

  internal sealed class NativeReportingAttributeMapping
  {
    internal string AttributeRequirement { get; set; } = string.Empty;
    internal string InternalPropertyId { get; set; } = string.Empty;
    internal string DefinitionSource { get; set; } = string.Empty;
  }

  internal sealed class NativeInternalPropertyDefinition
  {
    internal string PropertyId { get; set; } = string.Empty;
    internal string CanonicalKey { get; set; } = string.Empty;
    internal string DisplayName { get; set; } = string.Empty;
    internal string ValueKind { get; set; } = string.Empty;
    internal string CanonicalUnit { get; set; } = string.Empty;
    internal Guid ParameterGuid { get; set; }
    internal string ParameterName { get; set; } = string.Empty;
    internal string StorageType { get; set; } = string.Empty;
    internal string ParameterType { get; set; } = string.Empty;
    internal string BindingScope { get; set; } = string.Empty;
    internal NativeOfficialCarrierEvidenceStatus EvidenceStatus { get; set; }
    internal bool OfficialExportVerified { get; set; }
  }

  internal sealed class NativeOfficialAcceptancePropertyDefinition
  {
    internal string PropertyId { get; set; } = string.Empty;
    internal string Identity { get; set; } = string.Empty;
    internal string DeclaredIfcType { get; set; } = string.Empty;
    internal string CanonicalUnit { get; set; } = string.Empty;
    internal Guid ParameterGuid { get; set; }
    internal string BindingScope { get; set; } = string.Empty;
    internal NativeReportingSourceStage SourceStage { get; set; }
  }

  internal sealed class NativeOfficialCarrierPolicy
  {
    internal string IfcEntity { get; set; } = string.Empty;
    internal string InternalCarrier { get; set; } = string.Empty;
    internal string ProjectionPolicy { get; set; } = string.Empty;
    internal bool OfficialExportVerified { get; set; }
    internal NativeOfficialCarrierEvidenceStatus EvidenceStatus { get; set; }
    internal IReadOnlyList<string> ProbeRefs { get; set; } = Array.Empty<string>();
    internal IReadOnlyList<string> EvidenceRefs { get; set; } = Array.Empty<string>();
  }

  internal sealed class NativeOfficialProjectionCarrierDefinition
  {
    internal string CarrierId { get; set; } = string.Empty;
    internal string PropertyId { get; set; } = string.Empty;
    internal string SelectorKind { get; set; } = string.Empty;
    internal string RoleId { get; set; } = string.Empty;
    internal string CategoryBuiltInId { get; set; } = string.Empty;
    internal string ElementClass { get; set; } = string.Empty;
    internal string BindingScope { get; set; } = string.Empty;
    internal string ParameterGuid { get; set; } = string.Empty;
  }

  internal sealed class NativeOfficialEvidenceRecord
  {
    internal string EvidenceId { get; set; } = string.Empty;
    internal string PropertyId { get; set; } = string.Empty;
    internal string GoldenRvtSha256 { get; set; } = string.Empty;
    internal string HifctoolManifestSha256 { get; set; } = string.Empty;
    internal string HifctoolDllSha256 { get; set; } = string.Empty;
    internal string HifctoolProductVersion { get; set; } = string.Empty;
    internal string OfficialIfcSha256 { get; set; } = string.Empty;
    internal string IfcFluxProductVersion { get; set; } = string.Empty;
    internal string IfcFluxReportSha256 { get; set; } = string.Empty;
    internal string ObservedRevitUniqueId { get; set; } = string.Empty;
    internal string ObservedIfcGlobalId { get; set; } = string.Empty;
    internal string ObservedBindingScope { get; set; } = string.Empty;
    internal string ObservedParameterGuid { get; set; } = string.Empty;
  }

  internal sealed class NativeOfficialCarrierProbeRecord
  {
    internal string ProbeId { get; set; } = string.Empty;
    internal string PropertyId { get; set; } = string.Empty;
    internal string SourceGoldenRvtSha256 { get; set; } = string.Empty;
    internal string ProbeSeedManifestSha256 { get; set; } = string.Empty;
    internal string ProbeRvtSha256 { get; set; } = string.Empty;
    internal string ProbeIfcSha256 { get; set; } = string.Empty;
    internal string HifcToolManifestSha256 { get; set; } = string.Empty;
    internal string HifcToolDllSha256 { get; set; } = string.Empty;
    internal string HifcToolProductVersion { get; set; } = string.Empty;
    internal string ObservedRevitUniqueId { get; set; } = string.Empty;
    internal string ObservedIfcGlobalId { get; set; } = string.Empty;
    internal string ObservedBindingScope { get; set; } = string.Empty;
    internal string ObservedParameterGuid { get; set; } = string.Empty;
    internal string ObservedSentinel { get; set; } = string.Empty;
  }

  internal sealed class NativeReportingRuleCatalog
  {
    private static readonly Lazy<NativeReportingRuleCatalog> LazyCurrent =
      new Lazy<NativeReportingRuleCatalog>(LoadCurrent, true);

    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>>
      _taskIdsByProfile;
    private readonly IReadOnlyDictionary<string,
      IReadOnlyList<NativeReportingCheckDefinition>> _checksByProfile;
    private readonly IReadOnlyDictionary<string,
      IReadOnlyList<NativeReportingSemanticRole>> _rolesByProfile;
    private readonly IReadOnlyDictionary<string, NativeInternalPropertyDefinition>
      _internalPropertiesById;
    private readonly IReadOnlyDictionary<string,
      NativeOfficialAcceptancePropertyDefinition> _acceptanceById;
    private readonly IReadOnlyDictionary<string, NativeOfficialCarrierPolicy>
      _policiesByEntity;
    private readonly IReadOnlyDictionary<string,
      NativeOfficialProjectionCarrierDefinition> _carriersById;
    private readonly IReadOnlyDictionary<string, NativeOfficialCarrierProbeRecord>
      _probesById;
    private readonly IReadOnlyDictionary<string, NativeOfficialEvidenceRecord>
      _evidenceById;

    private NativeReportingRuleCatalog(
      IDictionary<string, IReadOnlyList<string>> taskIdsByProfile,
      IDictionary<string, IReadOnlyList<NativeReportingCheckDefinition>> checksByProfile,
      IDictionary<string, IReadOnlyList<NativeReportingSemanticRole>> rolesByProfile,
      IEnumerable<NativeInternalPropertyDefinition> internalProperties,
      IEnumerable<NativeOfficialAcceptancePropertyDefinition> acceptance,
      IEnumerable<NativeOfficialCarrierPolicy> policies,
      IEnumerable<NativeOfficialProjectionCarrierDefinition> carriers,
      IEnumerable<NativeOfficialCarrierProbeRecord> probes,
      IEnumerable<NativeOfficialEvidenceRecord> evidence,
      IEnumerable<NativeStage02BMetricDefinition> metrics)
    {
      _taskIdsByProfile = new ReadOnlyDictionary<string, IReadOnlyList<string>>(
        new Dictionary<string, IReadOnlyList<string>>(
          taskIdsByProfile,
          StringComparer.Ordinal));
      _checksByProfile = new ReadOnlyDictionary<string,
        IReadOnlyList<NativeReportingCheckDefinition>>(
          new Dictionary<string, IReadOnlyList<NativeReportingCheckDefinition>>(
            checksByProfile,
            StringComparer.Ordinal));
      _rolesByProfile = new ReadOnlyDictionary<string,
        IReadOnlyList<NativeReportingSemanticRole>>(
          new Dictionary<string, IReadOnlyList<NativeReportingSemanticRole>>(
            rolesByProfile,
            StringComparer.Ordinal));
      InternalProperties = Freeze(internalProperties);
      _internalPropertiesById = Index(
        InternalProperties,
        value => value.PropertyId,
        "internal property");
      OfficialAcceptanceProperties = Freeze(acceptance);
      OfficialAcceptancePropertyIds = Freeze(
        OfficialAcceptanceProperties.Select(value => value.PropertyId));
      _acceptanceById = Index(
        OfficialAcceptanceProperties,
        value => value.PropertyId,
        "official acceptance property");
      _policiesByEntity = Index(policies, value => value.IfcEntity, "carrier policy");
      _carriersById = Index(carriers, value => value.CarrierId, "projection carrier");
      _probesById = Index(probes, value => value.ProbeId, "carrier probe");
      _evidenceById = Index(evidence, value => value.EvidenceId, "official evidence");
      Stage02BMetrics = Freeze(metrics);
    }

    internal static NativeReportingRuleCatalog Current => LazyCurrent.Value;
    internal IReadOnlyList<NativeInternalPropertyDefinition> InternalProperties
    {
      get;
    }
    internal IReadOnlyList<string> OfficialAcceptancePropertyIds { get; }
    internal IReadOnlyList<NativeOfficialAcceptancePropertyDefinition>
      OfficialAcceptanceProperties { get; }
    internal IReadOnlyList<NativeStage02BMetricDefinition> Stage02BMetrics { get; }

    internal IReadOnlyList<string> GetTaskIds(string modelFileType)
    {
      return TryGet(_taskIdsByProfile, modelFileType);
    }

    internal IReadOnlyList<NativeReportingCheckDefinition> GetChecks(
      string modelFileType)
    {
      return TryGet(_checksByProfile, modelFileType);
    }

    internal IReadOnlyList<NativeReportingSemanticRole> GetSemanticRoles(
      string modelFileType)
    {
      return TryGet(_rolesByProfile, modelFileType);
    }

    internal NativeOfficialAcceptancePropertyDefinition
      GetOfficialAcceptanceProperty(string propertyId)
    {
      return RequiredLookup(_acceptanceById, propertyId, "official acceptance property");
    }

    internal NativeInternalPropertyDefinition GetInternalProperty(string propertyId)
    {
      return RequiredLookup(_internalPropertiesById, propertyId, "internal property");
    }

    internal NativeOfficialCarrierPolicy GetCarrierPolicy(string ifcEntity)
    {
      return RequiredLookup(_policiesByEntity, ifcEntity, "carrier policy");
    }

    internal NativeOfficialProjectionCarrierDefinition GetProjectionCarrier(
      string carrierId)
    {
      return RequiredLookup(_carriersById, carrierId, "projection carrier");
    }

    internal NativeOfficialCarrierProbeRecord GetCarrierProbe(string probeId)
    {
      return RequiredLookup(_probesById, probeId, "carrier probe");
    }

    internal NativeOfficialEvidenceRecord GetOfficialEvidence(string evidenceId)
    {
      return RequiredLookup(_evidenceById, evidenceId, "official evidence");
    }

    private static NativeReportingRuleCatalog LoadCurrent()
    {
      return Load(
        RulePackageIdentityReader.ReadEmbeddedEnvelope(),
        NativeRuleCatalog.Current,
        NativeStage02RuleCatalog.Current);
    }

    internal static NativeReportingRuleCatalog Load(
      RulePackageEnvelope envelope,
      NativeRuleCatalog rules,
      NativeStage02RuleCatalog stage02)
    {
      if (envelope == null || envelope.Identity == null
        || rules == null || stage02 == null)
        throw new ArgumentNullException(nameof(envelope));
      if (!string.Equals(
          envelope.Identity.RulePackageSha256,
          rules.Identity.RulePackageSha256,
          StringComparison.Ordinal)
        || !string.Equals(
          envelope.Identity.RulePackageSha256,
          stage02.Identity.RulePackageSha256,
          StringComparison.Ordinal))
      {
        throw new InvalidDataException("HBR reporting 目录规则身份不一致。");
      }

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
        exception is ArgumentException || exception is InvalidOperationException)
      {
        throw new InvalidDataException("HBR nativeReporting 无法反序列化。", exception);
      }
      if (dto == null || dto.nativeReporting == null
        || !string.Equals(dto.nativeReporting.schemaVersion, "1.0.0", StringComparison.Ordinal))
        throw new InvalidDataException("HBR nativeReporting 缺失或版本无效。");

      NativeReportingDto source = dto.nativeReporting;
      ReportingProfileDto[] profiles = source.profiles
        ?? Array.Empty<ReportingProfileDto>();
      if (profiles.Length != 1
        || !string.Equals(profiles[0].modelFileType, "总平模型", StringComparison.Ordinal)
        || !profiles[0].strictNoNotApplicable)
        throw new InvalidDataException("HBR reporting phase1 profile 无效。");
      string[] profileTaskIds = profiles[0].taskIds ?? Array.Empty<string>();
      NativeModelProfile ruleProfile = rules.ModelProfiles.SingleOrDefault(value =>
        string.Equals(value.ProfileId, profiles[0].modelFileType, StringComparison.Ordinal));
      if (ruleProfile == null
        || !profileTaskIds.SequenceEqual(ruleProfile.TaskIds, StringComparer.Ordinal))
        throw new InvalidDataException("HBR reporting profile taskIds 与基础规则不一致。");

      NativeInternalPropertyDefinition[] internalProperties =
        (source.internalProperties ?? Array.Empty<InternalPropertyDto>())
        .Select(MapInternalProperty)
        .ToArray();
      IReadOnlyDictionary<string, NativeInternalPropertyDefinition>
        internalById = Index(
          internalProperties,
          value => value.PropertyId,
          "internal property");

      NativeReportingSemanticRole[] roles =
        (source.semanticRoles ?? Array.Empty<SemanticRoleDto>())
        .Select(MapSemanticRole)
        .ToArray();
      var rolesByTask = new Dictionary<string, NativeReportingSemanticRole>(
        StringComparer.Ordinal);
      foreach (NativeReportingSemanticRole role in roles)
      {
        if (!rules.TasksById.TryGetValue(role.TaskId, out NativeTaskDefinition task)
          || rolesByTask.ContainsKey(role.TaskId)
          || !task.AttributeRequirements.SequenceEqual(
            role.AttributeMappings.Select(value => value.AttributeRequirement),
            StringComparer.Ordinal))
          throw new InvalidDataException("HBR semantic role attribute mapping 断链。");
        foreach (NativeReportingAttributeMapping mapping in role.AttributeMappings)
        {
          if (string.Equals(mapping.DefinitionSource, "RULE_PROPERTY", StringComparison.Ordinal))
          {
            if (!stage02.PropertiesById.ContainsKey(mapping.InternalPropertyId))
              throw new InvalidDataException("HBR semantic mapping 引用未知 propertyId。");
          }
          else if (string.Equals(
            mapping.DefinitionSource,
            "NATIVE_INTERNAL_EXTENSION",
            StringComparison.Ordinal))
          {
            if (!internalById.ContainsKey(mapping.InternalPropertyId))
              throw new InvalidDataException("HBR semantic mapping 引用未知 internal propertyId。");
          }
          else
          {
            throw new InvalidDataException("HBR semantic mapping definitionSource 无效。");
          }
        }
        rolesByTask.Add(role.TaskId, role);
      }
      if (roles.Length != 13
        || roles.SelectMany(value => value.AttributeMappings).Count() != 37)
        throw new InvalidDataException("HBR semantic role 计数无效。");
      string[] referencedInternalIds = roles
        .SelectMany(value => value.AttributeMappings)
        .Where(value => string.Equals(
          value.DefinitionSource,
          "NATIVE_INTERNAL_EXTENSION",
          StringComparison.Ordinal))
        .Select(value => value.InternalPropertyId)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      if (internalProperties.Length != 10
        || !referencedInternalIds.SequenceEqual(
          internalById.Keys.OrderBy(value => value, StringComparer.Ordinal),
          StringComparer.Ordinal))
      {
        throw new InvalidDataException(
          "HBR internal properties 存在孤儿、缺失或计数漂移。");
      }
      ValidateEvaluationPolicies(source, profileTaskIds, rules);

      NativeOfficialProjectionCarrierDefinition[] carriers =
        (source.officialProjectionCarriers
          ?? Array.Empty<ProjectionCarrierDto>())
        .Select(MapCarrier)
        .ToArray();
      NativeOfficialCarrierProbeRecord[] probes =
        (source.officialCarrierProbeRecords ?? Array.Empty<CarrierProbeDto>())
        .Select(MapProbe)
        .ToArray();
      NativeOfficialEvidenceRecord[] evidence =
        (source.officialEvidenceRecords ?? Array.Empty<OfficialEvidenceDto>())
        .Select(MapEvidence)
        .ToArray();
      IReadOnlyDictionary<string, NativeOfficialProjectionCarrierDefinition>
        carriersById = Index(carriers, value => value.CarrierId, "projection carrier");
      IReadOnlyDictionary<string, NativeOfficialCarrierProbeRecord> probesById =
        Index(probes, value => value.ProbeId, "carrier probe");
      IReadOnlyDictionary<string, NativeOfficialEvidenceRecord> evidenceById =
        Index(evidence, value => value.EvidenceId, "official evidence");

      NativeStage02BMetricDefinition[] metrics =
        (source.stage02BMetrics ?? Array.Empty<Stage02BMetricDto>())
        .Select(value => MapMetric(
          value,
          stage02,
          carriersById,
          probesById,
          evidenceById))
        .OrderBy(value => value.Sequence)
        .ToArray();
      if (metrics.Length != 6
        || metrics.Select(value => value.PropertyId)
          .Distinct(StringComparer.Ordinal).Count() != metrics.Length)
        throw new InvalidDataException("HBR Stage02B metric 计数或 propertyId 无效。");
      ValidateNoOrphans(carriersById, probesById, evidenceById, metrics);

      NativeOfficialCarrierPolicy[] policies =
        (source.officialCarrierPolicies ?? Array.Empty<CarrierPolicyDto>())
        .Select(MapPolicy)
        .ToArray();
      ValidatePolicies(policies, metrics);

      Dictionary<string, NativeReportingSourceStage> acceptanceSources =
        BuildAcceptanceSources(source, rules, stage02, roles, metrics);
      string[] acceptanceIds = source.officialAcceptancePropertyIds
        ?? Array.Empty<string>();
      if (acceptanceIds.Length == 0
        || !acceptanceIds.SequenceEqual(
          acceptanceIds.OrderBy(value => value, StringComparer.Ordinal),
          StringComparer.Ordinal)
        || acceptanceIds.Distinct(StringComparer.Ordinal).Count()
          != acceptanceIds.Length
        || !acceptanceIds.SequenceEqual(
          acceptanceSources.Keys.OrderBy(value => value, StringComparer.Ordinal),
          StringComparer.Ordinal))
      {
        throw new InvalidDataException("HBR official acceptance propertyIds 投影不一致。");
      }
      NativeOfficialAcceptancePropertyDefinition[] acceptance = acceptanceIds
        .Select(propertyId =>
        {
          if (!stage02.PropertiesById.TryGetValue(
            propertyId,
            out NativeStage02PropertyDefinition property))
            throw new InvalidDataException("HBR acceptance propertyId 无法解析。");
          return new NativeOfficialAcceptancePropertyDefinition
          {
            PropertyId = propertyId,
            Identity = Identity(property),
            DeclaredIfcType = property.DeclaredIfcType,
            CanonicalUnit = property.CanonicalUnit,
            ParameterGuid = property.ParameterGuid,
            BindingScope = property.BindingScope,
            SourceStage = acceptanceSources[propertyId]
          };
        })
        .ToArray();

      NativeReportingCheckDefinition[] checks = BuildChecks(
        source,
        profileTaskIds,
        rules,
        stage02,
        rolesByTask,
        internalById,
        metrics);
      var taskIdsByProfile = new Dictionary<string, IReadOnlyList<string>>(
        StringComparer.Ordinal)
      {
        [profiles[0].modelFileType] = Freeze(profileTaskIds)
      };
      var checksByProfile = new Dictionary<string,
        IReadOnlyList<NativeReportingCheckDefinition>>(StringComparer.Ordinal)
      {
        [profiles[0].modelFileType] = Freeze(checks)
      };
      var rolesByProfile = new Dictionary<string,
        IReadOnlyList<NativeReportingSemanticRole>>(StringComparer.Ordinal)
      {
        [profiles[0].modelFileType] = Freeze(roles)
      };
      return new NativeReportingRuleCatalog(
        taskIdsByProfile,
        checksByProfile,
        rolesByProfile,
        internalProperties,
        acceptance,
        policies,
        carriers,
        probes,
        evidence,
        metrics);
    }

    private static NativeInternalPropertyDefinition MapInternalProperty(
      InternalPropertyDto value)
    {
      if (value == null || value.revit == null
        || !Guid.TryParse(value.propertyId, out Guid propertyId)
        || !Guid.TryParse(value.revit.parameterGuid, out Guid parameterGuid)
        || propertyId != parameterGuid)
        throw new InvalidDataException("HBR internal property GUID 无效。");
      NativeOfficialCarrierEvidenceStatus status = MapStatus(value.evidenceStatus);
      if (status != NativeOfficialCarrierEvidenceStatus.InternalOnly
        || value.officialExportVerified
        || !string.Equals(value.revit.bindingScope, "INSTANCE", StringComparison.Ordinal))
        throw new InvalidDataException("HBR internal property 证据状态无效。");
      return new NativeInternalPropertyDefinition
      {
        PropertyId = propertyId.ToString("D"),
        CanonicalKey = Required(value.canonicalKey, "internalProperties.canonicalKey"),
        DisplayName = value.displayName ?? string.Empty,
        ValueKind = value.valueKind ?? string.Empty,
        CanonicalUnit = value.canonicalUnit ?? string.Empty,
        ParameterGuid = parameterGuid,
        ParameterName = value.revit.parameterName ?? string.Empty,
        StorageType = value.revit.storageType ?? string.Empty,
        ParameterType = value.revit.parameterType ?? string.Empty,
        BindingScope = value.revit.bindingScope,
        EvidenceStatus = status,
        OfficialExportVerified = false
      };
    }

    private static NativeReportingSemanticRole MapSemanticRole(SemanticRoleDto value)
    {
      if (value == null)
        throw new InvalidDataException("HBR semantic role 为空。");
      NativeOfficialCarrierEvidenceStatus internalStatus =
        MapStatus(value.internalCarrierStatus);
      NativeOfficialCarrierEvidenceStatus officialStatus =
        MapStatus(value.officialCarrierStatus);
      if (internalStatus != NativeOfficialCarrierEvidenceStatus.InternalOnly
        || officialStatus != NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt)
        throw new InvalidDataException("HBR semantic role carrier 状态无效。");
      return new NativeReportingSemanticRole
      {
        RoleId = Required(value.roleId, "semanticRoles.roleId"),
        TaskId = Required(value.taskId, "semanticRoles.taskId"),
        DisplayName = value.displayName ?? string.Empty,
        CandidateAliases = Freeze(value.candidateAliases),
        InternalCarrierStatus = internalStatus,
        OfficialCarrierStatus = officialStatus,
        AttributeMappings = Freeze(
          (value.attributeMappings ?? Array.Empty<AttributeMappingDto>())
          .Select(mapping => new NativeReportingAttributeMapping
          {
            AttributeRequirement = Required(
              mapping.attributeRequirement,
              "attributeMappings.attributeRequirement"),
            InternalPropertyId = Required(
              mapping.internalPropertyId,
              "attributeMappings.internalPropertyId"),
            DefinitionSource = Required(
              mapping.definitionSource,
              "attributeMappings.definitionSource")
          }))
      };
    }

    private static NativeStage02BMetricDefinition MapMetric(
      Stage02BMetricDto value,
      NativeStage02RuleCatalog stage02,
      IReadOnlyDictionary<string, NativeOfficialProjectionCarrierDefinition> carriers,
      IReadOnlyDictionary<string, NativeOfficialCarrierProbeRecord> probes,
      IReadOnlyDictionary<string, NativeOfficialEvidenceRecord> evidence)
    {
      if (value == null
        || !stage02.PropertiesById.TryGetValue(
          value.propertyId ?? string.Empty,
          out NativeStage02PropertyDefinition property)
        || !string.Equals(value.identity, Identity(property), StringComparison.Ordinal)
        || !string.Equals(value.source, "MANUAL_INPUT", StringComparison.Ordinal))
        throw new InvalidDataException("HBR Stage02B metric identity 无效。");
      NativeOfficialCarrierEvidenceStatus status = MapStatus(
        value.officialCarrierStatus);
      if (status != NativeOfficialCarrierEvidenceStatus.Verified
        && status != NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt)
        throw new InvalidDataException("HBR Stage02B metric carrier 状态无效。");
      if (status == NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt)
      {
        if (value.officialExportVerified
          || !string.IsNullOrEmpty(value.officialProjectionCarrierId)
          || !string.IsNullOrEmpty(value.officialCarrierProbeRef)
          || !string.IsNullOrEmpty(value.officialEvidenceRef))
          throw new InvalidDataException("HBR pending metric 包含越权官方引用。");
      }
      else
      {
        if (!carriers.TryGetValue(
            value.officialProjectionCarrierId ?? string.Empty,
            out NativeOfficialProjectionCarrierDefinition carrier)
          || !probes.TryGetValue(
            value.officialCarrierProbeRef ?? string.Empty,
            out NativeOfficialCarrierProbeRecord probe)
          || !string.Equals(carrier.PropertyId, value.propertyId, StringComparison.Ordinal)
          || !string.Equals(probe.PropertyId, value.propertyId, StringComparison.Ordinal)
          || !string.Equals(carrier.ParameterGuid, value.propertyId, StringComparison.Ordinal)
          || !string.Equals(probe.ObservedParameterGuid, value.propertyId, StringComparison.Ordinal))
          throw new InvalidDataException("HBR verified metric carrier/probe 外键无效。");
        if (value.officialExportVerified)
        {
          if (!evidence.TryGetValue(
              value.officialEvidenceRef ?? string.Empty,
              out NativeOfficialEvidenceRecord record)
            || !string.Equals(record.PropertyId, value.propertyId, StringComparison.Ordinal))
            throw new InvalidDataException("HBR verified metric evidence 外键无效。");
        }
        else if (!string.IsNullOrEmpty(value.officialEvidenceRef))
        {
          throw new InvalidDataException("HBR 未验证导出包含 evidence 外键。");
        }
      }
      return new NativeStage02BMetricDefinition
      {
        PropertyId = value.propertyId,
        Identity = value.identity,
        Sequence = value.sequence,
        Source = value.source,
        Property = property,
        OfficialExportVerified = value.officialExportVerified,
        OfficialCarrierStatus = status,
        OfficialProjectionCarrierId = value.officialProjectionCarrierId ?? string.Empty,
        OfficialCarrierProbeRef = value.officialCarrierProbeRef ?? string.Empty,
        OfficialEvidenceRef = value.officialEvidenceRef ?? string.Empty
      };
    }

    private static Dictionary<string, NativeReportingSourceStage>
      BuildAcceptanceSources(
        NativeReportingDto source,
        NativeRuleCatalog rules,
        NativeStage02RuleCatalog stage02,
        IEnumerable<NativeReportingSemanticRole> roles,
        IEnumerable<NativeStage02BMetricDefinition> metrics)
    {
      var result = new Dictionary<string, NativeReportingSourceStage>(
        StringComparer.Ordinal);
      foreach (string fieldKey in source.stage01FieldKeys ?? Array.Empty<string>())
      {
        NativeStage01FieldDefinition field = ResolveStage01Field(rules, fieldKey);
        if (!string.IsNullOrEmpty(field.PropertyId))
          AddAcceptanceSource(result, field.PropertyId, NativeReportingSourceStage.Stage01);
      }
      foreach (string propertyId in source.planningTargetPropertyIds
        ?? Array.Empty<string>())
        AddAcceptanceSource(result, propertyId, NativeReportingSourceStage.Stage01);
      foreach (NativeReportingAttributeMapping mapping in roles
        .SelectMany(value => value.AttributeMappings)
        .Where(value => string.Equals(
          value.DefinitionSource,
          "RULE_PROPERTY",
          StringComparison.Ordinal)))
        AddAcceptanceSource(result, mapping.InternalPropertyId, NativeReportingSourceStage.Stage02A);
      foreach (NativeStage02PropertyDefinition property in
        stage02.PropertiesForRole("SITE_GREEN_OBJECT"))
        AddAcceptanceSource(result, property.PropertyId, NativeReportingSourceStage.Stage02A);
      foreach (NativeStage02BMetricDefinition metric in metrics)
        AddAcceptanceSource(result, metric.PropertyId, NativeReportingSourceStage.Stage02B);
      return result;
    }

    private static void ValidateEvaluationPolicies(
      NativeReportingDto source,
      IReadOnlyList<string> profileTaskIds,
      NativeRuleCatalog rules)
    {
      var expectedGeometry = new List<string>();
      var expectedProperties = new List<string>();
      foreach (string taskId in profileTaskIds)
      {
        NativeTaskDefinition task = rules.TasksById[taskId];
        expectedGeometry.AddRange(task.GeometryChecks.Select(ruleText =>
          taskId + "\u001f" + ruleText));
        expectedProperties.AddRange(task.PropertyChecks.Select(ruleText =>
          taskId + "\u001f" + ruleText));
      }
      string[] actualGeometry = (source.geometryEvaluationPolicies
        ?? Array.Empty<EvaluationPolicyDto>())
        .Select(value => Required(value.taskId, "geometry policy taskId")
          + "\u001f" + Required(value.ruleText, "geometry policy ruleText"))
        .ToArray();
      string[] actualProperties = (source.propertyEvaluationPolicies
        ?? Array.Empty<EvaluationPolicyDto>())
        .Select(value => Required(value.taskId, "property policy taskId")
          + "\u001f" + Required(value.ruleText, "property policy ruleText"))
        .ToArray();
      if (!actualGeometry.SequenceEqual(expectedGeometry, StringComparer.Ordinal)
        || !actualProperties.SequenceEqual(
          expectedProperties,
          StringComparer.Ordinal))
      {
        throw new InvalidDataException(
          "HBR geometry/property evaluation policy 未逐字逐序覆盖 task 规则。");
      }
    }

    private static void ValidateNoOrphans(
      IReadOnlyDictionary<string, NativeOfficialProjectionCarrierDefinition> carriers,
      IReadOnlyDictionary<string, NativeOfficialCarrierProbeRecord> probes,
      IReadOnlyDictionary<string, NativeOfficialEvidenceRecord> evidence,
      IReadOnlyList<NativeStage02BMetricDefinition> metrics)
    {
      string[] referencedCarriers = metrics
        .Select(value => value.OfficialProjectionCarrierId)
        .Where(value => !string.IsNullOrEmpty(value))
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      string[] referencedProbes = metrics
        .Select(value => value.OfficialCarrierProbeRef)
        .Where(value => !string.IsNullOrEmpty(value))
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      string[] referencedEvidence = metrics
        .Select(value => value.OfficialEvidenceRef)
        .Where(value => !string.IsNullOrEmpty(value))
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      if (!referencedCarriers.SequenceEqual(
          carriers.Keys.OrderBy(value => value, StringComparer.Ordinal),
          StringComparer.Ordinal)
        || !referencedProbes.SequenceEqual(
          probes.Keys.OrderBy(value => value, StringComparer.Ordinal),
          StringComparer.Ordinal)
        || !referencedEvidence.SequenceEqual(
          evidence.Keys.OrderBy(value => value, StringComparer.Ordinal),
          StringComparer.Ordinal))
      {
        throw new InvalidDataException(
          "HBR official carrier/probe/evidence 存在孤儿或缺失引用。");
      }
    }

    private static void AddAcceptanceSource(
      IDictionary<string, NativeReportingSourceStage> values,
      string propertyId,
      NativeReportingSourceStage stage)
    {
      if (values.TryGetValue(propertyId, out NativeReportingSourceStage existing))
      {
        if (existing != stage)
          throw new InvalidDataException(
            "HBR official acceptance propertyId 跨阶段归属冲突：" + propertyId);
        return;
      }
      values.Add(propertyId, stage);
    }

    private static NativeReportingCheckDefinition[] BuildChecks(
      NativeReportingDto source,
      IReadOnlyList<string> profileTaskIds,
      NativeRuleCatalog rules,
      NativeStage02RuleCatalog stage02,
      IReadOnlyDictionary<string, NativeReportingSemanticRole> rolesByTask,
      IReadOnlyDictionary<string, NativeInternalPropertyDefinition> internalById,
      IReadOnlyList<NativeStage02BMetricDefinition> metrics)
    {
      var checks = new List<NativeReportingCheckDefinition>();
      string modelFileType = "总平模型";
      string[] stage01Keys = source.stage01FieldKeys ?? Array.Empty<string>();
      for (int index = 0; index < stage01Keys.Length; index++)
      {
        NativeStage01FieldDefinition field = ResolveStage01Field(rules, stage01Keys[index]);
        bool internalField = string.IsNullOrEmpty(field.PropertyId);
        checks.Add(new NativeReportingCheckDefinition
        {
          CheckId = "STAGE01.FIELD." + Sha16(stage01Keys[index]),
          ModelFileType = modelFileType,
          Sequence = 10000 + index * 10,
          DisplayName = field.Label,
          SourceStage = NativeReportingSourceStage.Stage01,
          CheckKind = NativeReportingCheckKind.Stage01Field,
          FieldKey = field.FieldKey,
          PropertyId = field.PropertyId,
          InternalDefinitionSource = internalField ? "STAGE01_INTERNAL" : "RULE_PROPERTY",
          InternalCarrierStatus = internalField
            ? NativeOfficialCarrierEvidenceStatus.InternalOnly
            : NativeOfficialCarrierEvidenceStatus.Unknown,
          OfficialCarrierStatus = internalField
            ? NativeOfficialCarrierEvidenceStatus.InternalOnly
            : NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt,
          RemediationTarget = "OPEN_STAGE01"
        });
      }

      string[] planningIds = source.planningTargetPropertyIds
        ?? Array.Empty<string>();
      for (int index = 0; index < planningIds.Length; index++)
      {
        NativeStage02PropertyDefinition property =
          RequiredLookup(stage02.PropertiesById, planningIds[index], "planning target");
        checks.Add(new NativeReportingCheckDefinition
        {
          CheckId = "STAGE01.TARGET." + property.PropertyId,
          ModelFileType = modelFileType,
          Sequence = 20000 + index * 10,
          DisplayName = property.IfcProperty,
          SourceStage = NativeReportingSourceStage.Stage01,
          CheckKind = NativeReportingCheckKind.PlanningTarget,
          PropertyId = property.PropertyId,
          Unit = property.CanonicalUnit,
          OfficialCarrierStatus = NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt,
          RemediationTarget = "OPEN_STAGE01"
        });
      }

      NativeReportingSemanticRole[] roles = rolesByTask.Values
        .OrderBy(value => Array.IndexOf(profileTaskIds.ToArray(), value.TaskId))
        .ToArray();
      for (int index = 0; index < roles.Length; index++)
      {
        NativeReportingSemanticRole role = roles[index];
        NativeTaskDefinition task = rules.TasksById[role.TaskId];
        checks.Add(new NativeReportingCheckDefinition
        {
          CheckId = "STAGE02A.ROLE." + role.RoleId,
          ModelFileType = modelFileType,
          Sequence = 30000 + index * 10,
          DisplayName = role.DisplayName,
          SourceStage = NativeReportingSourceStage.Stage02A,
          CheckKind = NativeReportingCheckKind.SemanticRole,
          ConditionId = task.ConditionId,
          TaskId = role.TaskId,
          RoleId = role.RoleId,
          InternalCarrierStatus = role.InternalCarrierStatus,
          OfficialCarrierStatus = role.OfficialCarrierStatus,
          RemediationTarget = "OPEN_STAGE02A"
        });
      }

      var skeletonFields = new Dictionary<string, string>(StringComparer.Ordinal)
      {
        ["坐标系统"] = "IfcProject|Pset_申报信息属性集|坐标系名称",
        ["高程系统"] = "IfcProject|Pset_申报信息属性集|高程系名称",
        ["真北方向"] = "HBR|SpatialReference|TrueNorthAngle"
      };
      for (int taskIndex = 0; taskIndex < profileTaskIds.Count; taskIndex++)
      {
        NativeTaskDefinition task = rules.TasksById[profileTaskIds[taskIndex]];
        for (int ruleIndex = 0; ruleIndex < task.AttributeRequirements.Count; ruleIndex++)
        {
          string ruleText = task.AttributeRequirements[ruleIndex];
          string propertyId;
          string definitionSource;
          NativeOfficialCarrierEvidenceStatus internalStatus;
          NativeOfficialCarrierEvidenceStatus officialStatus;
          if (rolesByTask.TryGetValue(task.TaskId, out NativeReportingSemanticRole role))
          {
            NativeReportingAttributeMapping mapping = role.AttributeMappings[ruleIndex];
            propertyId = mapping.InternalPropertyId;
            definitionSource = mapping.DefinitionSource;
            internalStatus = NativeOfficialCarrierEvidenceStatus.InternalOnly;
            officialStatus = role.OfficialCarrierStatus;
          }
          else if (string.Equals(task.TaskId, "SITE.SKELETON", StringComparison.Ordinal)
            && skeletonFields.TryGetValue(ruleText, out string fieldKey))
          {
            NativeStage01FieldDefinition field = ResolveStage01Field(rules, fieldKey);
            propertyId = field.PropertyId;
            definitionSource = "STAGE01_FIELD";
            internalStatus = string.IsNullOrEmpty(propertyId)
              ? NativeOfficialCarrierEvidenceStatus.InternalOnly
              : NativeOfficialCarrierEvidenceStatus.Unknown;
            officialStatus = string.IsNullOrEmpty(propertyId)
              ? NativeOfficialCarrierEvidenceStatus.InternalOnly
              : NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt;
          }
          else
          {
            throw new InvalidDataException(
              "HBR task attribute requirement 缺少显式 mapping：" + task.TaskId);
          }
          checks.Add(new NativeReportingCheckDefinition
          {
            CheckId = "STAGE02A.ATTRIBUTE." + task.TaskId + "." + Sha16(ruleText),
            ModelFileType = modelFileType,
            Sequence = 35000 + taskIndex * 100 + ruleIndex,
            DisplayName = ruleText,
            SourceStage = string.Equals(task.TaskId, "SITE.SKELETON", StringComparison.Ordinal)
              ? NativeReportingSourceStage.Stage01
              : NativeReportingSourceStage.Stage02A,
            CheckKind = NativeReportingCheckKind.AttributeRequirement,
            ConditionId = task.ConditionId,
            TaskId = task.TaskId,
            PropertyId = propertyId,
            InternalDefinitionSource = definitionSource,
            InternalCarrierStatus = internalStatus,
            RuleText = ruleText,
            OfficialCarrierStatus = officialStatus,
            RemediationTarget = string.Equals(task.TaskId, "SITE.SKELETON", StringComparison.Ordinal)
              ? "OPEN_STAGE01"
              : "OPEN_STAGE02A"
          });
        }
        AddRuleChecks(
          checks,
          task,
          taskIndex,
          task.GeometryChecks,
          45000,
          NativeReportingCheckKind.Geometry);
        AddRuleChecks(
          checks,
          task,
          taskIndex,
          task.PropertyChecks,
          55000,
          NativeReportingCheckKind.PropertyConsistency);
        for (int ruleIndex = 0; ruleIndex < task.TargetComparisons.Count; ruleIndex++)
        {
          string target = task.TargetComparisons[ruleIndex];
          string actualPropertyId = TargetActualPropertyId(target);
          checks.Add(new NativeReportingCheckDefinition
          {
            CheckId = "STAGE03.TARGET." + task.TaskId + "." + target,
            ModelFileType = modelFileType,
            Sequence = 65000 + taskIndex * 100 + ruleIndex,
            DisplayName = target,
            SourceStage = NativeReportingSourceStage.CrossStage,
            CheckKind = NativeReportingCheckKind.TargetComparison,
            ConditionId = task.ConditionId,
            TaskId = task.TaskId,
            PropertyId = actualPropertyId,
            RuleText = target,
            TargetKey = target,
            RemediationTarget = string.IsNullOrEmpty(actualPropertyId)
              ? "TARGET_COMPARISON_MAPPING_MISSING"
              : "OPEN_STAGE01_OR_STAGE02B"
          });
        }
      }

      foreach (NativeStage02BMetricDefinition metric in metrics)
      {
        checks.Add(new NativeReportingCheckDefinition
        {
          CheckId = "STAGE02B.METRIC." + metric.PropertyId,
          ModelFileType = modelFileType,
          Sequence = 75000 + metric.Sequence,
          DisplayName = metric.Property.IfcProperty,
          SourceStage = NativeReportingSourceStage.Stage02B,
          CheckKind = NativeReportingCheckKind.Stage02BMetric,
          PropertyId = metric.PropertyId,
          Unit = metric.Property.CanonicalUnit,
          OfficialCarrierStatus = metric.OfficialCarrierStatus,
          OfficialProjectionCarrierId = metric.OfficialProjectionCarrierId,
          OfficialCarrierProbeRef = metric.OfficialCarrierProbeRef,
          OfficialEvidenceRef = metric.OfficialEvidenceRef,
          RemediationTarget = "OPEN_STAGE02B"
        });
      }

      foreach (SystemCheckDto system in source.systemChecks
        ?? Array.Empty<SystemCheckDto>())
      {
        checks.Add(new NativeReportingCheckDefinition
        {
          CheckId = Required(system.checkId, "systemChecks.checkId"),
          ModelFileType = modelFileType,
          Sequence = system.sequence,
          DisplayName = system.displayName ?? string.Empty,
          SourceStage = MapSourceStage(system.sourceStage),
          CheckKind = NativeReportingCheckKind.System,
          ApplicableBasis = system.applicableBasis ?? string.Empty,
          RemediationTarget = system.remediationTarget ?? string.Empty
        });
      }
      NativeReportingCheckDefinition[] result = checks
        .OrderBy(value => value.Sequence)
        .ThenBy(value => value.CheckId, StringComparer.Ordinal)
        .ToArray();
      if (result.Select(value => value.CheckId)
        .Distinct(StringComparer.Ordinal).Count() != result.Length)
        throw new InvalidDataException("HBR reporting checkId 重复。");
      return result;
    }

    private static void AddRuleChecks(
      ICollection<NativeReportingCheckDefinition> checks,
      NativeTaskDefinition task,
      int taskIndex,
      IReadOnlyList<string> rules,
      int sequenceBase,
      NativeReportingCheckKind kind)
    {
      for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
      {
        string ruleText = rules[ruleIndex];
        checks.Add(new NativeReportingCheckDefinition
        {
          CheckId = (kind == NativeReportingCheckKind.Geometry
              ? "STAGE02A.GEOMETRY."
              : "STAGE02A.PROPERTY.")
            + task.TaskId + "." + Sha16(ruleText),
          ModelFileType = task.ModelFileType,
          Sequence = sequenceBase + taskIndex * 100 + ruleIndex,
          DisplayName = ruleText,
          SourceStage = string.Equals(
            task.TaskId,
            "SITE.SKELETON",
            StringComparison.Ordinal)
              ? NativeReportingSourceStage.Stage01
              : NativeReportingSourceStage.Stage02A,
          CheckKind = kind,
          ConditionId = task.ConditionId,
          TaskId = task.TaskId,
          RuleText = ruleText,
          RemediationTarget = string.Equals(
            task.TaskId,
            "SITE.SKELETON",
            StringComparison.Ordinal)
              ? "OPEN_STAGE01"
              : "OPEN_STAGE02A"
        });
      }
    }

    private static NativeStage01FieldDefinition ResolveStage01Field(
      NativeRuleCatalog rules,
      string key)
    {
      NativeStage01FieldDefinition[] matches = rules.Stage01Fields.Where(field =>
        string.Equals(field.FieldKey, key, StringComparison.Ordinal)
        || string.Equals(
          string.Join("|", field.IfcEntity, field.IfcPropertySet, field.IfcProperty),
          key,
          StringComparison.Ordinal)).ToArray();
      if (matches.Length != 1)
        throw new InvalidDataException("HBR Stage01 reporting fieldKey 无法唯一解析：" + key);
      return matches[0];
    }

    private static string TargetActualPropertyId(string targetKey)
    {
      switch (targetKey)
      {
        case "planning.building_density":
          return "93e51676-237e-56a8-8f28-2da845422e2e";
        case "planning.floor_area_ratio":
          return "201a00ac-3672-5ded-83d2-ed96f81bfabf";
        case "planning.green_rate":
          return "f630ad47-b006-5127-badd-b1660cf996c3";
        default:
          return string.Empty;
      }
    }

    private static void ValidatePolicies(
      IReadOnlyList<NativeOfficialCarrierPolicy> policies,
      IReadOnlyList<NativeStage02BMetricDefinition> metrics)
    {
      if (policies.Select(value => value.IfcEntity)
        .Distinct(StringComparer.Ordinal).Count() != policies.Count)
        throw new InvalidDataException("HBR official carrier policy 重复。");
      string[] expectedEntities = metrics.Select(value => value.Property.IfcEntity)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      if (!policies.Select(value => value.IfcEntity)
        .OrderBy(value => value, StringComparer.Ordinal)
        .SequenceEqual(expectedEntities, StringComparer.Ordinal))
        throw new InvalidDataException("HBR official carrier policy 实体覆盖不完整。");
      foreach (NativeOfficialCarrierPolicy policy in policies)
      {
        NativeStage02BMetricDefinition[] entityMetrics = metrics.Where(value =>
          string.Equals(value.Property.IfcEntity, policy.IfcEntity, StringComparison.Ordinal))
          .ToArray();
        if (policy.EvidenceStatus == NativeOfficialCarrierEvidenceStatus.Verified)
        {
          if (entityMetrics.Length == 0 || entityMetrics.Any(value =>
            value.OfficialCarrierStatus != NativeOfficialCarrierEvidenceStatus.Verified))
            throw new InvalidDataException("HBR official policy 不得连带升级 pending metric。");
          string[] expectedProbes = entityMetrics
            .Select(value => value.OfficialCarrierProbeRef)
            .Where(value => !string.IsNullOrEmpty(value))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
          string[] expectedEvidence = entityMetrics
            .Select(value => value.OfficialEvidenceRef)
            .Where(value => !string.IsNullOrEmpty(value))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
          if (!policy.ProbeRefs.SequenceEqual(
              expectedProbes,
              StringComparer.Ordinal)
            || !policy.EvidenceRefs.SequenceEqual(
              expectedEvidence,
              StringComparer.Ordinal)
            || policy.OfficialExportVerified
              != entityMetrics.All(value => value.OfficialExportVerified))
          {
            throw new InvalidDataException(
              "HBR verified official policy 引用或导出状态与 metric 不一致。");
          }
        }
        else if (policy.EvidenceStatus != NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt
          || policy.OfficialExportVerified
          || policy.ProbeRefs.Count != 0
          || policy.EvidenceRefs.Count != 0)
        {
          throw new InvalidDataException("HBR pending official policy 状态无效。");
        }
      }
    }

    private static NativeOfficialCarrierPolicy MapPolicy(CarrierPolicyDto value)
    {
      if (value == null)
        throw new InvalidDataException("HBR official carrier policy 为空。");
      NativeOfficialCarrierEvidenceStatus status = MapStatus(value.evidenceStatus);
      if (status != NativeOfficialCarrierEvidenceStatus.Verified
        && status != NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt)
        throw new InvalidDataException("HBR official carrier policy 状态无效。");
      return new NativeOfficialCarrierPolicy
      {
        IfcEntity = Required(value.ifcEntity, "officialCarrierPolicies.ifcEntity"),
        InternalCarrier = value.internalCarrier ?? string.Empty,
        ProjectionPolicy = value.projectionPolicy ?? string.Empty,
        OfficialExportVerified = value.officialExportVerified,
        EvidenceStatus = status,
        ProbeRefs = Freeze(value.probeRefs),
        EvidenceRefs = Freeze(value.evidenceRefs)
      };
    }

    private static NativeOfficialProjectionCarrierDefinition MapCarrier(
      ProjectionCarrierDto value)
    {
      if (value == null)
        throw new InvalidDataException("HBR projection carrier 为空。");
      if (string.IsNullOrWhiteSpace(value.selectorKind)
        || !string.Equals(value.bindingScope, "INSTANCE", StringComparison.Ordinal)
        || !string.Equals(value.parameterGuid, value.propertyId, StringComparison.Ordinal))
      {
        throw new InvalidDataException("HBR projection carrier 结构无效。");
      }
      return new NativeOfficialProjectionCarrierDefinition
      {
        CarrierId = Required(value.carrierId, "officialProjectionCarriers.carrierId"),
        PropertyId = Required(value.propertyId, "officialProjectionCarriers.propertyId"),
        SelectorKind = value.selectorKind ?? string.Empty,
        RoleId = value.roleId ?? string.Empty,
        CategoryBuiltInId = value.categoryBuiltInId ?? string.Empty,
        ElementClass = value.elementClass ?? string.Empty,
        BindingScope = value.bindingScope ?? string.Empty,
        ParameterGuid = value.parameterGuid ?? string.Empty
      };
    }

    private static NativeOfficialCarrierProbeRecord MapProbe(CarrierProbeDto value)
    {
      if (value == null)
        throw new InvalidDataException("HBR carrier probe 为空。");
      string[] hashes =
      {
        value.sourceGoldenRvtSha256,
        value.probeSeedManifestSha256,
        value.probeRvtSha256,
        value.probeIfcSha256,
        value.hifcToolManifestSha256,
        value.hifcToolDllSha256
      };
      if (hashes.Any(hash => !IsSha256(hash))
        || string.IsNullOrWhiteSpace(value.hifcToolProductVersion)
        || string.IsNullOrWhiteSpace(value.observedRevitUniqueId)
        || string.IsNullOrWhiteSpace(value.observedIfcGlobalId)
        || string.IsNullOrWhiteSpace(value.observedSentinel)
        || !string.Equals(
          value.observedBindingScope,
          "INSTANCE",
          StringComparison.Ordinal)
        || !string.Equals(
          value.observedParameterGuid,
          value.propertyId,
          StringComparison.Ordinal))
      {
        throw new InvalidDataException("HBR carrier probe 证据不完整。");
      }
      return new NativeOfficialCarrierProbeRecord
      {
        ProbeId = Required(value.probeId, "officialCarrierProbeRecords.probeId"),
        PropertyId = Required(value.propertyId, "officialCarrierProbeRecords.propertyId"),
        SourceGoldenRvtSha256 = value.sourceGoldenRvtSha256 ?? string.Empty,
        ProbeSeedManifestSha256 = value.probeSeedManifestSha256 ?? string.Empty,
        ProbeRvtSha256 = value.probeRvtSha256 ?? string.Empty,
        ProbeIfcSha256 = value.probeIfcSha256 ?? string.Empty,
        HifcToolManifestSha256 = value.hifcToolManifestSha256 ?? string.Empty,
        HifcToolDllSha256 = value.hifcToolDllSha256 ?? string.Empty,
        HifcToolProductVersion = value.hifcToolProductVersion ?? string.Empty,
        ObservedRevitUniqueId = value.observedRevitUniqueId ?? string.Empty,
        ObservedIfcGlobalId = value.observedIfcGlobalId ?? string.Empty,
        ObservedBindingScope = value.observedBindingScope ?? string.Empty,
        ObservedParameterGuid = value.observedParameterGuid ?? string.Empty,
        ObservedSentinel = value.observedSentinel ?? string.Empty
      };
    }

    private static NativeOfficialEvidenceRecord MapEvidence(OfficialEvidenceDto value)
    {
      if (value == null)
        throw new InvalidDataException("HBR official evidence 为空。");
      string[] hashes =
      {
        value.goldenRvtSha256,
        value.hifctoolManifestSha256,
        value.hifctoolDllSha256,
        value.officialIfcSha256,
        value.ifcFluxReportSha256
      };
      if (hashes.Any(hash => !IsSha256(hash))
        || string.IsNullOrWhiteSpace(value.hifctoolProductVersion)
        || string.IsNullOrWhiteSpace(value.ifcFluxProductVersion)
        || string.IsNullOrWhiteSpace(value.observedRevitUniqueId)
        || string.IsNullOrWhiteSpace(value.observedIfcGlobalId)
        || !string.Equals(
          value.observedBindingScope,
          "INSTANCE",
          StringComparison.Ordinal)
        || !string.Equals(
          value.observedParameterGuid,
          value.propertyId,
          StringComparison.Ordinal))
      {
        throw new InvalidDataException("HBR official evidence 记录不完整。");
      }
      return new NativeOfficialEvidenceRecord
      {
        EvidenceId = Required(value.evidenceId, "officialEvidenceRecords.evidenceId"),
        PropertyId = Required(value.propertyId, "officialEvidenceRecords.propertyId"),
        GoldenRvtSha256 = value.goldenRvtSha256 ?? string.Empty,
        HifctoolManifestSha256 = value.hifctoolManifestSha256 ?? string.Empty,
        HifctoolDllSha256 = value.hifctoolDllSha256 ?? string.Empty,
        HifctoolProductVersion = value.hifctoolProductVersion ?? string.Empty,
        OfficialIfcSha256 = value.officialIfcSha256 ?? string.Empty,
        IfcFluxProductVersion = value.ifcFluxProductVersion ?? string.Empty,
        IfcFluxReportSha256 = value.ifcFluxReportSha256 ?? string.Empty,
        ObservedRevitUniqueId = value.observedRevitUniqueId ?? string.Empty,
        ObservedIfcGlobalId = value.observedIfcGlobalId ?? string.Empty,
        ObservedBindingScope = value.observedBindingScope ?? string.Empty,
        ObservedParameterGuid = value.observedParameterGuid ?? string.Empty
      };
    }

    private static NativeOfficialCarrierEvidenceStatus MapStatus(string value)
    {
      switch (value)
      {
        case "VERIFIED": return NativeOfficialCarrierEvidenceStatus.Verified;
        case "PENDING_GOLDEN_RVT":
          return NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt;
        case "INTERNAL_ONLY":
          return NativeOfficialCarrierEvidenceStatus.InternalOnly;
        default:
          throw new InvalidDataException("HBR official carrier evidence status 无效。");
      }
    }

    private static NativeReportingSourceStage MapSourceStage(string value)
    {
      switch (value)
      {
        case "CROSS_STAGE": return NativeReportingSourceStage.CrossStage;
        case "EXPORT_PREPARATION":
          return NativeReportingSourceStage.ExportPreparation;
        default:
          throw new InvalidDataException("HBR reporting sourceStage 无效。");
      }
    }

    private static string Sha16(string value)
    {
      byte[] hash;
      using (SHA256 algorithm = SHA256.Create())
        hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
      var result = new StringBuilder(16);
      for (int index = 0; index < 8; index++)
        result.Append(hash[index].ToString("x2"));
      return result.ToString();
    }

    private static bool IsSha256(string value)
    {
      return value != null
        && value.Length == 64
        && value.All(character =>
          (character >= '0' && character <= '9')
          || (character >= 'a' && character <= 'f'));
    }

    private static string Identity(NativeStage02PropertyDefinition property)
    {
      return string.Join(
        "|",
        property.IfcEntity,
        property.IfcPropertySet,
        property.IfcProperty);
    }

    private static string Required(string value, string path)
    {
      if (string.IsNullOrWhiteSpace(value))
        throw new InvalidDataException("HBR 字段为空：" + path);
      return value;
    }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values)
    {
      return new ReadOnlyCollection<T>((values ?? Array.Empty<T>()).ToArray());
    }

    private static IReadOnlyDictionary<string, T> Index<T>(
      IEnumerable<T> values,
      Func<T, string> keySelector,
      string label)
    {
      var result = new Dictionary<string, T>(StringComparer.Ordinal);
      foreach (T value in values ?? Array.Empty<T>())
      {
        string key = value == null ? string.Empty : keySelector(value);
        if (string.IsNullOrWhiteSpace(key) || result.ContainsKey(key))
          throw new InvalidDataException("HBR " + label + " ID 无效或重复。");
        result.Add(key, value);
      }
      return new ReadOnlyDictionary<string, T>(result);
    }

    private static IReadOnlyList<T> TryGet<T>(
      IReadOnlyDictionary<string, IReadOnlyList<T>> values,
      string key)
    {
      return values.TryGetValue(key ?? string.Empty, out IReadOnlyList<T> result)
        ? result
        : Array.Empty<T>();
    }

    private static T RequiredLookup<T>(
      IReadOnlyDictionary<string, T> values,
      string key,
      string label)
    {
      if (!values.TryGetValue(key ?? string.Empty, out T result))
        throw new InvalidDataException("HBR 未找到 " + label + "：" + key);
      return result;
    }

    private sealed class RulePackageDto
    {
      public NativeReportingDto nativeReporting { get; set; }
    }

    private sealed class NativeReportingDto
    {
      public string schemaVersion { get; set; }
      public ReportingProfileDto[] profiles { get; set; }
      public InternalPropertyDto[] internalProperties { get; set; }
      public SemanticRoleDto[] semanticRoles { get; set; }
      public string[] stage01FieldKeys { get; set; }
      public string[] planningTargetPropertyIds { get; set; }
      public Stage02BMetricDto[] stage02BMetrics { get; set; }
      public CarrierPolicyDto[] officialCarrierPolicies { get; set; }
      public ProjectionCarrierDto[] officialProjectionCarriers { get; set; }
      public CarrierProbeDto[] officialCarrierProbeRecords { get; set; }
      public OfficialEvidenceDto[] officialEvidenceRecords { get; set; }
      public string[] officialAcceptancePropertyIds { get; set; }
      public SystemCheckDto[] systemChecks { get; set; }
      public EvaluationPolicyDto[] geometryEvaluationPolicies { get; set; }
      public EvaluationPolicyDto[] propertyEvaluationPolicies { get; set; }
    }

    private sealed class ReportingProfileDto
    {
      public string modelFileType { get; set; }
      public bool strictNoNotApplicable { get; set; }
      public string[] taskIds { get; set; }
    }

    private sealed class InternalPropertyDto
    {
      public string propertyId { get; set; }
      public string canonicalKey { get; set; }
      public string displayName { get; set; }
      public string valueKind { get; set; }
      public string canonicalUnit { get; set; }
      public InternalRevitDto revit { get; set; }
      public string evidenceStatus { get; set; }
      public bool officialExportVerified { get; set; }
    }

    private sealed class InternalRevitDto
    {
      public string parameterGuid { get; set; }
      public string parameterName { get; set; }
      public string storageType { get; set; }
      public string parameterType { get; set; }
      public string bindingScope { get; set; }
    }

    private sealed class SemanticRoleDto
    {
      public string roleId { get; set; }
      public string taskId { get; set; }
      public string displayName { get; set; }
      public string[] candidateAliases { get; set; }
      public string internalCarrierStatus { get; set; }
      public string officialCarrierStatus { get; set; }
      public AttributeMappingDto[] attributeMappings { get; set; }
    }

    private sealed class AttributeMappingDto
    {
      public string attributeRequirement { get; set; }
      public string internalPropertyId { get; set; }
      public string definitionSource { get; set; }
    }

    private sealed class Stage02BMetricDto
    {
      public int sequence { get; set; }
      public string propertyId { get; set; }
      public string identity { get; set; }
      public string source { get; set; }
      public bool officialExportVerified { get; set; }
      public string officialCarrierStatus { get; set; }
      public string officialProjectionCarrierId { get; set; }
      public string officialCarrierProbeRef { get; set; }
      public string officialEvidenceRef { get; set; }
    }

    private sealed class CarrierPolicyDto
    {
      public string ifcEntity { get; set; }
      public string internalCarrier { get; set; }
      public string projectionPolicy { get; set; }
      public bool officialExportVerified { get; set; }
      public string evidenceStatus { get; set; }
      public string[] probeRefs { get; set; }
      public string[] evidenceRefs { get; set; }
    }

    private sealed class ProjectionCarrierDto
    {
      public string carrierId { get; set; }
      public string propertyId { get; set; }
      public string selectorKind { get; set; }
      public string roleId { get; set; }
      public string categoryBuiltInId { get; set; }
      public string elementClass { get; set; }
      public string bindingScope { get; set; }
      public string parameterGuid { get; set; }
    }

    private sealed class CarrierProbeDto
    {
      public string probeId { get; set; }
      public string propertyId { get; set; }
      public string sourceGoldenRvtSha256 { get; set; }
      public string probeSeedManifestSha256 { get; set; }
      public string probeRvtSha256 { get; set; }
      public string probeIfcSha256 { get; set; }
      public string hifcToolManifestSha256 { get; set; }
      public string hifcToolDllSha256 { get; set; }
      public string hifcToolProductVersion { get; set; }
      public string observedRevitUniqueId { get; set; }
      public string observedIfcGlobalId { get; set; }
      public string observedBindingScope { get; set; }
      public string observedParameterGuid { get; set; }
      public string observedSentinel { get; set; }
    }

    private sealed class OfficialEvidenceDto
    {
      public string evidenceId { get; set; }
      public string propertyId { get; set; }
      public string goldenRvtSha256 { get; set; }
      public string hifctoolManifestSha256 { get; set; }
      public string hifctoolDllSha256 { get; set; }
      public string hifctoolProductVersion { get; set; }
      public string officialIfcSha256 { get; set; }
      public string ifcFluxProductVersion { get; set; }
      public string ifcFluxReportSha256 { get; set; }
      public string observedRevitUniqueId { get; set; }
      public string observedIfcGlobalId { get; set; }
      public string observedBindingScope { get; set; }
      public string observedParameterGuid { get; set; }
    }

    private sealed class SystemCheckDto
    {
      public int sequence { get; set; }
      public string checkId { get; set; }
      public string displayName { get; set; }
      public string sourceStage { get; set; }
      public string applicableBasis { get; set; }
      public string remediationTarget { get; set; }
    }

    private sealed class EvaluationPolicyDto
    {
      public string taskId { get; set; }
      public string ruleText { get; set; }
    }
  }
}
