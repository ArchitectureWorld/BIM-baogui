using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02ParameterBindingService
  {
    internal static void Ensure(
      Document document,
      NativeStage02PropertyDefinition property,
      IEnumerable<string> categoryKeys)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (property == null) throw new ArgumentNullException(nameof(property));
      if (property.ParameterGuid == Guid.Empty)
        throw new InvalidDataException("Stage02 参数缺少固定 GUID。" );

      string[] categories = (categoryKeys ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      if (categories.Length == 0)
        throw new InvalidOperationException("Stage02 参数绑定缺少目标类别。" );

      Autodesk.Revit.ApplicationServices.Application application =
        document.Application;
      string originalSharedParameterPath = application.SharedParametersFilename;
      string temporaryPath = Path.Combine(
        Path.GetTempPath(),
        "BIMBaoGui_Native_Stage02_"
          + property.ParameterGuid.ToString("N")
          + "_"
          + Guid.NewGuid().ToString("N")
          + ".txt");
      Exception primaryFailure = null;
      try
      {
        NativeStage02SharedParameterFile.Write(
          temporaryPath,
          new[] { property });
        application.SharedParametersFilename = temporaryPath;
        DefinitionFile definitionFile = application.OpenSharedParameterFile();
        if (definitionFile == null)
          throw new InvalidOperationException(
            "Revit 无法打开 Stage02 临时共享参数文件。" );
        EnsureCore(document, application, definitionFile, property, categories);
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
            "Stage02 临时共享参数环境清理失败。",
            cleanupFailure);
      }
    }

    private static void EnsureCore(
      Document document,
      Autodesk.Revit.ApplicationServices.Application application,
      DefinitionFile definitionFile,
      NativeStage02PropertyDefinition property,
      IEnumerable<string> categoryKeys)
    {
      SharedParameterElement shared = SharedParameterElement.Lookup(
        document,
        property.ParameterGuid);
      InternalDefinition internalDefinition = shared?.GetDefinition();
      if (internalDefinition != null
        && !string.Equals(
          internalDefinition.Name,
          property.ParameterName,
          StringComparison.Ordinal))
      {
        throw new InvalidDataException(
          "同 GUID 共享参数名称冲突：" + property.ParameterName);
      }
      if (internalDefinition != null
        && !string.Equals(
          internalDefinition.ParameterType.ToString(),
          property.ParameterType,
          StringComparison.OrdinalIgnoreCase))
      {
        throw new InvalidDataException(
          "同 GUID 共享参数类型冲突：" + property.ParameterName);
      }

      ElementBinding existing = internalDefinition == null
        ? null
        : FindBinding(document.ParameterBindings, internalDefinition);
      bool expectsType = string.Equals(
        property.BindingScope,
        "TYPE",
        StringComparison.Ordinal);
      if (existing != null
        && ((expectsType && !(existing is TypeBinding))
          || (!expectsType && !(existing is InstanceBinding))))
      {
        throw new InvalidDataException(
          "同 GUID 共享参数绑定范围冲突：" + property.ParameterName);
      }

      CategorySet union = application.Create.NewCategorySet();
      var existingCategoryIds = new HashSet<int>();
      if (existing != null)
      {
        foreach (Category category in existing.Categories)
        {
          union.Insert(category);
          existingCategoryIds.Add(category.Id.IntegerValue);
        }
      }

      bool changed = false;
      foreach (string categoryKey in categoryKeys)
      {
        Category category = ResolveCategory(document, categoryKey);
        if (existingCategoryIds.Add(category.Id.IntegerValue))
        {
          union.Insert(category);
          changed = true;
        }
      }
      if (existing != null && !changed) return;

      Binding binding = expectsType
        ? (Binding)application.Create.NewTypeBinding(union)
        : application.Create.NewInstanceBinding(union);
      if (existing != null)
      {
        if (!document.ParameterBindings.ReInsert(
          internalDefinition,
          binding,
          internalDefinition.ParameterGroup))
        {
          throw new InvalidOperationException(
            "ParameterBindings.ReInsert 返回 false："
            + property.ParameterName);
        }
        return;
      }

      ExternalDefinition external = FindExternalDefinition(
        definitionFile,
        property.ParameterGuid)
        ?? throw new InvalidDataException(
          "临时共享参数文件缺少固定 GUID：" + property.ParameterName);
      if (!document.ParameterBindings.Insert(
        external,
        binding,
        BuiltInParameterGroup.PG_DATA))
      {
        throw new InvalidOperationException(
          "ParameterBindings.Insert 返回 false：" + property.ParameterName);
      }
    }

    internal static ElementBinding FindBinding(
      BindingMap bindingMap,
      InternalDefinition definition)
    {
      if (bindingMap == null || definition == null) return null;
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

    internal static bool IncludesCategory(
      ElementBinding binding,
      Category category)
    {
      if (binding == null || category == null) return false;
      foreach (Category current in binding.Categories)
      {
        if (current.Id.IntegerValue == category.Id.IntegerValue) return true;
      }
      return false;
    }

    private static Category ResolveCategory(
      Document document,
      string categoryKey)
    {
      if (!Enum.TryParse(
        categoryKey,
        false,
        out BuiltInCategory builtInCategory))
      {
        throw new InvalidDataException(
          "HBR 规则包含无法解析的 Revit 类别：" + categoryKey);
      }
      return Category.GetCategory(document, builtInCategory)
        ?? throw new InvalidOperationException(
          "当前文档不支持 Revit 类别：" + categoryKey);
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
  }
}
