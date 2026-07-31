using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.Stage01.Core
{
  internal static class Stage01UiPolicy
  {
    private static readonly string[] HiddenSystemGroups =
    {
      "00_当前Revit文件",
      "09_提交与回读"
    };

    public static string[] BuildDirectoryGroups(IEnumerable<string> registryGroups)
    {
      var result = new List<string>();
      foreach (string group in registryGroups ?? Array.Empty<string>())
      {
        if (string.IsNullOrWhiteSpace(group)) continue;
        if (HiddenSystemGroups.Contains(group, StringComparer.Ordinal)) continue;
        if (!result.Contains(group, StringComparer.Ordinal)) result.Add(group);
      }

      if (!result.Contains("10_项目条件", StringComparer.Ordinal)) result.Add("10_项目条件");
      if (!result.Contains("11_提交与校验", StringComparer.Ordinal)) result.Add("11_提交与校验");
      return result.ToArray();
    }

    public static string DecorateRequiredLabel(string label, bool required)
    {
      string normalized = label ?? string.Empty;
      return required ? normalized + " *" : normalized;
    }

    public static int ClampScrollOffset(int requested, int itemCount, int visibleCount)
    {
      int count = Math.Max(0, itemCount);
      int visible = Math.Max(1, visibleCount);
      int maximum = Math.Max(0, count - visible);
      return Math.Max(0, Math.Min(requested, maximum));
    }

    public static int ScrollByWheel(int current, int delta, int itemCount, int visibleCount)
    {
      if (delta == 0) return ClampScrollOffset(current, itemCount, visibleCount);
      int rows = Math.Max(1, Math.Abs(delta) / 120) * 3;
      int requested = current + (delta > 0 ? -rows : rows);
      return ClampScrollOffset(requested, itemCount, visibleCount);
    }
  }
}
