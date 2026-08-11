using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIMBaoGui.Stage01.Hifc;
using BIMBaoGui.Stage01.Rules;

namespace BIMBaoGui.Stage01.Mvd
{
  internal sealed class MvdIfcNormalizationCatalog
  {
    private static readonly Lazy<MvdIfcNormalizationCatalog> LazyInstance =
      new Lazy<MvdIfcNormalizationCatalog>(() =>
        FromDatabase(HbrRuleDatabase.Current));

    private readonly Dictionary<string, MvdIfcNormalizationRule> _byAlias;

    private MvdIfcNormalizationCatalog(
      IReadOnlyCollection<MvdIfcNormalizationRule> rules)
    {
      Rules = rules ?? throw new ArgumentNullException(nameof(rules));
      _byAlias = new Dictionary<string, MvdIfcNormalizationRule>(
        StringComparer.OrdinalIgnoreCase);
      foreach (MvdIfcNormalizationRule rule in rules)
      {
        if (rule == null)
          throw new InvalidDataException(
            "MVD IFC normalization rules contain a null record.");
        foreach (string propertySet in rule.PropertySetAliases)
        foreach (string property in rule.PropertyAliases)
          AddAlias(rule.Entity, propertySet, property, rule);
      }
    }

    public static MvdIfcNormalizationCatalog Instance => LazyInstance.Value;

    public IReadOnlyCollection<MvdIfcNormalizationRule> Rules { get; }

    internal static MvdIfcNormalizationCatalog FromDatabase(
      HbrRuleDatabase database)
    {
      if (database == null) throw new ArgumentNullException(nameof(database));
      OfficialHifcMappingCatalog official =
        OfficialHifcMappingCatalog.FromDatabase(database);
      var rules = new List<MvdIfcNormalizationRule>();

      foreach (HbrStage01FieldRef source in database.Package.Stage01.FieldRefs)
      {
        if (!database.PropertiesById.TryGetValue(
          source.PropertyId,
          out HbrRuleProperty property))
          throw new InvalidDataException(
            "MVD Stage01 field references unknown propertyId: "
            + source.PropertyId);
        LegacyFieldIdentity sourceIdentity = ParseFieldKey(source.FieldKey);
        official.TryResolveStage01FieldKey(
          source.FieldKey,
          out OfficialHifcMapping mapping);
        string canonicalProperty = string.IsNullOrWhiteSpace(mapping?.IfcProperty)
          ? sourceIdentity.Property.Trim()
          : mapping.IfcProperty.Trim();
        string canonicalPropertySet = sourceIdentity.PropertySet.Trim();
        string propertySetWithoutPrefix = canonicalPropertySet.StartsWith(
          "Pset_",
          StringComparison.OrdinalIgnoreCase)
          ? canonicalPropertySet.Substring("Pset_".Length)
          : canonicalPropertySet;

        rules.Add(new MvdIfcNormalizationRule
        {
          Entity = sourceIdentity.Entity.Trim(),
          CanonicalPropertySet = canonicalPropertySet,
          PropertySetAliases = DistinctNonEmpty(
            canonicalPropertySet,
            propertySetWithoutPrefix,
            mapping?.PropertySet),
          CanonicalProperty = canonicalProperty,
          PropertyAliases = DistinctNonEmpty(
            canonicalProperty,
            sourceIdentity.Property,
            RemoveWhitespace(sourceIdentity.Property),
            mapping?.IfcProperty,
            property.Source.RawProperty),
          TargetType = NormalizeIfcType(property.Ifc.DeclaredType),
          Unit = mapping?.Unit?.Trim() ?? string.Empty,
          InternalAliases = DistinctNonEmpty(
            mapping?.ParameterName,
            "HIFC." + propertySetWithoutPrefix + "." + canonicalProperty)
        });
      }

      var rulesByIdentity = new Dictionary<string, MvdIfcNormalizationRule>(
        StringComparer.OrdinalIgnoreCase);
      foreach (MvdIfcNormalizationRule rule in rules)
      {
        string identity = CreateKey(
          rule.Entity,
          rule.CanonicalPropertySet,
          rule.CanonicalProperty);
        if (rulesByIdentity.ContainsKey(identity))
          throw new InvalidDataException(
            "MVD Stage01 identity is duplicated: " + identity);
        rulesByIdentity.Add(identity, rule);
      }

      foreach (OfficialHifcMapping mapping in official.Mappings)
      {
        if (mapping == null
          || string.IsNullOrWhiteSpace(mapping.IfcEntity)
          || string.IsNullOrWhiteSpace(mapping.PropertySet)
          || string.IsNullOrWhiteSpace(mapping.IfcProperty)
          || string.IsNullOrWhiteSpace(mapping.IfcDataType))
          throw new InvalidDataException(
            "Official H-IFC mapping contains an incomplete record.");

        string propertySet = mapping.PropertySet.Trim();
        string canonicalPropertySet = propertySet.StartsWith(
          "Pset_",
          StringComparison.OrdinalIgnoreCase)
          ? propertySet
          : "Pset_" + propertySet;
        string canonicalProperty = mapping.IfcProperty.Trim();
        string identity = CreateKey(
          mapping.IfcEntity,
          canonicalPropertySet,
          canonicalProperty);
        if (rulesByIdentity.TryGetValue(
          identity,
          out MvdIfcNormalizationRule existing))
        {
          string officialType = NormalizeIfcType(mapping.IfcDataType);
          string officialUnit = mapping.Unit?.Trim() ?? string.Empty;
          if (!string.Equals(
            existing.TargetType,
            officialType,
            StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
              existing.Unit ?? string.Empty,
              officialUnit,
              StringComparison.Ordinal))
            throw new InvalidDataException(
              "MVD and official H-IFC type or unit conflict: " + identity);
          existing.PropertySetAliases = MergeDistinct(
            existing.PropertySetAliases,
            canonicalPropertySet,
            propertySet);
          existing.PropertyAliases = MergeDistinct(
            existing.PropertyAliases,
            canonicalProperty,
            RemoveWhitespace(canonicalProperty));
          existing.InternalAliases = MergeDistinct(
            existing.InternalAliases,
            mapping.ParameterName,
            "HIFC." + propertySet + "." + canonicalProperty);
          continue;
        }

        var rule = new MvdIfcNormalizationRule
        {
          Entity = mapping.IfcEntity.Trim(),
          CanonicalPropertySet = canonicalPropertySet,
          PropertySetAliases = DistinctNonEmpty(
            canonicalPropertySet,
            propertySet),
          CanonicalProperty = canonicalProperty,
          PropertyAliases = DistinctNonEmpty(
            canonicalProperty,
            RemoveWhitespace(canonicalProperty)),
          TargetType = NormalizeIfcType(mapping.IfcDataType),
          Unit = mapping.Unit?.Trim() ?? string.Empty,
          InternalAliases = DistinctNonEmpty(
            mapping.ParameterName,
            "HIFC." + propertySet + "." + canonicalProperty)
        };
        rules.Add(rule);
        rulesByIdentity.Add(identity, rule);
      }

      return new MvdIfcNormalizationCatalog(rules);
    }

