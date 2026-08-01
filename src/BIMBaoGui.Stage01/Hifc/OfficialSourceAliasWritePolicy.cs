using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.Stage01.Hifc
{
  internal sealed class OfficialSourceAliasWrite<T>
  {
    public int TargetElementId { get; set; }
    public Guid AliasGuid { get; set; }
    public T Item { get; set; }
    public string RawValue { get; set; } = string.Empty;
    public string OfficialSourceName { get; set; } = string.Empty;
    public string PropertySet { get; set; } = string.Empty;
    public string IfcProperty { get; set; } = string.Empty;
  }

  internal static class OfficialSourceAliasWritePolicy
  {
    public static IReadOnlyList<OfficialSourceAliasWrite<T>> Fold<T>(
      params OfficialSourceAliasWrite<T>[] writes)
    {
      OfficialSourceAliasWrite<T>[] all = (writes
        ?? Array.Empty<OfficialSourceAliasWrite<T>>())
        .Where(item => item != null)
        .ToArray();
      IGrouping<string, OfficialSourceAliasWrite<T>> conflict = all
        .GroupBy(CreateGroupKey, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault(group => group
          .Select(item => item.RawValue ?? string.Empty)
          .Distinct(StringComparer.Ordinal)
          .Count() > 1);
      if (conflict != null)
      {
        OfficialSourceAliasWrite<T> first = conflict.First();
        string properties = string.Join(
          ", ",
          conflict.Select(item => item.PropertySet
            + "."
            + item.IfcProperty)
            .Distinct(StringComparer.Ordinal));
        throw new InvalidOperationException(
          "OFFICIAL_SOURCE_VALUE_CONFLICT：同一 Revit 载体的官方精确源参数“"
          + first.OfficialSourceName
          + "”收到冲突值；ElementId="
          + first.TargetElementId
          + "；属性="
          + properties);
      }

      return all
        .GroupBy(CreateGroupKey, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .ToArray();
    }

    private static string CreateGroupKey<T>(OfficialSourceAliasWrite<T> write)
    {
      return write.TargetElementId
        + "|"
        + write.AliasGuid.ToString("D");
    }
  }
}
