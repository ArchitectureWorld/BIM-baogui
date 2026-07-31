using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.Stage01.Hifc;

namespace BIMBaoGui.Stage01.Revit
{
  internal static class OfficialHifcWriteService
  {
    private const string SharedParameterResource = "BIMBaoGui.Stage01.Resources.GH_HIFC_SharedParameters.txt";

    public static bool Enqueue(
      OfficialHifcWriteRequest request,
      Action<OfficialHifcWriteResult> completed,
      out string error)
    {
      return RevitHost.EnqueueAction(uiapp =>
      {
        OfficialHifcWriteResult result;
        try { result = Execute(uiapp, request); }
        catch (Exception exception)
        {
          result = Failure("写入失败：" + exception.Message);
        }
        completed?.Invoke(result);
      }, out error);
    }

    private static OfficialHifcWriteResult Execute(UIApplication uiapp, OfficialHifcWriteRequest request)
    {
      Document document = uiapp.ActiveUIDocument?.Document;
      if (document == null) return Failure("Revit 当前没有活动项目文档。");
      if (uiapp.Application.VersionNumber != "2020") return Failure("本组件仅支持 Revit 2020。");
      if (document.IsFamilyDocument) return Failure("族文件不能执行 H-IFC 属性写入。");
      if (document.IsReadOnly) return Failure("当前文档为只读状态。");
      if (request == null) return Failure("写入请求为空。");
      if (request.PropertyKeys == null || request.PropertyKeys.Count == 0) return Failure("至少需要一个属性。");
      if (request.Values == null || request.Values.Count == 0) return Failure("至少需要一个值。");
      if (request.Values.Count != 1 && request.Values.Count != request.PropertyKeys.Count)
        return Failure("值数量必须为 1，或与属性数量一致。");

      var mappings = new List<OfficialHifcMapping>();
      foreach (string key in request.PropertyKeys)
      {
        if (!OfficialHifcMappingCatalog.Instance.TryResolve(key, out OfficialHifcMapping mapping))
          return Failure("未找到官方 H-IFC 属性映射：" + key);
        mappings.Add(mapping);
      }

      IReadOnlyList<Element> targets = ResolveTargets(document, request.ElementIds);
      if (targets.Count == 0) return Failure("没有可写入的 Revit 对象。");

      var messages = new List<string>();
      int writeCount = 0;
      using (var group = new TransactionGroup(document, "湖北BIM报规｜官方H-IFC属性写入"))
      {
        if (group.Start() != TransactionStatus.Started) return Failure("无法启动 Revit 事务组。");
        try
        {
          using (var transaction = new Transaction(document, "安装并写入 H-IFC 参数"))
          {
            if (transaction.Start() != TransactionStatus.Started)
              throw new InvalidOperationException("无法启动 Revit 事务。");

            EnsureBindings(uiapp.Application, document, mappings, messages);
            foreach (Element sourceTarget in targets)
            {
              for (int index = 0; index < mappings.Count; index++)
              {
                OfficialHifcMapping mapping = mappings[index];
                Element target = mapping.IsTypeBinding ? ResolveTypeTarget(document, sourceTarget) : sourceTarget;
                if (target == null)
                  throw new InvalidOperationException("无法解析类型对象：" + sourceTarget.Id.IntegerValue);

                string rawValue = request.Values.Count == 1 ? request.Values[0] : request.Values[index];
                Parameter parameter = target.get_Parameter(mapping.ParameterGuid) ?? target.LookupParameter(mapping.ParameterName);
                if (parameter == null)
                  throw new InvalidOperationException("参数未绑定到目标对象：" + mapping.ParameterName + " / Id=" + target.Id.IntegerValue);
                if (parameter.IsReadOnly)
                  throw new InvalidOperationException("参数为只读：" + mapping.ParameterName);

                SetValue(parameter, rawValue);
                writeCount++;
              }
            }

            document.Regenerate();
            VerifyReadback(document, targets, mappings, request.Values);
            if (transaction.Commit() != TransactionStatus.Committed)
              throw new InvalidOperationException("Revit 写入事务未成功提交。");
          }

          if (group.Assimilate() != TransactionStatus.Committed)
            return Failure("事务组未能合并为一次可撤销操作。");
        }
        catch (Exception exception)
        {
          try { group.RollBack(); } catch { }
          return Failure("写入或回读失败，已整体回滚：" + exception.Message);
        }
      }

      messages.Add("已写入并回读验证 " + writeCount + " 个 H-IFC 参数值。");
      return new OfficialHifcWriteResult
      {
        Success = true,
        Status = "官方 H-IFC 属性写入通过",
        WriteCount = writeCount,
        Messages = messages
      };
    }

    private static IReadOnlyList<Element> ResolveTargets(Document document, IReadOnlyList<int> ids)
    {
      if (ids == null || ids.Count == 0)
        return new Element[] { document.ProjectInformation };
      var result = new List<Element>();
      foreach (int id in ids.Distinct())
      {
        Element element = document.GetElement(new ElementId(id));
        if (element == null) throw new InvalidOperationException("找不到 Revit 元素 Id=" + id);
        result.Add(element);
      }
      return result;
    }

    private static Element ResolveTypeTarget(Document document, Element element)
    {
      ElementId typeId = element?.GetTypeId();
      return typeId == null || typeId == ElementId.InvalidElementId ? null : document.GetElement(typeId);
    }

