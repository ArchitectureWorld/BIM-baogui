using System;

namespace BIMBaoGui.Stage01.Revit
{
  internal static class Stage02TransactionStatePolicy
  {
    internal static bool CanRollbackGroup(
      string transactionStatus,
      string groupStatus)
    {
      return IsTerminal(transactionStatus)
        && string.Equals(groupStatus, "Started", StringComparison.Ordinal);
    }

    internal static bool CanDispose(
      string transactionStatus,
      string groupStatus)
    {
      return IsTerminal(transactionStatus) && IsTerminal(groupStatus);
    }

    internal static bool IsTerminal(string status)
    {
      return string.Equals(status, "Committed", StringComparison.Ordinal)
        || string.Equals(status, "RolledBack", StringComparison.Ordinal);
    }

    internal static bool IsPending(string status)
    {
      return string.Equals(status, "Pending", StringComparison.Ordinal);
    }

    internal static bool CanDisposeAfterRejectedStart(string status)
    {
      return IsTerminal(status)
        || string.Equals(status, "Error", StringComparison.Ordinal)
        || string.Equals(status, "Uninitialized", StringComparison.Ordinal);
    }

  }
}
