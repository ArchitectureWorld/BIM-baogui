using System;
using BIMBaoGui.RevitAddin;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeTransactionCommitPolicyTests
  {
    [Fact]
    public void Committed_status_is_the_only_accepted_terminal_status()
    {
      NativeTransactionCommitPolicy.RequireCommitted(
        "Committed",
        "METRIC_TRANSACTION_NOT_COMMITTED");

      foreach (string status in new[]
      {
        "RolledBack",
        "Pending"
      })
      {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
          () => NativeTransactionCommitPolicy.RequireCommitted(
            status,
            "METRIC_TRANSACTION_NOT_COMMITTED"));
        Assert.Equal("METRIC_TRANSACTION_NOT_COMMITTED", exception.Message);
      }
    }
  }
}
