using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.Stage01.Stage02
{
  internal sealed class Stage02ConfirmationUiDecision
  {
    internal bool RequiresNewPreview { get; set; }
    internal string Status { get; set; } = string.Empty;
  }

  internal static class Stage02ConfirmationUiPolicy
  {
    internal static Stage02ConfirmationUiDecision Decide(
      IEnumerable<Stage02Blocker> blockers)
    {
      Stage02Blocker[] items = (blockers ?? Array.Empty<Stage02Blocker>())
        .Where(blocker => blocker != null)
        .ToArray();
      bool retrySamePreview = items.Length > 0 && items.All(blocker =>
        string.Equals(
          blocker.Code,
          Stage02Codes.InvalidSelectionEvidence,
          StringComparison.Ordinal));
      return new Stage02ConfirmationUiDecision
      {
        RequiresNewPreview = !retrySamePreview,
        Status = retrySamePreview
          ? "确认拒绝｜预览未消费｜可补充当前选择证据后重试"
          : "确认拒绝｜必须重新生成预览"
      };
    }
  }
}
