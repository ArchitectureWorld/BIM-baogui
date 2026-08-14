using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02SelectionInventoryPolicy
  {
    internal static NativeStage02InventoryDecision Resolve(
      IEnumerable<NativeStage02ElementSnapshot> inventory,
      IEnumerable<string> selectedUniqueIds)
    {
      string[] selected = CanonicalizeIds(selectedUniqueIds);
      if (selected.Length == 0)
      {
        return NativeStage02InventoryDecision.Failure(
          NativeStage02InventoryCodes.SelectionEmpty,
          "当前选择必须至少包含一个当前文档中的可持久化模型元素。" );
      }

      var byUniqueId = (inventory ?? Array.Empty<NativeStage02ElementSnapshot>())
        .Where(value => value != null
          && !string.IsNullOrWhiteSpace(value.UniqueId))
        .GroupBy(value => value.UniqueId.Trim(), StringComparer.Ordinal)
        .ToDictionary(
          group => group.Key,
          group => group.OrderBy(value => value.ElementId).First(),
          StringComparer.Ordinal);

      var accepted = new List<NativeStage02ElementSnapshot>();
      foreach (string uniqueId in selected)
      {
        NativeStage02ElementSnapshot element;
        if (!byUniqueId.TryGetValue(uniqueId, out element))
        {
          return NativeStage02InventoryDecision.Failure(
            NativeStage02InventoryCodes.SelectionElementMissing,
            "当前选择中的构件已经不存在：" + uniqueId);
        }

        if (!IsEligible(element))
        {
          return NativeStage02InventoryDecision.Failure(
            NativeStage02InventoryCodes.SelectionElementNotEligible,
            "当前选择中的构件不能进入 Stage02 写入流程："
              + Describe(element));
        }

        accepted.Add(element);
      }

      return NativeStage02InventoryDecision.Success(
        accepted.OrderBy(value => value.UniqueId, StringComparer.Ordinal));
    }

    internal static bool IsEligible(NativeStage02ElementSnapshot element)
    {
      return element != null
        && !string.IsNullOrWhiteSpace(element.UniqueId)
        && !element.IsElementType
        && !element.IsViewSpecific
        && !element.IsImported
        && !element.IsLinked
        && element.IsModelElement;
    }

    private static string[] CanonicalizeIds(IEnumerable<string> values)
    {
      return (values ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
    }

    private static string Describe(NativeStage02ElementSnapshot element)
    {
      if (element == null) return "<null>";
      return string.Join("; ", new[]
      {
        "ElementId=" + element.ElementId,
        "UniqueId=" + (element.UniqueId ?? string.Empty),
        "CategoryKey=" + (element.Category ?? string.Empty),
        "CategoryName=" + (element.CategoryName ?? string.Empty),
        "CLRType=" + (element.ClrType ?? string.Empty),
        "ElementKind=" + (element.ElementKind ?? string.Empty),
        "IsViewSpecific=" + element.IsViewSpecific,
        "IsImported=" + element.IsImported,
        "IsLinked=" + element.IsLinked
      });
    }
  }
}
