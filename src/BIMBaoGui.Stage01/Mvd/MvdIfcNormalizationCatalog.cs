using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Hifc;

namespace BIMBaoGui.Stage01.Mvd
{
  internal sealed class MvdIfcNormalizationCatalog
  {
    private const string RegistryResourceName =
      "BIMBaoGui.Stage01.Resources.stage01_file_initialization_registry_v0.1.json";
    private static readonly Lazy<MvdIfcNormalizationCatalog> LazyInstance =
      new Lazy<MvdIfcNormalizationCatalog>(Load);

    private readonly Dictionary<string, MvdIfcNormalizationRule> _byAlias;

    private MvdIfcNormalizationCatalog(
      IReadOnlyCollection<MvdIfcNormalizationRule> rules)
    {
      Rules = rules;
      _byAlias = new Dictionary<string, MvdIfcNormalizationRule>(
        StringComparer.OrdinalIgnoreCase);
      foreach (MvdIfcNormalizationRule rule in rules)
      {
        foreach (string propertySet in rule.PropertySetAliases)
        foreach (string property in rule.PropertyAliases)
          AddAlias(rule.Entity, propertySet, property, rule);
      }
    }

    public static MvdIfcNormalizationCatalog Instance => LazyInstance.Value;

    public IReadOnlyCollection<MvdIfcNormalizationRule> Rules { get; }

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
          "MVD IFC 规范化别名冲突："
          + entity
          + "|"
          + propertySet
          + "|"
          + property);
      _byAlias[key] = rule;
    }

    private static MvdIfcNormalizationCatalog Load()
    {
      var serializer = new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 512
      };
      RegistryEnvelope envelope = serializer.Deserialize<RegistryEnvelope>(
        ReadEmbeddedText(RegistryResourceName));
      if (envelope?.mvd_fields == null || envelope.mvd_fields.Count == 0)
        throw new InvalidDataException("MVD 字段注册表为空。");

      var rules = new List<MvdIfcNormalizationRule>();
      foreach (MvdFieldRecord source in envelope.mvd_fields)
      {
        if (source == null
          || string.IsNullOrWhiteSpace(source.field_key)
          || string.IsNullOrWhiteSpace(source.entity)
          || string.IsNullOrWhiteSpace(source.pset)
          || string.IsNullOrWhiteSpace(source.property)
          || string.IsNullOrWhiteSpace(source.declared_ifc_type))
          throw new InvalidDataException("MVD 字段注册表包含不完整记录。");

        OfficialHifcMapping mapping = null;
        OfficialHifcMappingCatalog.Instance.TryResolveStage01FieldKey(
          source.field_key,
          out mapping);
        string canonicalProperty = string.IsNullOrWhiteSpace(mapping?.IfcProperty)
          ? source.property.Trim()
          : mapping.IfcProperty.Trim();
        string canonicalPropertySet = source.pset.Trim();
        string propertySetWithoutPrefix = canonicalPropertySet.StartsWith(
          "Pset_",
          StringComparison.OrdinalIgnoreCase)
          ? canonicalPropertySet.Substring("Pset_".Length)
          : canonicalPropertySet;

        string[] propertySetAliases = DistinctNonEmpty(
          canonicalPropertySet,
          propertySetWithoutPrefix,
          mapping?.PropertySet);
        string[] propertyAliases = DistinctNonEmpty(
          canonicalProperty,
          source.property,
          RemoveWhitespace(source.property),
          mapping?.IfcProperty);
        string[] internalAliases = DistinctNonEmpty(
          mapping?.ParameterName,
          "HIFC." + propertySetWithoutPrefix + "." + canonicalProperty);

        rules.Add(new MvdIfcNormalizationRule
        {
          Entity = source.entity.Trim(),
          CanonicalPropertySet = canonicalPropertySet,
          PropertySetAliases = propertySetAliases,
          CanonicalProperty = canonicalProperty,
          PropertyAliases = propertyAliases,
          TargetType = NormalizeIfcType(source.declared_ifc_type),
          Unit = mapping?.Unit?.Trim() ?? string.Empty,
          InternalAliases = internalAliases
        });
      }

      Dictionary<string, MvdIfcNormalizationRule> rulesByIdentity = rules
        .ToDictionary(
          rule => CreateKey(
            rule.Entity,
            rule.CanonicalPropertySet,
            rule.CanonicalProperty),
          rule => rule,
          StringComparer.OrdinalIgnoreCase);
      foreach (OfficialHifcMapping mapping in
        OfficialHifcMappingCatalog.Instance.Mappings)
      {
        if (mapping == null
          || string.IsNullOrWhiteSpace(mapping.IfcEntity)
          || string.IsNullOrWhiteSpace(mapping.PropertySet)
          || string.IsNullOrWhiteSpace(mapping.IfcProperty)
          || string.IsNullOrWhiteSpace(mapping.IfcDataType))
          throw new InvalidDataException("官方 H-IFC 映射包含不完整记录。");

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
              "MVD 与官方 H-IFC 映射类型或单位冲突：" + identity);
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

    private static string NormalizeIfcType(string value)
    {
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
      return entity.Trim() + "\u001f" + propertySet.Trim() + "\u001f" + property.Trim();
    }

    private static string ReadEmbeddedText(string resourceName)
    {
      Assembly assembly = typeof(MvdIfcNormalizationCatalog).Assembly;
      using (Stream stream = assembly.GetManifestResourceStream(resourceName))
      {
        if (stream == null)
          throw new InvalidDataException("缺少嵌入资源：" + resourceName);
        using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
      }
    }

    private sealed class RegistryEnvelope
    {
      public List<MvdFieldRecord> mvd_fields { get; set; }
    }

    private sealed class MvdFieldRecord
    {
      public string field_key { get; set; }
      public string property { get; set; }
      public string declared_ifc_type { get; set; }
      public string entity { get; set; }
      public string pset { get; set; }
    }
  }
}