    private static void EnsureBindings(
      Autodesk.Revit.ApplicationServices.Application application,
      Document document,
      IEnumerable<OfficialHifcMapping> mappings,
      ICollection<string> messages)
    {
      string previous = application.SharedParametersFilename;
      string temporary = Path.Combine(Path.GetTempPath(), "BIMBaoGui_GH_HIFC_SharedParameters.txt");
      File.WriteAllText(temporary, ReadEmbeddedText(SharedParameterResource));
      try
      {
        application.SharedParametersFilename = temporary;
        DefinitionFile definitionFile = application.OpenSharedParameterFile();
        if (definitionFile == null) throw new InvalidOperationException("无法打开嵌入的 H-IFC 共享参数文件。");

        foreach (OfficialHifcMapping mapping in mappings.Distinct())
        {
          ExternalDefinition definition = FindDefinition(definitionFile, mapping.ParameterGuid);
          if (definition == null)
            throw new InvalidOperationException("共享参数文件中缺少定义：" + mapping.ParameterName);

          if (!Enum.TryParse(mapping.Category, out BuiltInCategory builtInCategory))
            throw new InvalidOperationException("不支持的 Revit 类别：" + mapping.Category);
          Category category = Category.GetCategory(document, builtInCategory);
          if (category == null)
            throw new InvalidOperationException("当前 Revit 文档不支持类别：" + mapping.Category);

          CategorySet categorySet = application.Create.NewCategorySet();
          categorySet.Insert(category);
          Binding binding = mapping.IsTypeBinding
            ? (Binding)application.Create.NewTypeBinding(categorySet)
            : application.Create.NewInstanceBinding(categorySet);

          bool bound = document.ParameterBindings.Insert(definition, binding, BuiltInParameterGroup.PG_DATA);
          if (!bound)
            document.ParameterBindings.ReInsert(definition, binding, BuiltInParameterGroup.PG_DATA);
          messages.Add((bound ? "已安装" : "已校正绑定") + "：" + mapping.ParameterName + " → " + mapping.Category);
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
          if (!parameter.Set(raw)) throw new InvalidOperationException("文本参数写入失败。");
          return;
        case StorageType.Integer:
          int integer = ParseInteger(parameter.Definition.ParameterType, raw);
          if (!parameter.Set(integer)) throw new InvalidOperationException("整数参数写入失败。");
          return;
        case StorageType.Double:
          double number = ParseDouble(raw);
          double internalValue = ToInternalValue(parameter.Definition.ParameterType, number);
          if (!parameter.Set(internalValue)) throw new InvalidOperationException("数值参数写入失败。");
          return;
        default:
          throw new InvalidOperationException("暂不支持参数存储类型：" + parameter.StorageType);
      }
    }

    private static int ParseInteger(ParameterType parameterType, string raw)
    {
      if (parameterType == ParameterType.YesNo)
      {
        string value = raw.Trim().ToLowerInvariant();
        if (value == "1" || value == "true" || value == "是" || value == "yes") return 1;
        if (value == "0" || value == "false" || value == "否" || value == "no") return 0;
        throw new FormatException("布尔值只接受 true/false、是/否、1/0。");
      }
      if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
        throw new FormatException("不是有效整数：" + raw);
      return result;
    }

    private static double ParseDouble(string raw)
    {
      if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
        throw new FormatException("不是有效数值：" + raw);
      return result;
    }

    private static double ToInternalValue(ParameterType type, double value)
    {
      if (type == ParameterType.Length)
        return UnitUtils.ConvertToInternalUnits(value, DisplayUnitType.DUT_METERS);
      if (type == ParameterType.Area)
        return UnitUtils.ConvertToInternalUnits(value, DisplayUnitType.DUT_SQUARE_METERS);
      if (type == ParameterType.Volume)
        return UnitUtils.ConvertToInternalUnits(value, DisplayUnitType.DUT_CUBIC_METERS);
      return value;
    }

    private static void VerifyReadback(
      Document document,
      IReadOnlyList<Element> sourceTargets,
      IReadOnlyList<OfficialHifcMapping> mappings,
      IReadOnlyList<string> values)
    {
      foreach (Element sourceTarget in sourceTargets)
      {
        for (int index = 0; index < mappings.Count; index++)
        {
          OfficialHifcMapping mapping = mappings[index];
          Element target = mapping.IsTypeBinding ? ResolveTypeTarget(document, sourceTarget) : sourceTarget;
          Parameter parameter = target?.get_Parameter(mapping.ParameterGuid);
          if (parameter == null) throw new InvalidOperationException("回读找不到参数：" + mapping.ParameterName);
          string expected = values.Count == 1 ? values[0] : values[index];
          if (!ReadbackMatches(parameter, expected))
            throw new InvalidOperationException("参数回读不一致：" + mapping.ParameterName + "，预期=" + expected + "，实际=" + parameter.AsValueString());
        }
      }
    }

    private static bool ReadbackMatches(Parameter parameter, string expected)
    {
      switch (parameter.StorageType)
      {
        case StorageType.String:
          return string.Equals(parameter.AsString() ?? string.Empty, expected ?? string.Empty, StringComparison.Ordinal);
        case StorageType.Integer:
          return parameter.AsInteger() == ParseInteger(parameter.Definition.ParameterType, expected);
        case StorageType.Double:
          double target = ToInternalValue(parameter.Definition.ParameterType, ParseDouble(expected));
          return Math.Abs(parameter.AsDouble() - target) <= 1e-8;
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

    private static OfficialHifcWriteResult Failure(params string[] messages)
    {
      return new OfficialHifcWriteResult
      {
        Success = false,
        Status = "官方 H-IFC 属性写入失败",
        WriteCount = 0,
        Messages = messages ?? Array.Empty<string>()
      };
    }
  }
}
