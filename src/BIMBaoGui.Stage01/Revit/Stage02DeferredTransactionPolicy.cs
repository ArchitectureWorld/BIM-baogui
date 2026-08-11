using System;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class Stage02DeferredTransactionDecision
  {
    internal bool ShouldFinalize { get; set; }
    internal bool ShouldDefer { get; set; }
    internal bool ShouldFailClosed { get; set; }
    internal string TerminalStatus { get; set; } = string.Empty;
    internal string ObservedStatus { get; set; } = string.Empty;
  }

  internal static class Stage02DeferredTransactionPolicy
  {
    internal static Stage02DeferredTransactionDecision Advance(
      Func<string> readStatus,
      Func<string> rollback)
    {
      if (readStatus == null)
        throw new ArgumentNullException(nameof(readStatus));
      if (rollback == null)
        throw new ArgumentNullException(nameof(rollback));

      string status = readStatus() ?? string.Empty;
      if (string.Equals(status, "Started", StringComparison.Ordinal))
        status = rollback() ?? string.Empty;

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
