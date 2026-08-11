using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIMBaoGui.Stage01.Rules;

namespace BIMBaoGui.Stage01.Hifc
{
  internal sealed class OfficialHifcMappingCatalog
  {
    private static readonly Lazy<OfficialHifcMappingCatalog> LazyInstance =
      new Lazy<OfficialHifcMappingCatalog>(() =>
        FromDatabase(HbrRuleDatabase.Current));

    private readonly Dictionary<string, OfficialHifcMapping> _byAlias;
    private readonly IReadOnlyCollection<OfficialHifcMapping> _mappings;

    private OfficialHifcMappingCatalog(
      IEnumerable<OfficialHifcMapping> mappings)
    {
      OfficialHifcMapping[] ordered = (mappings
        ?? throw new ArgumentNullException(nameof(mappings))).ToArray();
      _mappings = ordered;
      _byAlias = new Dictionary<string, OfficialHifcMapping>(
        StringComparer.OrdinalIgnoreCase);
      foreach (OfficialHifcMapping mapping in ordered)
      {
        if (mapping == null)
          throw new InvalidDataException("H-IFC mapping contains a null record.");
        AddAlias(mapping.PropertyId, mapping);
        AddAlias(mapping.ParameterGuid.ToString("D"), mapping);
        AddAlias(mapping.ParameterName, mapping);
      }
    }

    public static OfficialHifcMappingCatalog Instance => LazyInstance.Value;

    public IReadOnlyCollection<OfficialHifcMapping> Mappings => _mappings;

    internal static OfficialHifcMappingCatalog FromDatabase(
      HbrRuleDatabase database)
    {
      if (database == null) throw new ArgumentNullException(nameof(database));

      var result = new List<OfficialHifcMapping>();
      var projectedPropertyIds = new HashSet<string>(StringComparer.Ordinal);
      foreach (HbrLegacyAlias alias in database.Package.LegacyAliases)
      {
        if (!projectedPropertyIds.Add(alias.PropertyId))
          throw new InvalidDataException(
            "HBR legacy alias repeats propertyId: " + alias.PropertyId);
        if (!database.PropertiesById.TryGetValue(
          alias.PropertyId,
          out HbrRuleProperty property))
          throw new InvalidDataException(
            "HBR legacy alias references unknown propertyId: "
            + alias.PropertyId);
        if (!property.OfficialPlugin.InExtracted166)
          throw new InvalidDataException(
            "HBR legacy alias references a non-official property: "
            + alias.PropertyId);
        HbrLegacyProjection legacy = property.OfficialPlugin.LegacyProjection;
        if (legacy == null)
          throw new InvalidDataException(
            "HBR official property is missing legacyProjection: "
            + alias.PropertyId);

        LegacyIfcIdentity identity = ParseOriginalIdentity(
          property.OfficialPlugin.OriginalIdentity,
          alias.PropertyId);
        string propertySet = identity.PropertySet.StartsWith(
          "Pset_",
          StringComparison.Ordinal)
          ? identity.PropertySet.Substring("Pset_".Length)
          : identity.PropertySet;
        string sourceOverride = legacy.SourceParameterOverride;
        string officialSourceName = string.IsNullOrWhiteSpace(sourceOverride)
          ? identity.Property.Trim()
          : sourceOverride.Trim();
        string bindingScope = property.Revit.BindingScope.Trim();
        string ifcDataType = property.Ifc.DeclaredType;
        string officialSourceParameterType =
          OfficialSourceParameterTypePolicy.Resolve(ifcDataType);

        result.Add(new OfficialHifcMapping
        {
          PropertyId = property.PropertyId,
          ParameterGuid = property.Revit.ParameterGuid,
          ParameterName = alias.Alias,
          BindingScope = bindingScope,
          Category = legacy.Category.Trim(),
          Carrier = legacy.Carrier,
          PersistenceMode = legacy.PersistenceMode,
          IfcEntity = identity.Entity,
          PropertySet = propertySet,
          IfcProperty = identity.Property,
          IfcDataType = ifcDataType,
          SharedParameterType = legacy.SharedParameterType,
          Unit = legacy.OfficialUnit ?? string.Empty,
          SourceParameterOverride = sourceOverride,
          OfficialSourceParameterName = officialSourceName,
          OfficialSourceParameterGroup =
            legacy.OfficialSourceParameterGroup.Trim(),
          OfficialSourceParameterType = officialSourceParameterType,
          OfficialSourceParameterGuid = OfficialSourceAliasPolicy.CreateGuid(
            bindingScope,
            legacy.Category,
            legacy.Carrier,
            legacy.OfficialSourceParameterGroup,
            officialSourceName,
            officialSourceParameterType),
          LegacyOfficialSourceParameterGuid =
            OfficialSourceAliasPolicy.CreateLegacyGuid(
              bindingScope,
              legacy.Category,
              legacy.Carrier,
              officialSourceName)
        });
      }

      HbrRuleProperty[] officialProperties = database.Package.Properties
        .Where(property => property.OfficialPlugin.InExtracted166)
        .ToArray();
      if (result.Count != officialProperties.Length)
        throw new InvalidDataException(
          "HBR legacy aliases do not cover all official properties: "
          + result.Count
          + "/"
          + officialProperties.Length);
      foreach (HbrRuleProperty property in officialProperties)
      {
        if (!projectedPropertyIds.Contains(property.PropertyId))
          throw new InvalidDataException(
            "HBR official property has no legacy alias: "
            + property.PropertyId);
      }
      return new OfficialHifcMappingCatalog(result);
    }

