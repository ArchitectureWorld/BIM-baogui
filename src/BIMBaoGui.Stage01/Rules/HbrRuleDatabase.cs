using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace BIMBaoGui.Stage01.Rules
{
  public sealed class HbrRuleDatabase
  {
    public const string ResourceName =
      "BIMBaoGui.Stage01.Resources.HBR_RulePack.hbrpack";

    private static readonly Lazy<HbrRuleDatabase> LazyCurrent =
      CreateLazy(LoadCurrent);
    private static readonly IReadOnlyDictionary<string, string>
      ExpectedOwnerRuntimeStatuses =
        new ReadOnlyDictionary<string, string>(
          new Dictionary<string, string>(StringComparer.Ordinal)
          {
            { "BY_EXPORT_GUID", "SUPPORTED" },
            { "CANONICAL_SPATIAL_ZONE_RECORD", "NOT_IMPLEMENTED" },
            { "SINGLE_ENTITY_BY_TYPE", "SUPPORTED" },
            {
              "USER_SELECTED_EXPORTABLE_GENERIC_MODEL",
              "NOT_IMPLEMENTED"
            },
          });
    private static readonly IReadOnlyDictionary<string, string>
      ExpectedRequirementRuntimeStatuses =
        new ReadOnlyDictionary<string, string>(
          new Dictionary<string, string>(StringComparer.Ordinal)
          {
            { "CONDITIONAL", "SUPPORTED" },
            { "NOT_APPLICABLE", "SUPPORTED" },
            { "OPTIONAL", "SUPPORTED" },
            { "REQUIRED", "SUPPORTED" },
            { "UNCLASSIFIED", "UNCLASSIFIED_REQUIREMENT" },
          });

    private HbrRuleDatabase(HbrRulePackage package)
    {
      Package = package ?? throw new InvalidDataException(
        "HBRP package is null.");
      if (Package.PackageId != "HBR-WUHAN-PLANNING"
        || Package.PackageVersion != "1.0.0")
        throw new InvalidDataException(
          "HBRP runtime support policy requires HBR-WUHAN-PLANNING 1.0.0.");

      _runtimeStatusPrecedence = ValidateRuntimeStatusPrecedence(
        Package.RuntimeSupport.StatusPrecedence);
      _ownerRuntimeStatuses = BuildRuntimeStatusIndex(
        Package.RuntimeSupport.OwnerStrategies,
        support => support.OwnerStrategy,
        support => support.Status,
        ExpectedOwnerRuntimeStatuses,
        "RuntimeSupport.OwnerStrategies");
      _requirementRuntimeStatuses = BuildRuntimeStatusIndex(
        Package.RuntimeSupport.RequirementLevels,
        support => support.Level,
        support => support.Status,
        ExpectedRequirementRuntimeStatuses,
        "RuntimeSupport.RequirementLevels");

      var propertiesById = new Dictionary<string, HbrRuleProperty>(
        StringComparer.Ordinal);
      var propertiesByIfcIdentity =
        new Dictionary<HbrIfcIdentity, HbrRuleProperty>();
      var propertiesByParameterGuid =
        new Dictionary<Guid, HbrRuleProperty>();
      foreach (HbrRuleProperty property in package.Properties)
      {
        AddUnique(
          propertiesById,
          property.PropertyId,
          property,
          "PropertiesById");
        AddUnique(
          propertiesByIfcIdentity,
          new HbrIfcIdentity(
            property.Ifc.Entity,
            property.Ifc.PropertySet,
            property.Ifc.Property),
          property,
          "PropertiesByIfcIdentity");
        AddUnique(
          propertiesByParameterGuid,
          property.Revit.ParameterGuid,
          property,
          "PropertiesByParameterGuid");
      }

      var carrierRolesById = new Dictionary<string, HbrCarrierRole>(
        StringComparer.Ordinal);
      foreach (HbrCarrierRole role in package.CarrierRoles)
        AddUnique(
          carrierRolesById,
          role.RoleId,
          role,
          "CarrierRolesById");

      var profilesByModelFileType =
        new Dictionary<string, HbrModelProfile>(StringComparer.Ordinal);
      foreach (HbrModelProfile profile in package.ModelProfiles)
        AddUnique(
          profilesByModelFileType,
          profile.ProfileId,
          profile,
          "ProfilesByModelFileType");

      var tasksById = new Dictionary<string, HbrTaskRule>(
        StringComparer.Ordinal);
      foreach (HbrTaskRule task in package.Tasks)
        AddUnique(tasksById, task.TaskId, task, "TasksById");

      _suggestionPropertyIdsByRoleAlias =
        BuildSuggestionAliasIndex(package.Properties);

      PropertiesById = new ReadOnlyDictionary<string, HbrRuleProperty>(
        propertiesById);
      PropertiesByIfcIdentity =
        new ReadOnlyDictionary<HbrIfcIdentity, HbrRuleProperty>(
          propertiesByIfcIdentity);
      PropertiesByParameterGuid =
        new ReadOnlyDictionary<Guid, HbrRuleProperty>(
          propertiesByParameterGuid);
      CarrierRolesById = new ReadOnlyDictionary<string, HbrCarrierRole>(
        carrierRolesById);
      ProfilesByModelFileType =
        new ReadOnlyDictionary<string, HbrModelProfile>(
          profilesByModelFileType);
      TasksById = new ReadOnlyDictionary<string, HbrTaskRule>(tasksById);
    }

    public static HbrRuleDatabase Current => LazyCurrent.Value;

    public HbrRulePackage Package { get; }

    public IReadOnlyDictionary<string, HbrRuleProperty> PropertiesById
    {
      get;
    }

    public IReadOnlyDictionary<HbrIfcIdentity, HbrRuleProperty>
      PropertiesByIfcIdentity
    {
      get;
    }

    public IReadOnlyDictionary<Guid, HbrRuleProperty> PropertiesByParameterGuid
    {
      get;
    }

    public IReadOnlyDictionary<string, HbrCarrierRole> CarrierRolesById
    {
      get;
    }

    public IReadOnlyDictionary<string, HbrModelProfile> ProfilesByModelFileType
    {
      get;
    }

    public IReadOnlyDictionary<string, HbrTaskRule> TasksById { get; }

    public IReadOnlyList<string> GetSuggestionAliasPropertyIds(
      string roleId,
      string alias)
    {
      IReadOnlyList<string> propertyIds;
      return _suggestionPropertyIdsByRoleAlias.TryGetValue(
        SuggestionAliasKey(roleId, alias),
        out propertyIds)
          ? propertyIds
          : Array.Empty<string>();
    }

    public HbrRuntimeStatusDecision GetRuntimeStatusDecision(
      HbrRuleProperty property)
    {
      if (property == null)
        throw new ArgumentNullException(nameof(property));
      if (!_ownerRuntimeStatuses.TryGetValue(
        property.IfcWrite.OwnerStrategy,
        out string ownerStatus))
        throw new InvalidDataException(
          "HBRP unknown owner strategy for " + property.PropertyId + ".");
      if (!_requirementRuntimeStatuses.TryGetValue(
        property.Requirement.Level,
        out string requirementStatus))
        throw new InvalidDataException(
          "HBRP unknown requirement level for " + property.PropertyId + ".");

      string status = _runtimeStatusPrecedence.FirstOrDefault(
        value => value == ownerStatus || value == requirementStatus);
      switch (status)
      {
        case HbrRuntimeStatuses.NotImplemented:
          return new HbrRuntimeStatusDecision(
            status,
            HbrRuntimeReasonCodes.OwnerStrategyNotImplemented,
            "当前 IFC owner strategy 尚未实现："
              + property.IfcWrite.OwnerStrategy + "。");
        case HbrRuntimeStatuses.UnclassifiedRequirement:
          return new HbrRuntimeStatusDecision(
            status,
            HbrRuntimeReasonCodes.RequirementLevelUnclassified,
            "字段 requirement.level 为 "
              + property.Requirement.Level + "，需求等级待定。");
        case HbrRuntimeStatuses.OfficialEvidenceOnly:
          return new HbrRuntimeStatusDecision(
            status,
            HbrRuntimeReasonCodes.OfficialEvidenceOnly,
            "该字段仅用于官方证据对账，不自动形成写入策略。");
        case HbrRuntimeStatuses.Supported:
          return new HbrRuntimeStatusDecision(
            status,
            HbrRuntimeReasonCodes.Supported,
            "当前运行策略已支持。");
        default:
          throw new InvalidDataException(
            "HBRP has no runtime status for property "
              + property.PropertyId + ".");
      }
    }

    public string GetEffectiveRuntimeStatus(HbrRuleProperty property)
    {
      return GetRuntimeStatusDecision(property).Status;
    }

    public static HbrRuleDatabase Load(Stream stream)
    {
      return new HbrRuleDatabase(HbrRulePackageLoader.Load(stream));
    }

    internal static Lazy<HbrRuleDatabase> CreateLazy(
      Func<HbrRuleDatabase> factory)
    {
      return new Lazy<HbrRuleDatabase>(
        factory,
        LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private static HbrRuleDatabase LoadCurrent()
    {
      Assembly assembly = typeof(HbrRuleDatabase).Assembly;
      using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
      {
        if (stream == null)
          throw new InvalidDataException(
            "Missing exact HBRP embedded resource: " + ResourceName + ".");
        return Load(stream);
      }
    }

    private static void AddUnique<TKey, TValue>(
      IDictionary<TKey, TValue> dictionary,
      TKey key,
      TValue value,
      string indexName)
    {
      if (dictionary.ContainsKey(key))
        throw new InvalidDataException(
          "HBRP duplicate key in " + indexName + ": " + key + ".");
      dictionary.Add(key, value);
    }

    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>>
      _suggestionPropertyIdsByRoleAlias;
    private readonly IReadOnlyList<string> _runtimeStatusPrecedence;
    private readonly IReadOnlyDictionary<string, string> _ownerRuntimeStatuses;
    private readonly IReadOnlyDictionary<string, string> _requirementRuntimeStatuses;

    private static IReadOnlyList<string> ValidateRuntimeStatusPrecedence(
      IReadOnlyList<string> statuses)
    {
      string[] expected = {
        "NOT_IMPLEMENTED", "UNCLASSIFIED_REQUIREMENT",
        "OFFICIAL_EVIDENCE_ONLY", "SUPPORTED",
      };
      if (statuses == null || !statuses.SequenceEqual(expected))
        throw new InvalidDataException(
          "HBRP runtime status precedence is invalid.");
      return statuses;
    }

    private static IReadOnlyDictionary<string, string> BuildRuntimeStatusIndex<T>(
      IEnumerable<T> supports,
      Func<T, string> keySelector,
      Func<T, string> statusSelector,
      IReadOnlyDictionary<string, string> expected,
      string name)
    {
      var result = new Dictionary<string, string>(StringComparer.Ordinal);
      string[] known = {
        "SUPPORTED", "NOT_IMPLEMENTED", "UNCLASSIFIED_REQUIREMENT",
        "OFFICIAL_EVIDENCE_ONLY",
      };
      foreach (T support in supports ?? Array.Empty<T>())
      {
        string key = keySelector(support);
        string status = statusSelector(support);
        if (string.IsNullOrWhiteSpace(key) || !known.Contains(status)
          || result.ContainsKey(key))
          throw new InvalidDataException("HBRP invalid " + name + ".");
        result.Add(key, status);
      }
      if (result.Count == 0)
        throw new InvalidDataException("HBRP missing " + name + ".");
      if (result.Count != expected.Count || expected.Any(pair =>
        !result.ContainsKey(pair.Key) || result[pair.Key] != pair.Value))
        throw new InvalidDataException(
          "HBRP " + name + " must match the fixed runtime support mapping.");
      return new ReadOnlyDictionary<string, string>(result);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>
      BuildSuggestionAliasIndex(IEnumerable<HbrRuleProperty> properties)
    {
      var mutable = new Dictionary<string, List<string>>(
        StringComparer.Ordinal);
      foreach (HbrRuleProperty property in properties
        ?? Array.Empty<HbrRuleProperty>())
      {
        foreach (string roleId in property.CarrierRoleIds)
        {
          foreach (string alias in property.Suggestion.Aliases)
          {
            string key = SuggestionAliasKey(roleId, alias);
            List<string> propertyIds;
            if (!mutable.TryGetValue(key, out propertyIds))
            {
              propertyIds = new List<string>();
              mutable.Add(key, propertyIds);
            }
            if (!propertyIds.Contains(property.PropertyId))
              propertyIds.Add(property.PropertyId);
          }
        }
      }
      var frozen = mutable.ToDictionary(
        pair => pair.Key,
        pair => (IReadOnlyList<string>)new ReadOnlyCollection<string>(
          pair.Value.OrderBy(value => value, StringComparer.Ordinal).ToArray()),
        StringComparer.Ordinal);
      return new ReadOnlyDictionary<string, IReadOnlyList<string>>(frozen);
    }

    private static string SuggestionAliasKey(string roleId, string alias)
    {
      string normalizedRole = (roleId ?? string.Empty).Trim();
      string normalizedAlias = string.IsNullOrWhiteSpace(alias)
        ? string.Empty
        : alias.Trim()
          .Normalize(NormalizationForm.FormKC)
          .ToUpperInvariant();
      return normalizedRole + "\u001f" + normalizedAlias;
    }
  }

  public sealed class HbrIfcIdentity : IEquatable<HbrIfcIdentity>
  {
    public HbrIfcIdentity(string entity, string propertySet, string property)
    {
      if (entity == null)
        throw new ArgumentNullException(nameof(entity));
      if (propertySet == null)
        throw new ArgumentNullException(nameof(propertySet));
      if (property == null)
        throw new ArgumentNullException(nameof(property));
      Entity = entity;
      PropertySet = propertySet;
      Property = property;
    }

    public string Entity { get; }
    public string PropertySet { get; }
    public string Property { get; }

    public bool Equals(HbrIfcIdentity other)
    {
      return !ReferenceEquals(other, null)
        && StringComparer.Ordinal.Equals(Entity, other.Entity)
        && StringComparer.Ordinal.Equals(PropertySet, other.PropertySet)
        && StringComparer.Ordinal.Equals(Property, other.Property);
    }

    public override bool Equals(object obj)
    {
      return Equals(obj as HbrIfcIdentity);
    }

    public override int GetHashCode()
    {
      unchecked
      {
        int hash = 17;
        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Entity);
        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(PropertySet);
        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Property);
        return hash;
      }
    }

    public override string ToString()
    {
      return Entity + "|" + PropertySet + "|" + Property;
    }
  }
}
