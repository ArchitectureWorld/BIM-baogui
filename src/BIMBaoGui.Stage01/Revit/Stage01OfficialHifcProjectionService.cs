using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.Hifc;

namespace BIMBaoGui.Stage01.Revit
{
  internal static class Stage01OfficialHifcProjectionService
  {
    private const string SharedParameterResource = "BIMBaoGui.Stage01.Resources.GH_HIFC_SharedParameters.txt";

    private static readonly IReadOnlyDictionary<string, string> FieldMappings =
      new Dictionary<string, string>(StringComparer.Ordinal)
      {
        [Stage01Keys.ProjectNumber] = "HIFC.申报信息属性集.项目编号",
        [Stage01Keys.ProjectName] = "HIFC.申报信息属性集.项目名称",
        [Stage01Keys.ProjectAddress] = "HIFC.申报信息属性集.项目地址",
        [Stage01Keys.OwnerOrganization] = "HIFC.申报信息属性集.建设单位",
        [Stage01Keys.DesignOrganization] = "HIFC.申报信息属性集.设计单位",
        [Stage01Keys.BaseX] = "HIFC.申报信息属性集.基点坐标X",
        [Stage01Keys.BaseY] = "HIFC.申报信息属性集.基点坐标Y",
        [Stage01Keys.BaseElevation] = "HIFC.申报信息属性集.基点高程",
        [Stage01Keys.CoordinateSystem] = "HIFC.申报信息属性集.坐标系名称",
        [Stage01Keys.ElevationSystem] = "HIFC.申报信息属性集.高程系名称"
      };

    public static IReadOnlyList<string> WriteAndVerify(Document document, string payloadJson)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      IDictionary<string, string> values = ReadValues(payloadJson);
      var projected = new List<KeyValuePair<OfficialHifcMapping, string>>();
      foreach (KeyValuePair<string, string> field in FieldMappings)
      {
        if (!values.TryGetValue(field.Key, out string value) || string.IsNullOrWhiteSpace(value)) continue;
        if (!OfficialHifcMappingCatalog.Instance.TryResolve(field.Value, out OfficialHifcMapping mapping))
          throw new InvalidOperationException("缺少 Stage 01 官方 H-IFC 映射：" + field.Value);
        projected.Add(new KeyValuePair<OfficialHifcMapping, string>(mapping, value));
      }

      if (projected.Count == 0) return Array.Empty<string>();
      var messages = new List<string>();
      EnsureBindings(document, projected.Select(x => x.Key), messages);
      Element target = document.ProjectInformation;
      foreach (KeyValuePair<OfficialHifcMapping, string> item in projected)
      {
        Parameter parameter = target.get_Parameter(item.Key.ParameterGuid) ?? target.LookupParameter(item.Key.ParameterName);
        if (parameter == null) throw new InvalidOperationException("Stage 01 参数未绑定：" + item.Key.ParameterName);
        if (parameter.IsReadOnly) throw new InvalidOperationException("Stage 01 参数只读：" + item.Key.ParameterName);
        SetValue(parameter, item.Value);
      }

      document.Regenerate();
      foreach (KeyValuePair<OfficialHifcMapping, string> item in projected)
      {
        Parameter parameter = target.get_Parameter(item.Key.ParameterGuid);
        if (parameter == null || !ReadbackMatches(parameter, item.Value))
          throw new InvalidOperationException("Stage 01 官方参数回读不一致：" + item.Key.ParameterName);
      }
      messages.Add("Stage 01 已写入并回读验证 " + projected.Count + " 个官方 H-IFC 项目级参数。");
      return messages;
    }