    public bool TryResolve(string key, out OfficialHifcMapping mapping)
    {
      mapping = null;
      return !string.IsNullOrWhiteSpace(key)
        && _byAlias.TryGetValue(key.Trim(), out mapping);
    }

    public bool TryResolveStage01FieldKey(
      string fieldKey,
      out OfficialHifcMapping mapping)
    {
      mapping = null;
      if (string.IsNullOrWhiteSpace(fieldKey)) return false;

      string[] parts = fieldKey.Split(new[] { '|' }, 3);
      if (parts.Length != 3) return false;

      string ifcEntity = parts[0].Trim();
      string propertySet = parts[1].Trim();
      if (propertySet.StartsWith("Pset_", StringComparison.Ordinal))
        propertySet = propertySet.Substring("Pset_".Length);
      string ifcProperty = parts[2].Trim();

      if (TryResolveStage01Alias(
        ifcEntity,
        propertySet,
        ifcProperty,
        out mapping))
        return true;

      string whitespaceNormalizedProperty = new string(
        ifcProperty.Where(character => !char.IsWhiteSpace(character)).ToArray());
      return !string.Equals(
          whitespaceNormalizedProperty,
          ifcProperty,
          StringComparison.Ordinal)
        && TryResolveStage01Alias(
          ifcEntity,
          propertySet,
          whitespaceNormalizedProperty,
          out mapping);
    }

    private bool TryResolveStage01Alias(
      string ifcEntity,
      string propertySet,
      string ifcProperty,
      out OfficialHifcMapping mapping)
    {
      mapping = null;
      string parameterName = "HIFC." + propertySet + "." + ifcProperty;
      if (!TryResolve(parameterName, out OfficialHifcMapping resolved))
        return false;
      if (!string.Equals(
        resolved.IfcEntity,
        ifcEntity,
        StringComparison.Ordinal))
        return false;
      mapping = resolved;
      return true;
    }

    private void AddAlias(string alias, OfficialHifcMapping mapping)
    {
      if (string.IsNullOrWhiteSpace(alias))
        throw new InvalidDataException("H-IFC mapping contains an empty alias.");
      if (_byAlias.TryGetValue(alias, out OfficialHifcMapping existing)
        && !ReferenceEquals(existing, mapping))
        throw new InvalidDataException("H-IFC mapping alias is duplicated: " + alias);
      _byAlias[alias] = mapping;
    }

    private static LegacyIfcIdentity ParseOriginalIdentity(
      string value,
      string propertyId)
    {
      if (string.IsNullOrWhiteSpace(value))
        throw new InvalidDataException(
          "HBR official property is missing originalIdentity: "
          + propertyId);
      string[] parts = value.Split(new[] { '|' }, 3);
      if (parts.Length != 3
        || string.IsNullOrWhiteSpace(parts[0])
        || string.IsNullOrWhiteSpace(parts[1])
        || string.IsNullOrWhiteSpace(parts[2]))
        throw new InvalidDataException(
          "HBR official originalIdentity is invalid: " + value);
      return new LegacyIfcIdentity(parts[0], parts[1], parts[2]);
    }

    private sealed class LegacyIfcIdentity
    {
      internal LegacyIfcIdentity(
        string entity,
        string propertySet,
        string property)
      {
        Entity = entity;
        PropertySet = propertySet;
        Property = property;
      }

      internal string Entity { get; }
      internal string PropertySet { get; }
      internal string Property { get; }
    }
  }
}