    public bool TryResolve(
      string entity,
      string propertySet,
      string property,
      out MvdIfcNormalizationRule rule)
    {
      rule = null;
      if (string.IsNullOrWhiteSpace(entity)
        || string.IsNullOrWhiteSpace(propertySet)
        || string.IsNullOrWhiteSpace(property))
        return false;
      return _byAlias.TryGetValue(
        CreateKey(entity, propertySet, property),
        out rule);
    }

    private void AddAlias(
      string entity,
      string propertySet,
      string property,
      MvdIfcNormalizationRule rule)
    {
      string key = CreateKey(entity, propertySet, property);
      if (_byAlias.TryGetValue(key, out MvdIfcNormalizationRule existing)
        && !ReferenceEquals(existing, rule))
        throw new InvalidDataException(
          "MVD IFC normalization alias conflict: "
          + entity
          + "|"
          + propertySet
          + "|"
          + property);
      _byAlias[key] = rule;
    }

    private static LegacyFieldIdentity ParseFieldKey(string fieldKey)
    {
      if (string.IsNullOrWhiteSpace(fieldKey))
        throw new InvalidDataException("MVD Stage01 fieldKey is empty.");
      string[] parts = fieldKey.Split(new[] { '|' }, 3);
      if (parts.Length != 3
        || string.IsNullOrWhiteSpace(parts[0])
        || string.IsNullOrWhiteSpace(parts[1])
        || string.IsNullOrWhiteSpace(parts[2]))
        throw new InvalidDataException(
          "MVD Stage01 fieldKey is invalid: " + fieldKey);
      return new LegacyFieldIdentity(parts[0], parts[1], parts[2]);
    }

    private static string NormalizeIfcType(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
        throw new InvalidDataException("MVD IFC type is empty.");
      string trimmed = value.Trim();
      if (trimmed.StartsWith("Ifc", StringComparison.Ordinal)) return trimmed;
      if (trimmed.StartsWith("IFC", StringComparison.OrdinalIgnoreCase))
        return "Ifc" + trimmed.Substring(3);
      return trimmed;
    }

    private static string RemoveWhitespace(string value)
    {
      return new string((value ?? string.Empty)
        .Where(character => !char.IsWhiteSpace(character))
        .ToArray());
    }

    private static string[] DistinctNonEmpty(params string[] values)
    {
      return values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.Ordinal)
        .ToArray();
    }

    private static string[] MergeDistinct(
      IReadOnlyCollection<string> existing,
      params string[] values)
    {
      return DistinctNonEmpty((existing ?? Array.Empty<string>())
        .Concat(values ?? Array.Empty<string>())
        .ToArray());
    }

    private static string CreateKey(
      string entity,
      string propertySet,
      string property)
    {
      return entity.Trim()
        + "\u001f"
        + propertySet.Trim()
        + "\u001f"
        + property.Trim();
    }

    private sealed class LegacyFieldIdentity
    {
      internal LegacyFieldIdentity(
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
