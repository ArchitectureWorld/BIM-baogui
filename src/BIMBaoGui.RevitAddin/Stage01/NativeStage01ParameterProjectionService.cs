using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal static class NativeStage01ParameterProjectionService
  {
    private const double DoubleTolerance = 1e-9;

    internal static IReadOnlyList<string> WriteAndVerify(
      Document document,
      NativeStage01Model model,
      NativeRuleCatalog catalog)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (model == null) throw new ArgumentNullException(nameof(model));
      if (catalog == null) throw new ArgumentNullException(nameof(catalog));

      NativeStage01FieldDefinition[] fields = catalog.Stage01Fields
        .Where(field => field.WriteInStage01)
        .Where(field => !field.ReadOnly)
        .Where(field => field.ParameterGuid.HasValue)
        .Where(field => string.Equals(
          field.IfcEntity,
          "IfcProject",
          StringComparison.Ordinal))
        .Where(field => !string.IsNullOrWhiteSpace(
          GetProjectionValue(model, field)))
        .OrderBy(field => field.ParameterGuid.Value)
        .ToArray();
      if (fields.Length == 0) return Array.Empty<string>();

      EnsureBindings(document, fields);
      ProjectInfo target = document.ProjectInformation
        ?? throw new InvalidOperationException("当前文档缺少 ProjectInformation。");
      foreach (NativeStage01FieldDefinition field in fields)
        WriteValue(target, field, GetProjectionValue(model, field));

      IReadOnlyList<string> errors = Verify(document, model, catalog);
      if (errors.Count > 0)
      {
        throw new InvalidOperationException(
          "Stage01 项目参数回读失败：" + string.Join(" ", errors));
      }
      return new[]
      {
        "已按 HBR 数据库安装并回读 "
        + fields.Length.ToString(CultureInfo.InvariantCulture)
        + " 个 IfcProject 固定 GUID 参数。"
      };
    }

    internal static IReadOnlyList<string> Verify(
      Document document,
      NativeStage01Model model,
      NativeRuleCatalog catalog)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (model == null) throw new ArgumentNullException(nameof(model));
      if (catalog == null) throw new ArgumentNullException(nameof(catalog));
      ProjectInfo target = document.ProjectInformation;
      var errors = new List<string>();
      if (target == null)
      {
        errors.Add("当前文档缺少 ProjectInformation。" );
        return errors;
      }

      foreach (NativeStage01FieldDefinition field in catalog.Stage01Fields
        .Where(field => field.WriteInStage01)
        .Where(field => !field.ReadOnly)
        .Where(field => field.ParameterGuid.HasValue)
        .Where(field => string.Equals(
          field.IfcEntity,
          "IfcProject",
          StringComparison.Ordinal))
        .OrderBy(field => field.ParameterGuid.Value))
      {
        string expected = GetProjectionValue(model, field);
        if (string.IsNullOrWhiteSpace(expected)) continue;
        Parameter parameter = target.get_Parameter(field.ParameterGuid.Value);
        if (parameter == null)
        {
          errors.Add(field.Label + "：固定 GUID 参数不存在。" );
          continue;
        }
        try
        {
          if (!ValueMatches(parameter, field, expected))
            errors.Add(field.Label + "：固定 GUID 参数回读值不一致。" );
        }
        catch (Exception exception)
        {
          errors.Add(field.Label + "：" + exception.Message);
        }
      }
      return errors;
    }

    private static string GetProjectionValue(
      NativeStage01Model model,
      NativeStage01FieldDefinition field)
    {
      if (NativeStage01FieldPresentationPolicy.IsPlanningTarget(field))
      {
        NativePlanningTargetValue target;
        if (model.PlanningTargets.TryGetValue(field.PropertyId, out target)
          && target != null)
        {
          return target.Value1 ?? string.Empty;
        }
        return string.Empty;
      }
      return model.GetValue(field.FieldKey);
    }

    private static void EnsureBindings(
      Document document,
      IReadOnlyList<NativeStage01FieldDefinition> fields)
    {
      Autodesk.Revit.ApplicationServices.Application application =
        document.Application;
      string originalSharedParameterPath = application.SharedParametersFilename;
      string temporaryPath = Path.Combine(
        Path.GetTempPath(),
        "BIMBaoGui_Native_Stage01_"
          + Guid.NewGuid().ToString("N")
          + ".txt");
      Exception primaryFailure = null;
      try
      {
        NativeStage01SharedParameterFile.Write(temporaryPath, fields);
        application.SharedParametersFilename = temporaryPath;
        DefinitionFile definitionFile = application.OpenSharedParameterFile();
        if (definitionFile == null)
          throw new InvalidOperationException(
            "Revit 无法打开原生插件生成的临时共享参数文件。" );
        foreach (NativeStage01FieldDefinition field in fields)
          EnsureOneBinding(document, application, definitionFile, field);
      }
      catch (Exception exception)
      {
        primaryFailure = exception;
        throw;
      }
      finally
      {
        Exception cleanupFailure = null;
        try
        {
          application.SharedParametersFilename = originalSharedParameterPath;
        }
        catch (Exception exception)
        {
          cleanupFailure = exception;
        }
        try
        {
          if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        catch (Exception exception)
        {
          if (cleanupFailure == null) cleanupFailure = exception;
        }
        if (primaryFailure == null && cleanupFailure != null)
          throw new IOException(
            "Stage01 临时共享参数环境清理失败。",
            cleanupFailure);
      }
    }

    private static void EnsureOneBinding(
      Document document,
      Autodesk.Revit.ApplicationServices.Application application,
      DefinitionFile definitionFile,
      NativeStage01FieldDefinition field)
    {
      Guid parameterGuid = field.ParameterGuid
        ?? throw new InvalidDataException("Stage01 参数缺少固定 GUID。" );
      SharedParameterElement shared = SharedParameterElement.Lookup(
        document,
        parameterGuid);
      InternalDefinition internalDefinition = shared?.GetDefinition();
      if (internalDefinition != null
        && !string.Equals(
          internalDefinition.Name,
          field.ParameterName,
          StringComparison.Ordinal))
      {
        throw new InvalidDataException(
          "同 GUID 共享参数名称冲突：" + field.ParameterName);
      }
      if (internalDefinition != null
        && !string.Equals(
          internalDefinition.ParameterType.ToString(),
          field.ParameterType,
          StringComparison.OrdinalIgnoreCase))
      {
        throw new InvalidDataException(
          "同 GUID 共享参数类型冲突：" + field.ParameterName);
      }

      ElementBinding existing = internalDefinition == null
        ? null
        : FindBinding(document.ParameterBindings, internalDefinition);
      if (existing != null && !(existing is InstanceBinding))
        throw new InvalidDataException(
          "Stage01 项目参数必须使用 InstanceBinding：" + field.ParameterName);

      Category category = Category.GetCategory(
        document,
        BuiltInCategory.OST_ProjectInformation)
        ?? throw new InvalidOperationException(
          "当前文档不支持 OST_ProjectInformation 类别绑定。" );
      CategorySet union = application.Create.NewCategorySet();
      bool hasProjectInformation = false;
      if (existing != null)
      {
        foreach (Category current in existing.Categories)
        {
          union.Insert(current);
          if (current.Id.IntegerValue == category.Id.IntegerValue)
            hasProjectInformation = true;
        }
      }
      if (!hasProjectInformation) union.Insert(category);
      if (existing != null && hasProjectInformation) return;

      Binding binding = application.Create.NewInstanceBinding(union);
      if (existing != null)
      {
        if (!document.ParameterBindings.ReInsert(
          internalDefinition,
          binding,
          internalDefinition.ParameterGroup))
        {
          throw new InvalidOperationException(
            "BindingMap.ReInsert 返回 false：" + field.ParameterName);
        }
        return;
      }

      ExternalDefinition external = FindExternalDefinition(
        definitionFile,
        parameterGuid)
        ?? throw new InvalidDataException(
          "临时共享参数文件缺少固定 GUID：" + field.ParameterName);
      if (!document.ParameterBindings.Insert(
        external,
        binding,
        BuiltInParameterGroup.PG_DATA))
      {
        throw new InvalidOperationException(
          "BindingMap.Insert 返回 false：" + field.ParameterName);
      }
    }

    private static ElementBinding FindBinding(
      BindingMap bindingMap,
      InternalDefinition definition)
    {
      DefinitionBindingMapIterator iterator = bindingMap.ForwardIterator();
      iterator.Reset();
      while (iterator.MoveNext())
      {
        InternalDefinition candidate = iterator.Key as InternalDefinition;
        if (candidate != null && candidate.Id == definition.Id)
          return iterator.Current as ElementBinding;
      }
      return null;
    }

    private static ExternalDefinition FindExternalDefinition(
      DefinitionFile file,
      Guid parameterGuid)
    {
      foreach (DefinitionGroup group in file.Groups)
      {
        foreach (Definition definition in group.Definitions)
        {
          ExternalDefinition external = definition as ExternalDefinition;
          if (external != null && external.GUID == parameterGuid)
            return external;
        }
      }
      return null;
    }

    private static void WriteValue(
      Element target,
      NativeStage01FieldDefinition field,
      string canonicalValue)
    {
      Parameter parameter = target.get_Parameter(field.ParameterGuid.Value)
        ?? throw new InvalidOperationException(
          "绑定后仍无法按固定 GUID 取得参数：" + field.ParameterName);
      if (parameter.IsReadOnly)
        throw new InvalidOperationException(
          "固定 GUID 参数为只读：" + field.ParameterName);
      EnsureStorageType(parameter, field.StorageType);

      bool written;
      switch (parameter.StorageType)
      {
        case StorageType.String:
          written = parameter.Set(canonicalValue ?? string.Empty);
          break;
        case StorageType.Integer:
          written = parameter.Set(ToInteger(field, canonicalValue));
          break;
        case StorageType.Double:
          written = parameter.Set(ToInternalDouble(field, canonicalValue));
          break;
        default:
          throw new InvalidDataException(
            "Stage01 不支持该 StorageType：" + parameter.StorageType);
      }
      if (!written)
        throw new InvalidOperationException(
          "Parameter.Set 返回 false：" + field.ParameterName);
    }

    private static bool ValueMatches(
      Parameter parameter,
      NativeStage01FieldDefinition field,
      string canonicalValue)
    {
      EnsureStorageType(parameter, field.StorageType);
      switch (parameter.StorageType)
      {
        case StorageType.String:
          return string.Equals(
            parameter.AsString() ?? string.Empty,
            canonicalValue ?? string.Empty,
            StringComparison.Ordinal);
        case StorageType.Integer:
          return parameter.AsInteger() == ToInteger(field, canonicalValue);
        case StorageType.Double:
          return Math.Abs(
            parameter.AsDouble() - ToInternalDouble(field, canonicalValue))
            <= DoubleTolerance;
        default:
          return false;
      }
    }

    private static int ToInteger(
      NativeStage01FieldDefinition field,
      string value)
    {
      if (string.Equals(
        field.ParameterType,
        "YesNo",
        StringComparison.OrdinalIgnoreCase))
      {
        if (bool.TryParse(value, out bool boolean)) return boolean ? 1 : 0;
        if (string.Equals(value, "是", StringComparison.Ordinal)) return 1;
        if (string.Equals(value, "否", StringComparison.Ordinal)) return 0;
        throw new FormatException("YesNo 参数值必须为 true/false 或 是/否。" );
      }
      if (int.TryParse(
        value,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out int integer))
      {
        return integer;
      }
      throw new FormatException("Integer 参数值无效：" + value);
    }

    private static double ToInternalDouble(
      NativeStage01FieldDefinition field,
      string value)
    {
      if (!double.TryParse(
        value,
        NumberStyles.Float | NumberStyles.AllowThousands,
        CultureInfo.InvariantCulture,
        out double number))
      {
        throw new FormatException("Double 参数值无效：" + value);
      }
      switch ((field.ParameterType ?? string.Empty).Trim().ToUpperInvariant())
      {
        case "LENGTH":
          return UnitUtils.ConvertToInternalUnits(
            number,
            DisplayUnitType.DUT_METERS);
        case "AREA":
          return UnitUtils.ConvertToInternalUnits(
            number,
            DisplayUnitType.DUT_SQUARE_METERS);
        case "VOLUME":
          return UnitUtils.ConvertToInternalUnits(
            number,
            DisplayUnitType.DUT_CUBIC_METERS);
        case "ANGLE":
          return UnitUtils.ConvertToInternalUnits(
            number,
            DisplayUnitType.DUT_DECIMAL_DEGREES);
        case "NUMBER":
          return number;
        default:
          throw new InvalidDataException(
            "Double 参数使用了不支持的 ParameterType："
            + field.ParameterType);
      }
    }

    private static void EnsureStorageType(
      Parameter parameter,
      string expected)
    {
      if (!string.Equals(
        parameter.StorageType.ToString(),
        expected,
        StringComparison.Ordinal))
      {
        throw new InvalidDataException(
          "固定 GUID 参数 StorageType 与 HBR 数据库不一致。" );
      }
    }
  }
}
