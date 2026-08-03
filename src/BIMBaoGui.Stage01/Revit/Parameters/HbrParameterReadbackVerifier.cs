using System;
using Autodesk.Revit.DB;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.Stage02;

namespace BIMBaoGui.Stage01.Revit.Parameters
{
  internal sealed class HbrParameterReadbackVerifier
  {
    internal void Verify(
      Document document,
      Stage02Preview preview,
      HbrRuleDatabase database)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (preview == null) throw new ArgumentNullException(nameof(preview));
      if (database == null) throw new ArgumentNullException(nameof(database));
      foreach (Stage02MatchedElement matched in preview.Elements)
      {
        foreach (Stage02WriteOperation operation in matched.Operations)
        {
          HbrRuleProperty property;
          if (!database.PropertiesById.TryGetValue(
            operation.PropertyId,
            out property))
          {
            throw new InvalidOperationException(
              "回读操作引用了未知 HBR 属性规则。");
          }
          Element target = document.GetElement(operation.TargetUniqueId);
          if (target == null)
            throw new InvalidOperationException(
              "无法按 TargetUniqueId 解析 HBR 回读目标。");
          Parameter parameter = target.get_Parameter(operation.ParameterGuid);
          if (parameter == null)
            throw new InvalidOperationException(
              "固定 GUID HBR 参数回读不存在。");
          SharedParameterElement shared = SharedParameterElement.Lookup(
            document,
            operation.ParameterGuid);
          InternalDefinition definition = parameter.Definition
            as InternalDefinition;
          if (shared == null || definition == null)
            throw new InvalidOperationException(
              "固定 GUID HBR 参数不是共享参数定义。");
          if (!definition.Visible)
            throw new InvalidOperationException("HBR 共享参数定义不可见。");
          if (shared.ShouldHideWhenNoValue())
            throw new InvalidOperationException(
              "HBR 共享参数在无值时被隐藏。");
          if (!parameter.UserModifiable || parameter.IsReadOnly)
            throw new InvalidOperationException(
              "HBR 共享参数在 Revit UI 中不可编辑。");
          if (!string.Equals(
            parameter.StorageType.ToString(),
            property.Revit.StorageType,
            StringComparison.Ordinal))
          {
            throw new InvalidOperationException(
              "HBR 参数 StorageType 回读与规则不一致。");
          }
          if (!string.Equals(
            definition.ParameterType.ToString(),
            property.Revit.ParameterType,
            StringComparison.OrdinalIgnoreCase))
          {
            throw new InvalidOperationException(
              "HBR 参数 ParameterType 回读与规则不一致。");
          }
          if (!string.IsNullOrWhiteSpace(operation.SuggestedValue)
            && string.Equals(
              operation.ValueAction,
              "SET",
              StringComparison.Ordinal)
            && !HbrParameterValueConverter.TypedValueMatches(
              parameter,
              property,
              operation.SuggestedValue))
          {
            throw new InvalidOperationException(
              "HBR 参数 typed raw 值回读不一致。");
          }
        }
      }
    }
  }
}
