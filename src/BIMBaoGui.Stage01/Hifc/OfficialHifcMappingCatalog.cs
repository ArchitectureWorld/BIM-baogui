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
    private const string ResourceName = "BIMBaoGui.Stage01.Resources.GH_HIFC_ParameterBindings.json";
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

    public static OfficialHifcMappingCatalog Instance { get; } = Load();

    public IReadOnlyCollection<OfficialHifcMapping> Mappings => _byAlias.Values.Distinct().ToArray();

    public bool TryResolve(string key, out OfficialHifcMapping mapping)
    {
      mapping = null;
      return !string.IsNullOrWhiteSpace(key) && _byAlias.TryGetValue(key.Trim(), out mapping);
    }

    private void AddAlias(string alias, OfficialHifcMapping mapping)
    {
      if (string.IsNullOrWhiteSpace(alias))
        throw new InvalidDataException("H-IFC 映射存在空别名。");
      if (_byAlias.TryGetValue(alias, out OfficialHifcMapping existing) && !ReferenceEquals(existing, mapping))
        throw new InvalidDataException("H-IFC 映射别名重复：" + alias);
      _byAlias[alias] = mapping;
    }

    private static OfficialHifcMappingCatalog Load()
    {
      Assembly assembly = typeof(OfficialHifcMappingCatalog).Assembly;
      using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
      {
        if (stream == null) throw new InvalidDataException("缺少嵌入资源：" + ResourceName);
        using (var reader = new StreamReader(stream))
        {
          var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
          BindingEnvelope envelope = serializer.Deserialize<BindingEnvelope>(reader.ReadToEnd());
          if (envelope?.bindings == null || envelope.bindings.Length == 0)
            throw new InvalidDataException("官方 H-IFC 参数映射为空。");

          var result = new List<OfficialHifcMapping>();
          foreach (BindingRecord item in envelope.bindings)
          {
            if (!Guid.TryParse(item.parameterGuid, out Guid guid))
              throw new InvalidDataException("无效参数 GUID：" + item.parameterGuid);
            if (string.IsNullOrWhiteSpace(item.parameterName) || string.IsNullOrWhiteSpace(item.category))
              throw new InvalidDataException("H-IFC 映射缺少参数名或类别：" + item.propertyId);
            result.Add(new OfficialHifcMapping
            {
              PropertyId = item.propertyId ?? string.Empty,
              ParameterGuid = guid,
              ParameterName = item.parameterName,
              BindingScope = item.bindingScope ?? "INSTANCE",
              Category = item.category,
              Carrier = item.carrier ?? string.Empty
            });
          }
          return new OfficialHifcMappingCatalog(result);
        }
      }
    }

    private sealed class BindingEnvelope { public BindingRecord[] bindings { get; set; } }
    private sealed class BindingRecord
    {
      public string propertyId { get; set; }
      public string parameterGuid { get; set; }
      public string parameterName { get; set; }
      public string bindingScope { get; set; }
      public string category { get; set; }
      public string carrier { get; set; }
    }
  }
}
