using System;

namespace BIMBaoGui.Stage01.Revit
{
  internal static class Stage02DeferredGroupPolicy
  {
    internal static Stage02DeferredTransactionDecision Advance(
      Func<string> readStatus,
      Func<string> closeStartedGroup)
    {
      if (readStatus == null)
        throw new ArgumentNullException(nameof(readStatus));
      if (closeStartedGroup == null)
        throw new ArgumentNullException(nameof(closeStartedGroup));

      string status = readStatus() ?? string.Empty;
      if (string.Equals(status, "Started", StringComparison.Ordinal))
        status = closeStartedGroup() ?? string.Empty;

      bool terminal = Stage02TransactionStatePolicy.IsTerminal(status);
      bool pending = Stage02TransactionStatePolicy.IsPending(status);
      return new Stage02DeferredTransactionDecision
      {
        ShouldFinalize = terminal,
        ShouldDefer = pending,
        ShouldFailClosed = !terminal && !pending,
        TerminalStatus = terminal ? status : string.Empty,
        ObservedStatus = status
      };
    }
  }
}
