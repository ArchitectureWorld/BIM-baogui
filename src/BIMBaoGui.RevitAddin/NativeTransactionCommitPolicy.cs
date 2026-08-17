using System;

namespace BIMBaoGui.RevitAddin
{
  internal static class NativeTransactionCommitPolicy
  {
    internal static void RequireCommitted(
      string status,
      string errorCode)
    {
      if (string.Equals(status, "Committed", StringComparison.Ordinal)) return;
      throw new InvalidOperationException(errorCode ?? string.Empty);
    }
  }
}
