using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace BIMBaoGui.Stage01.Hifc
{
  internal sealed class OfficialHifcMappingCatalog
  {
    private static readonly object InstanceLock = new object();
    private static OfficialHifcMappingCatalog _instance;

    private const string BindingResourceName =
      "BIMBaoGui.Stage01.Resources.GH_HIFC_ParameterBindings.json";
    private const string RuleResourceName =
      "BIMBaoGui.Stage01.Resources.wuhan_planning_rules.v1.json";

    private readonly Dictionary<string, OfficialHifcMapping> _byAlias;

    private OfficialHifcMappingCatalog(IEnumerable<OfficialHifcMapping> mappings)
    {
      _byAlias = new Dictionary<string, OfficialHifcMapping>(StringComparer.OrdinalIgnoreCase);
      foreach (OfficialHifcMapping mapping in mappings)
      {
        AddAlias(mapping.PropertyId, mapping);
        AddAlias(mapping.ParameterGuid.ToString("D"), mapping);
        AddAlias(mapping.ParameterName, mapping);
      }
    }

    public static OfficialHifcMappingCatalog Instance
    {
      get
      {
        if (_instance != null) return _instance;
        lock (InstanceLock)
        {
          if (_instance == null) _instance = Load();
          return _instance;
        }
      }
    }

    public IReadOnlyCollection<OfficialHifcMapping> Mappings =>
      _byAlias.Values.Distinct().ToArray();

    public bool TryResolve(string key, out OfficialHifcMapping mapping)
    {
      mapping = null;
      return !string.IsNullOrWhiteSpace(key)
        && _byAlias.TryGetValue(key.Trim(), out mapping);
    }

    public bool TryResolveStage01FieldKey(string fieldKey, out OfficialHifcMapping mapping)
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
      if (!TryResolve(parameterName, out OfficialHifcMapping resolved)) return false;
      if (!string.Equals(resolved.IfcEntity, ifcEntity, StringComparison.Ordinal)) return false;
      mapping = resolved;
      return true;
    }

    private void AddAlias(string alias, OfficialHifcMapping mapping)
    {
      if (string.IsNullOrWhiteSpace(alias))
        throw new InvalidDataException("H-IFC 映射存在空别名。");
      if (_byAlias.TryGetValue(alias, out OfficialHifcMapping existing)
        && !ReferenceEquals(existing, mapping))
        throw new InvalidDataException("H-IFC 映射别名重复：" + alias);
      _byAlias[alias] = mapping;
    }

    private static OfficialHifcMappingCatalog Load()
    {
      var serializer = new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 512
      };
      BindingEnvelope bindingEnvelope = serializer.Deserialize<BindingEnvelope>(
        ReadEmbeddedText(BindingResourceName));
      RuleEnvelope ruleEnvelope = serializer.Deserialize<RuleEnvelope>(
        ReadEmbeddedText(RuleResourceName));

      if (bindingEnvelope?.bindings == null || bindingEnvelope.bindings.Length == 0)
        throw new InvalidDataException("H-IFC 参数绑定为空。");
      if (ruleEnvelope?.properties == null || ruleEnvelope.properties.Length == 0)
        throw new InvalidDataException("H-IFC 规则包为空。");

      Dictionary<string, RuleRecord> rules = ruleEnvelope.properties
        .Where(item => item != null && !string.IsNullOrWhiteSpace(item.propertyId))
        .ToDictionary(item => item.propertyId, item => item, StringComparer.OrdinalIgnoreCase);

      var result = new List<OfficialHifcMapping>();
      foreach (BindingRecord item in bindingEnvelope.bindings)
      {
        if (item == null)
          throw new InvalidDataException("H-IFC 参数绑定包含空记录。");
        if (!Guid.TryParse(item.parameterGuid, out Guid guid))
          throw new InvalidDataException("无效参数 GUID：" + item.parameterGuid);
        if (string.IsNullOrWhiteSpace(item.propertyId)
          || string.IsNullOrWhiteSpace(item.parameterName)
          || string.IsNullOrWhiteSpace(item.officialSourceParameterGroup))
          throw new InvalidDataException(
            "H-IFC 映射缺少 propertyId、参数名或官方源参数组。");
        if (!rules.TryGetValue(item.propertyId, out RuleRecord rule)
          || rule.official == null || rule.canonical == null)
          throw new InvalidDataException("参数绑定找不到对应规则：" + item.propertyId);

        string ifcEntity = rule.official.ifcEntity ?? string.Empty;
        string propertySet = rule.official.propertySet ?? string.Empty;
        string ifcProperty = rule.official.ifcProperty ?? string.Empty;
        string sourceOverride = rule.official.sourceParameterOverride ?? string.Empty;
        string officialSourceName = string.IsNullOrWhiteSpace(sourceOverride)
          ? ifcProperty.Trim()
          : sourceOverride.Trim();
        string bindingScope = string.IsNullOrWhiteSpace(item.bindingScope)
          ? "INSTANCE"
          : item.bindingScope.Trim();
        string officialSourceParameterType =
          OfficialSourceParameterTypePolicy.Resolve(
            rule.official.ifcDataType);
        result.Add(new OfficialHifcMapping
        {
          PropertyId = item.propertyId,
          ParameterGuid = guid,
          ParameterName = item.parameterName,
          BindingScope = bindingScope,
          Category = (item.category ?? string.Empty).Trim(),
          Carrier = item.carrier ?? string.Empty,
          PersistenceMode = item.persistenceMode ?? string.Empty,
          IfcEntity = ifcEntity,
          PropertySet = propertySet,
          IfcProperty = ifcProperty,
          IfcDataType = rule.official.ifcDataType ?? string.Empty,
          SharedParameterType = rule.canonical.sharedParameterType ?? string.Empty,
          Unit = rule.official.unit ?? string.Empty,
          SourceParameterOverride = sourceOverride,
          OfficialSourceParameterName = officialSourceName,
          OfficialSourceParameterGroup =
            (item.officialSourceParameterGroup ?? string.Empty).Trim(),
          OfficialSourceParameterType = officialSourceParameterType,
          OfficialSourceParameterGuid = OfficialSourceAliasPolicy.CreateGuid(
            bindingScope,
            item.category,
            item.carrier,
            item.officialSourceParameterGroup,
            officialSourceName,
            officialSourceParameterType),
          LegacyOfficialSourceParameterGuid =
            OfficialSourceAliasPolicy.CreateLegacyGuid(
              bindingScope,
              item.category,
              item.carrier,
              officialSourceName)
        });
      }

      return new OfficialHifcMappingCatalog(result);
    }

    private static string ReadEmbeddedText(string name)
    {
      Assembly assembly = typeof(OfficialHifcMappingCatalog).Assembly;
      using (Stream stream = assembly.GetManifestResourceStream(name))
      {
        if (stream == null)
        {
          string available = string.Join(", ", assembly.GetManifestResourceNames());
          throw new InvalidDataException(
            "缺少嵌入资源：" + name + "。当前资源：" + available);
        }
        using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
      }
    }

    private sealed class BindingEnvelope
    {
      public BindingRecord[] bindings { get; set; }
    }

    private sealed class BindingRecord
    {
      public string propertyId { get; set; }
      public string parameterGuid { get; set; }
      public string parameterName { get; set; }
      public string bindingScope { get; set; }
      public string category { get; set; }
      public string carrier { get; set; }
      public string persistenceMode { get; set; }
      public string officialSourceParameterGroup { get; set; }
    }

    private sealed class RuleEnvelope
    {
      public RuleRecord[] properties { get; set; }
    }

    private sealed class RuleRecord
    {
      public string propertyId { get; set; }
      public OfficialRuleRecord official { get; set; }
      public CanonicalRuleRecord canonical { get; set; }
    }

    private sealed class OfficialRuleRecord
    {
      public string ifcEntity { get; set; }
      public string propertySet { get; set; }
      public string ifcProperty { get; set; }
      public string ifcDataType { get; set; }
      public string unit { get; set; }
      public string sourceParameterOverride { get; set; }
    }

    private sealed class CanonicalRuleRecord
    {
      public string sharedParameterType { get; set; }
    }
  }
}
