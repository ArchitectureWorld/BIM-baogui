using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.Stage01.Stage02
{
  internal sealed class Stage02SelectionSetDecision
  {
    internal bool Success { get; set; }
    internal Stage02Blocker Blocker { get; set; }
  }

  internal static class Stage02SelectionSetPolicy
  {
    internal static Stage02SelectionSetDecision Evaluate(
      IEnumerable<string> expectedUniqueIds,
      IEnumerable<string> liveUniqueIds)
    {
      string[] expected = (expectedUniqueIds ?? Array.Empty<string>())
        .ToArray();
      string[] live = (liveUniqueIds ?? Array.Empty<string>()).ToArray();
      bool invalid = expected.Length == 0
        || live.Length == 0
        || expected.Any(string.IsNullOrWhiteSpace)
        || live.Any(string.IsNullOrWhiteSpace)
        || expected.Distinct(StringComparer.Ordinal).Count() != expected.Length
        || live.Distinct(StringComparer.Ordinal).Count() != live.Length;
      bool same = !invalid
        && expected.OrderBy(value => value, StringComparer.Ordinal)
          .SequenceEqual(
            live.OrderBy(value => value, StringComparer.Ordinal),
            StringComparer.Ordinal);
      return new Stage02SelectionSetDecision
      {
        Success = same,
        Blocker = same
          ? null
          : new Stage02Blocker(
            Stage02Codes.ElementSetChanged,
            "确认时实际选择集已新增、减少、清空、重复或元素已删除；必须重新预览。")
      };
    }
  }
}
