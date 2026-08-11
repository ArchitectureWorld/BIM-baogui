using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.Stage02;

namespace BIMBaoGui.Stage01.Revit.Parameters
{
  internal sealed class HbrSharedParameterInstaller
  {
    private const bool Visible = true;
    private const bool UserModifiable = true;
    private const bool HideWhenNoValue = false;

    internal void EnsureBindings(
      Document document,
      Stage02Preview preview,
      HbrRuleDatabase database)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (preview == null) throw new ArgumentNullException(nameof(preview));
      if (database == null) throw new ArgumentNullException(nameof(database));
      if (document.IsFamilyDocument)
        throw new InvalidOperationException(
          "族文档不能访问 Document.ParameterBindings。");

      IDictionary<string, BindingRequest> requests = BuildRequests(
        preview,
        database);
      Autodesk.Revit.ApplicationServices.Application application =
        document.Application;
      string originalSharedParameterPath =
        application.SharedParametersFilename;
      string temporaryPath = Path.Combine(
        Path.GetTempPath(),
        "BIMBaoGui_HBR_" + Guid.NewGuid().ToString("N") + ".txt");
      Exception primaryException = null;
      try
      {
        HbrSharedParameterDefinitionText.WriteRevitFile(
          temporaryPath,
          requests.Values.Select(request => request.Property));
        application.SharedParametersFilename = temporaryPath;
        DefinitionFile definitionFile = application.OpenSharedParameterFile();
        if (definitionFile == null)
          throw new InvalidOperationException(
            "Revit 无法打开临时 HBR 共享参数文件。");

        foreach (BindingRequest request in requests.Values
          .OrderBy(item => item.Property.PropertyId, StringComparer.Ordinal))
        {
          EnsureOne(
            document,
            application,
            definitionFile,
            request);
        }
      }
      catch (Exception exception)
      {
        primaryException = exception;
      }
      finally
      {
        HbrTemporaryFileCleanup.Complete(
          primaryException,
          () => application.SharedParametersFilename =
            originalSharedParameterPath,
          () => HbrTemporaryFileCleanup.DeleteTemporaryFile(temporaryPath));
      }
    }

    private static IDictionary<string, BindingRequest> BuildRequests(
      Stage02Preview preview,
      HbrRuleDatabase database)
    {
      var result = new Dictionary<string, BindingRequest>(
        StringComparer.Ordinal);
      foreach (Stage02MatchedElement matched in preview.Elements)
      {
        HbrCarrierRole role;
        if (!database.CarrierRolesById.TryGetValue(matched.RoleId, out role))
          throw new InvalidOperationException(
            "预览引用了未知 HBR 载体角色。");
        foreach (Stage02WriteOperation operation in matched.Operations)
        {
          HbrRuleProperty property;
          if (!database.PropertiesById.TryGetValue(
            operation.PropertyId,
            out property))
          {
            throw new InvalidOperationException(
              "预览引用了未知 HBR 属性规则。");
          }
          BindingRequest request;
          if (!result.TryGetValue(property.PropertyId, out request))
          {
            request = new BindingRequest(property);
            result.Add(property.PropertyId, request);
          }
          foreach (string category in role.RevitCategories)
            request.RequestedCategories.Add(category);
        }
      }
      return result;
    }

