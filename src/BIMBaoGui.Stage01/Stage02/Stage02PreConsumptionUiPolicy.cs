using System;
using System.Collections.Generic;

namespace BIMBaoGui.Stage01.Stage02
{
  internal sealed class Stage02PreConsumptionUiDecision
  {
    internal bool Handled { get; set; }
    internal bool ShouldWriteFailureReport { get; set; } = true;
    internal bool RequiresNewPreview { get; set; }
    internal string Status { get; set; } = string.Empty;
    internal IReadOnlyList<Stage02Blocker> Blockers { get; set; } =
      Array.Empty<Stage02Blocker>();
  }

  internal static class Stage02PreConsumptionUiPolicy
  {
    internal static Stage02PreConsumptionUiDecision Decide(
      Stage02ContractException exception,
      bool consumed)
    {
      if (exception == null || consumed)
      {
        return new Stage02PreConsumptionUiDecision();
      }

      IReadOnlyList<Stage02Blocker> blockers = Stage02Collections.Freeze(
        new[]
        {
          new Stage02Blocker(exception.Code, exception.Message)
        });
      Stage02ConfirmationUiDecision ui =
        Stage02ConfirmationUiPolicy.Decide(blockers);
      return new Stage02PreConsumptionUiDecision
      {
        Handled = true,
        ShouldWriteFailureReport = false,
        RequiresNewPreview = ui.RequiresNewPreview,
        Status = ui.Status,
        Blockers = blockers
      };
    }
  }
}