    private static IDictionary<string, string> ReadValues(string payloadJson)
    {
      var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
      PayloadEnvelope payload = serializer.Deserialize<PayloadEnvelope>(payloadJson ?? string.Empty);
      return payload?.values ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static void EnsureBindings(Document document, IEnumerable<OfficialHifcMapping> mappings, ICollection<string> messages)
    {
      Autodesk.Revit.ApplicationServices.Application application = document.Application;
      string previous = application.SharedParametersFilename;
      string temporary = Path.Combine(Path.GetTempPath(), "BIMBaoGui_GH_HIFC_SharedParameters.txt");
      File.WriteAllText(temporary, ReadEmbeddedText(SharedParameterResource));
      try
      {
        application.SharedParametersFilename = temporary;
        DefinitionFile file = application.OpenSharedParameterFile();
        if (file == null) throw new InvalidOperationException("无法打开嵌入的 H-IFC 共享参数文件。");
        foreach (OfficialHifcMapping mapping in mappings.Distinct())
        {
          ExternalDefinition definition = FindDefinition(file, mapping.ParameterGuid);
          if (definition == null) throw new InvalidOperationException("共享参数定义缺失：" + mapping.ParameterName);
          if (!Enum.TryParse(mapping.Category, out BuiltInCategory categoryId))
            throw new InvalidOperationException("不支持的 Revit 类别：" + mapping.Category);
          Category category = Category.GetCategory(document, categoryId);
          if (category == null) throw new InvalidOperationException("当前文档不支持类别：" + mapping.Category);
          CategorySet categories = application.Create.NewCategorySet();
          categories.Insert(category);
          Binding binding = mapping.IsTypeBinding
            ? (Binding)application.Create.NewTypeBinding(categories)
            : application.Create.NewInstanceBinding(categories);
          bool inserted = document.ParameterBindings.Insert(definition, binding, BuiltInParameterGroup.PG_DATA);
          if (!inserted) document.ParameterBindings.ReInsert(definition, binding, BuiltInParameterGroup.PG_DATA);
          messages.Add((inserted ? "已安装" : "已校正绑定") + "：" + mapping.ParameterName);
        }
      }
      finally
      {
        application.SharedParametersFilename = previous;
        try { File.Delete(temporary); } catch { }
      }
    }

    private static ExternalDefinition FindDefinition(DefinitionFile file, Guid guid)
    {
      foreach (DefinitionGroup group in file.Groups)
        foreach (Definition definition in group.Definitions)
          if (definition is ExternalDefinition external && external.GUID == guid) return external;
      return null;
    }

    private static void SetValue(Parameter parameter, string raw)
    {
      raw = raw ?? string.Empty;
      if (parameter.StorageType == StorageType.String)
      {
        if (!parameter.Set(raw)) throw new InvalidOperationException("文本参数写入失败。");
        return;
      }
      if (parameter.StorageType == StorageType.Double)
      {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
          throw new FormatException("不是有效数值：" + raw);
        double internalValue = parameter.Definition.ParameterType == ParameterType.Length
          ? UnitUtils.ConvertToInternalUnits(value, DisplayUnitType.DUT_METERS)
          : value;
        if (!parameter.Set(internalValue)) throw new InvalidOperationException("数值参数写入失败。");
        return;
      }
      throw new InvalidOperationException("Stage 01 暂不支持参数类型：" + parameter.StorageType);
    }

    private static bool ReadbackMatches(Parameter parameter, string expected)
    {
      if (parameter.StorageType == StorageType.String)
        return string.Equals(parameter.AsString() ?? string.Empty, expected ?? string.Empty, StringComparison.Ordinal);
      if (parameter.StorageType == StorageType.Double)
      {
        if (!double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return false;
        double internalValue = parameter.Definition.ParameterType == ParameterType.Length
          ? UnitUtils.ConvertToInternalUnits(value, DisplayUnitType.DUT_METERS)
          : value;
        return Math.Abs(parameter.AsDouble() - internalValue) <= 1e-8;
      }
      return false;
    }

    private static string ReadEmbeddedText(string name)
    {
      using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
      {
        if (stream == null) throw new InvalidDataException("缺少嵌入资源：" + name);
        using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
      }
    }

    private sealed class PayloadEnvelope
    {
      public Dictionary<string, string> values { get; set; }
    }
  }
}
