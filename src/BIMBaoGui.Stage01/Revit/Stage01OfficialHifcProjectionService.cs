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
    private const string SharedParameterResource =
      "BIMBaoGui.Stage01.Resources.GH_HIFC_SharedParameters.txt";
    private const string OrganizationBlockedCode =
      "BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT";

    public static IReadOnlyList<string> WriteAndVerify(Document document, string payloadJson)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      PayloadEnvelope payload = ReadPayload(payloadJson);
      var projected = new List<KeyValuePair<OfficialHifcMapping, string>>();
      var messages = new List<string>();

      foreach (KeyValuePair<string, string> field in payload.values
        .OrderBy(item => item.Key, StringComparer.Ordinal))
      {
        if (string.IsNullOrWhiteSpace(field.Value)) continue;
        if (!field.Key.StartsWith("IfcProject|", StringComparison.Ordinal)) continue;
        if (PlanningTargetCatalog.IsManagedMvdField(field.Key)) continue;

        if (!OfficialHifcMappingCatalog.Instance.TryResolveStage01FieldKey(
          field.Key,
          out OfficialHifcMapping mapping))
        {
          if (OfficialPluginCompatibilityCatalog.Instance.IsStage01ProjectFieldException(field.Key))
          {
            messages.Add("Stage 01 标准字段按登记例外暂不投影：" + field.Key);
            continue;
          }
          throw new InvalidOperationException(
            "Stage 01 标准字段缺少官方规则对应参数映射：" + field.Key);
        }

        if (mapping.EntityPolicy.IsBlocked)
          throw new InvalidOperationException(
            mapping.EntityPolicy.WritePolicy + "：" + mapping.IfcEntity + " / " + mapping.ParameterName);

        projected.Add(new KeyValuePair<OfficialHifcMapping, string>(mapping, field.Value));
      }

      if (payload.organizations.Any(record =>
        record != null && record.Values.Any(value => !string.IsNullOrWhiteSpace(value))))
      {
        messages.Add(
          OrganizationBlockedCode
          + "：IfcOrganization 的官方 Revit 写入/导出协议尚未从官方插件中确认；"
          + "组织数据已保存在 HBR 初始化载荷中，但本版不伪装成 ProjectInformation 参数。" );
      }

      if (projected.Count == 0)
      {
        messages.Add("Stage 01 没有需要投影的非空 IfcProject 标准字段。");
        return messages;
      }

      EnsureBindings(document, projected.Select(item => item.Key), messages);
      Element target = document.ProjectInformation;
      foreach (KeyValuePair<OfficialHifcMapping, string> item in projected)
      {
        Parameter parameter = target.get_Parameter(item.Key.ParameterGuid)
          ?? target.LookupParameter(item.Key.ParameterName);
        if (parameter == null)
          throw new InvalidOperationException("Stage 01 参数未绑定：" + item.Key.ParameterName);
        if (parameter.IsReadOnly)
          throw new InvalidOperationException("Stage 01 参数只读：" + item.Key.ParameterName);
        SetValue(parameter, item.Value);
      }

      document.Regenerate();
      foreach (KeyValuePair<OfficialHifcMapping, string> item in projected)
      {
        Parameter parameter = target.get_Parameter(item.Key.ParameterGuid);
        if (parameter == null || !ReadbackMatches(parameter, item.Value))
          throw new InvalidOperationException(
            "Stage 01 参数回读不一致：" + item.Key.ParameterName);
      }

      messages.Add(
        "REVIT_WRITE_VERIFIED：Stage 01 已写入并回读验证 "
        + projected.Count
        + " 个 IfcProject 候选兼容参数；仍需官方插件导出与检查软件验收。" );
      return messages;
    }

    private static PayloadEnvelope ReadPayload(string payloadJson)
    {
      if (string.IsNullOrWhiteSpace(payloadJson))
        throw new InvalidDataException("Stage 01 初始化载荷为空。");

      var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
      PayloadEnvelope payload = serializer.Deserialize<PayloadEnvelope>(payloadJson);
      if (payload == null) throw new InvalidDataException("Stage 01 初始化载荷无法解析。");
      payload.values = payload.values
        ?? new Dictionary<string, string>(StringComparer.Ordinal);
      payload.organizations = payload.organizations
        ?? new List<Dictionary<string, string>>();
      return payload;
    }

    private static void EnsureBindings(
      Document document,
      IEnumerable<OfficialHifcMapping> mappings,
      ICollection<string> messages)
    {
      Autodesk.Revit.ApplicationServices.Application application = document.Application;
      string previous = application.SharedParametersFilename;
      string temporary = Path.Combine(
        Path.GetTempPath(),
        "BIMBaoGui_GH_HIFC_SharedParameters.txt");
      File.WriteAllText(temporary, ReadEmbeddedText(SharedParameterResource));
      try
      {
        application.SharedParametersFilename = temporary;
        DefinitionFile file = application.OpenSharedParameterFile();
        if (file == null)
          throw new InvalidOperationException("无法打开嵌入的 H-IFC 共享参数文件。");

        foreach (OfficialHifcMapping mapping in mappings
          .GroupBy(item => item.ParameterGuid)
          .Select(group => group.First()))
        {
          ExternalDefinition definition = FindDefinition(file, mapping.ParameterGuid);
          if (definition == null)
            throw new InvalidOperationException("共享参数定义缺失：" + mapping.ParameterName);
          if (!Enum.TryParse(mapping.Category, out BuiltInCategory categoryId))
            throw new InvalidOperationException("不支持的 Revit 类别：" + mapping.Category);
          Category category = Category.GetCategory(document, categoryId);
          if (category == null)
            throw new InvalidOperationException("当前文档不支持类别：" + mapping.Category);

          CategorySet categories = application.Create.NewCategorySet();
          categories.Insert(category);
          Binding binding = mapping.IsTypeBinding
            ? (Binding)application.Create.NewTypeBinding(categories)
            : application.Create.NewInstanceBinding(categories);
          bool inserted = document.ParameterBindings.Insert(
            definition,
            binding,
            BuiltInParameterGroup.PG_DATA);
          if (!inserted)
            document.ParameterBindings.ReInsert(
              definition,
              binding,
              BuiltInParameterGroup.PG_DATA);
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
          if (definition is ExternalDefinition external && external.GUID == guid)
            return external;
      return null;
    }

    private static void SetValue(Parameter parameter, string raw)
    {
      raw = raw ?? string.Empty;
      switch (parameter.StorageType)
      {
        case StorageType.String:
          if (!parameter.Set(raw))
            throw new InvalidOperationException("文本参数写入失败。");
          return;
        case StorageType.Integer:
          if (!parameter.Set(ParseInteger(parameter.Definition.ParameterType, raw)))
            throw new InvalidOperationException("整数参数写入失败。");
          return;
        case StorageType.Double:
          double internalValue = ToInternalValue(
            parameter.Definition.ParameterType,
            ParseDouble(raw));
          if (!parameter.Set(internalValue))
            throw new InvalidOperationException("数值参数写入失败。");
          return;
        default:
          throw new InvalidOperationException(
            "Stage 01 暂不支持参数存储类型：" + parameter.StorageType);
      }
    }

    private static int ParseInteger(ParameterType type, string raw)
    {
      if (type == ParameterType.YesNo)
      {
        string normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized == "1" || normalized == "true" || normalized == "是" || normalized == "yes")
          return 1;
        if (normalized == "0" || normalized == "false" || normalized == "否" || normalized == "no")
          return 0;
        throw new FormatException("布尔值只接受 true/false、是/否、1/0。");
      }
      if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        throw new FormatException("不是有效整数：" + raw);
      return value;
    }

    private static double ParseDouble(string raw)
    {
      if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        throw new FormatException("不是有效数值：" + raw);
      return value;
    }

    private static double ToInternalValue(ParameterType type, double value)
    {
      if (type == ParameterType.Length)
        return UnitUtils.ConvertToInternalUnits(value, DisplayUnitType.DUT_METERS);
      if (type == ParameterType.Area)
        return UnitUtils.ConvertToInternalUnits(value, DisplayUnitType.DUT_SQUARE_METERS);
      if (type == ParameterType.Volume)
        return UnitUtils.ConvertToInternalUnits(value, DisplayUnitType.DUT_CUBIC_METERS);
      if (type == ParameterType.Angle)
        return UnitUtils.ConvertToInternalUnits(value, DisplayUnitType.DUT_DECIMAL_DEGREES);
      return value;
    }

    private static bool ReadbackMatches(Parameter parameter, string expected)
    {
      switch (parameter.StorageType)
      {
        case StorageType.String:
          return string.Equals(
            parameter.AsString() ?? string.Empty,
            expected ?? string.Empty,
            StringComparison.Ordinal);
        case StorageType.Integer:
          return parameter.AsInteger() == ParseInteger(parameter.Definition.ParameterType, expected);
        case StorageType.Double:
          double internalValue = ToInternalValue(
            parameter.Definition.ParameterType,
            ParseDouble(expected));
          return Math.Abs(parameter.AsDouble() - internalValue) <= 1e-8;
        default:
          return false;
      }
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
      public List<Dictionary<string, string>> organizations { get; set; }
    }
  }
}