    private static void EnsureOne(
      Document document,
      Autodesk.Revit.ApplicationServices.Application application,
      DefinitionFile definitionFile,
      BindingRequest request)
    {
      HbrRuleProperty property = request.Property;
      ValidateRuleVisibility(property);
      SharedParameterElement shared = SharedParameterElement.Lookup(
        document,
        property.Revit.ParameterGuid);
      InternalDefinition internalDefinition = shared == null
        ? null
        : shared.GetDefinition();
      if (internalDefinition != null
        && !IsAllowedName(property, internalDefinition.Name))
      {
        throw new InvalidOperationException(
          "同 GUID 的现有共享参数名称不属于规则允许集合。");
      }
      ElementBinding existingBinding = internalDefinition == null
        ? null
        : FindBinding(document.ParameterBindings, internalDefinition);
      EnsureBindingScope(property, existingBinding);

      CategorySet union = application.Create.NewCategorySet();
      var existingCategoryIds = new HashSet<int>();
      if (existingBinding != null)
      {
        foreach (Category category in existingBinding.Categories)
        {
          union.Insert(category);
          existingCategoryIds.Add(category.Id.IntegerValue);
        }
      }
      bool requiresCategoryMerge = existingBinding == null;
      foreach (string categoryName in request.RequestedCategories)
      {
        BuiltInCategory categoryId;
        if (!Enum.TryParse(categoryName, out categoryId))
          throw new InvalidOperationException(
            "HBR 规则包含未知 BuiltInCategory。");
        Category category = Category.GetCategory(document, categoryId);
        if (category == null)
          throw new InvalidOperationException(
            "当前文档不支持 HBR 规则请求的 BuiltInCategory。");
        union.Insert(category);
        if (!existingCategoryIds.Contains(category.Id.IntegerValue))
          requiresCategoryMerge = true;
      }

      if (existingBinding != null && !requiresCategoryMerge)
        return;

      Binding binding = HbrBindingScopePolicy.RequiresTypeBinding(
        property.Revit.BindingScope)
        ? (Binding)application.Create.NewTypeBinding(union)
        : application.Create.NewInstanceBinding(union);
      BuiltInParameterGroup group = existingBinding == null
        ? BuiltInParameterGroup.PG_DATA
        : internalDefinition.ParameterGroup;
      if (existingBinding != null)
      {
        if (!document.ParameterBindings.ReInsert(
          internalDefinition,
          binding,
          group))
        {
          throw new InvalidOperationException(
            "HBR 共享参数 BindingMap.ReInsert 返回 false。");
        }
        return;
      }

      Definition definition = internalDefinition;
      if (definition == null)
      {
        definition = FindExternalDefinition(
          definitionFile,
          property.Revit.ParameterGuid);
        if (definition == null)
          throw new InvalidOperationException(
            "临时 HBR 共享参数文件缺少固定 GUID 定义。");
      }
      if (!document.ParameterBindings.Insert(definition, binding, group))
        throw new InvalidOperationException(
          "HBR 共享参数 BindingMap.Insert 返回 false。");
    }

    private static void ValidateRuleVisibility(HbrRuleProperty property)
    {
      if (property.Revit.Visible != Visible
        || property.Revit.UserModifiable != UserModifiable
        || HideWhenNoValue)
      {
        throw new InvalidOperationException(
          "HBR 规则共享参数必须 Visible = true、UserModifiable = true、HideWhenNoValue = false。");
      }
    }

    private static bool IsAllowedName(
      HbrRuleProperty property,
      string actualName)
    {
      return string.Equals(
          property.Revit.ParameterName,
          actualName,
          StringComparison.Ordinal)
        || property.Revit.LegacyNames.Contains(
          actualName,
          StringComparer.Ordinal);
    }

    private static void EnsureBindingScope(
      HbrRuleProperty property,
      ElementBinding existing)
    {
      if (existing == null) return;
      bool expectedType = HbrBindingScopePolicy.RequiresTypeBinding(
        property.Revit.BindingScope);
      if ((expectedType && !(existing is TypeBinding))
        || (!expectedType && !(existing is InstanceBinding)))
      {
        throw new InvalidOperationException(
          "同 GUID 参数的 InstanceBinding/TypeBinding 与规则冲突。");
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

    private sealed class BindingRequest
    {
      internal BindingRequest(HbrRuleProperty property)
      {
        Property = property;
        RequestedCategories = new HashSet<string>(StringComparer.Ordinal);
      }

      internal HbrRuleProperty Property { get; }
      internal ISet<string> RequestedCategories { get; }
    }
  }
}
