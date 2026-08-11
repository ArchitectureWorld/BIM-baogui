using System;
using BIMBaoGui.Stage01.Revit;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage02TransactionStatePolicyTests
  {
    [Theory]
    [InlineData("Pending")]
    [InlineData("Started")]
    public void Active_or_pending_transaction_blocks_group_rollback(
      string transactionStatus)
    {
      Assert.False(Stage02TransactionStatePolicy.CanRollbackGroup(
        transactionStatus,
        "Started"));
    }

    [Fact]
    public void Terminal_transaction_allows_started_group_rollback()
    {
      Assert.True(Stage02TransactionStatePolicy.CanRollbackGroup(
        "RolledBack",
        "Started"));
      Assert.True(Stage02TransactionStatePolicy.CanRollbackGroup(
        "Committed",
        "Started"));
    }

    [Theory]
    [InlineData("Pending", "Started")]
    [InlineData("Committed", "Pending")]
    [InlineData("Started", "Started")]
    public void Pending_or_active_scope_cannot_be_disposed(
      string transactionStatus,
      string groupStatus)
    {
      Assert.False(Stage02TransactionStatePolicy.CanDispose(
        transactionStatus,
        groupStatus));
    }

    [Theory]
    [InlineData("Committed", "Committed")]
    [InlineData("RolledBack", "RolledBack")]
    [InlineData("RolledBack", "Committed")]
    public void Terminal_scopes_can_be_disposed(
      string transactionStatus,
      string groupStatus)
    {
      Assert.True(Stage02TransactionStatePolicy.CanDispose(
        transactionStatus,
        groupStatus));
    }

    [Theory]
    [InlineData("Error")]
    [InlineData("Proceed")]
    [InlineData("Uninitialized")]
    [InlineData("Unexpected")]
    [InlineData("")]
    public void Nonterminal_or_unknown_status_is_fail_closed(string status)
    {
      Assert.False(Stage02TransactionStatePolicy.CanDispose(
        status,
        "Committed"));
      Assert.False(Stage02TransactionStatePolicy.CanDispose(
        "Committed",
        status));
      Assert.False(Stage02TransactionStatePolicy.CanRollbackGroup(
        status,
        "Started"));
    }

    [Theory]
    [InlineData("Error")]
    [InlineData("Uninitialized")]
    [InlineData("Committed")]
    [InlineData("RolledBack")]
    public void Explicit_nonactive_start_result_allows_wrapper_dispose(
      string status)
    {
      Assert.True(
        Stage02TransactionStatePolicy.CanDisposeAfterRejectedStart(status));
    }

    [Theory]
    [InlineData("Started")]
    [InlineData("Pending")]
    [InlineData("Unexpected")]
    [InlineData("")]
    public void Active_pending_or_unknown_start_result_keeps_wrapper(
      string status)
    {
      Assert.False(
        Stage02TransactionStatePolicy.CanDisposeAfterRejectedStart(status));
    }

    [Fact]
    public void Rollback_throw_then_next_idle_retries_once_and_finalizes()
    {
      int rollbackCalls = 0;
      Assert.Throws<InvalidOperationException>(() =>
        Stage02DeferredTransactionPolicy.Advance(
          () => "Started",
          () =>
          {
            rollbackCalls++;
            throw new InvalidOperationException("first rollback failed");
          }));

      Stage02DeferredTransactionDecision decision =
        Stage02DeferredTransactionPolicy.Advance(
          () => "Started",
          () =>
          {
            rollbackCalls++;
            return "RolledBack";
          });

      Assert.Equal(2, rollbackCalls);
      Assert.True(decision.ShouldFinalize);
      Assert.False(decision.ShouldDefer);
      Assert.Equal("RolledBack", decision.TerminalStatus);
    }

    [Fact]
    public void Rollback_throw_then_pending_waits_for_later_terminal_status()
    {
      int rollbackCalls = 0;
      Assert.Throws<InvalidOperationException>(() =>
        Stage02DeferredTransactionPolicy.Advance(
          () => "Started",
          () =>
          {
            rollbackCalls++;
            throw new InvalidOperationException("first rollback failed");
          }));

      Stage02DeferredTransactionDecision pending =
        Stage02DeferredTransactionPolicy.Advance(
          () => "Started",
          () =>
          {
            rollbackCalls++;
            return "Pending";
          });
      Stage02DeferredTransactionDecision terminal =
        Stage02DeferredTransactionPolicy.Advance(
          () => "RolledBack",
          () =>
          {
            rollbackCalls++;
            return "Unexpected";
          });

      Assert.Equal(2, rollbackCalls);
      Assert.True(pending.ShouldDefer);
      Assert.False(pending.ShouldFinalize);
      Assert.True(terminal.ShouldFinalize);
      Assert.Equal("RolledBack", terminal.TerminalStatus);
    }

    [Fact]
    public void Rollback_throw_then_already_terminal_does_not_rollback_again()
    {
      int rollbackCalls = 0;
      Assert.Throws<InvalidOperationException>(() =>
        Stage02DeferredTransactionPolicy.Advance(
          () => "Started",
          () =>
          {
            rollbackCalls++;
            throw new InvalidOperationException("first rollback failed");
          }));

      Stage02DeferredTransactionDecision decision =
        Stage02DeferredTransactionPolicy.Advance(
          () => "Committed",
          () =>
          {
            rollbackCalls++;
            return "Unexpected";
          });

      Assert.Equal(1, rollbackCalls);
      Assert.True(decision.ShouldFinalize);
      Assert.Equal("Committed", decision.TerminalStatus);
    }

    [Theory]
    [InlineData("Error")]
    [InlineData("Uninitialized")]
    [InlineData("")]
    [InlineData("Unexpected")]
    public void Unknown_status_is_fatal_once_not_deferred(string status)
    {
      int rollbackCalls = 0;

      Stage02DeferredTransactionDecision decision =
        Stage02DeferredTransactionPolicy.Advance(
          () => status,
          () =>
          {
            rollbackCalls++;
            return "RolledBack";
          });

      Assert.Equal(0, rollbackCalls);
      Assert.True(decision.ShouldFailClosed);
      Assert.False(decision.ShouldDefer);
      Assert.False(decision.ShouldFinalize);
      Assert.Equal(status, decision.ObservedStatus);
    }

    [Fact]
    public void Started_with_unknown_rollback_result_is_fatal_after_one_attempt()
    {
      int rollbackCalls = 0;

      Stage02DeferredTransactionDecision decision =
        Stage02DeferredTransactionPolicy.Advance(
          () => "Started",
          () =>
          {
            rollbackCalls++;
            return "Unexpected";
          });

      Assert.Equal(1, rollbackCalls);
      Assert.True(decision.ShouldFailClosed);
      Assert.False(decision.ShouldDefer);
      Assert.Equal("Unexpected", decision.ObservedStatus);
    }

    [Theory]
    [InlineData("Error")]
    [InlineData("Uninitialized")]
    [InlineData("")]
    [InlineData("Unexpected")]
    public void Group_unknown_status_is_fatal_without_close_attempt(
      string status)
    {
      int closeCalls = 0;

      Stage02DeferredTransactionDecision decision =
        Stage02DeferredGroupPolicy.Advance(
          () => status,
          () =>
          {
            closeCalls++;
            return "RolledBack";
          });

      Assert.Equal(0, closeCalls);
      Assert.True(decision.ShouldFailClosed);
      Assert.False(decision.ShouldDefer);
      Assert.False(decision.ShouldFinalize);
      Assert.Equal(status, decision.ObservedStatus);
    }

    [Fact]
    public void Group_started_attempts_close_once_and_pending_defers()
    {
      int closeCalls = 0;

      Stage02DeferredTransactionDecision decision =
        Stage02DeferredGroupPolicy.Advance(
          () => "Started",
          () =>
          {
            closeCalls++;
            return "Pending";
          });

      Assert.Equal(1, closeCalls);
      Assert.True(decision.ShouldDefer);
      Assert.False(decision.ShouldFailClosed);
      Assert.False(decision.ShouldFinalize);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Persistent_deferred_exception_is_fatal_on_third_idle(
      bool throwFromRollback)
    {
      var budget = new Stage02DeferredFailureBudget(3);
      int readCalls = 0;
      int rollbackCalls = 0;

      for (int attempt = 1; attempt <= 3; attempt++)
      {
        Assert.Throws<InvalidOperationException>(() =>
          Stage02DeferredTransactionPolicy.Advance(
            () =>
            {
              readCalls++;
              if (!throwFromRollback)
                throw new InvalidOperationException("GetStatus failed");
              return "Started";
            },
            () =>
            {
              rollbackCalls++;
              throw new InvalidOperationException("RollBack failed");
            }));
        Stage02DeferredFailureDecision decision =
          budget.RegisterFailure();
        Assert.Equal(attempt < 3, decision.ShouldRetry);
        Assert.Equal(attempt == 3, decision.ShouldFailClosed);
      }

      Assert.Equal(3, readCalls);
      Assert.Equal(throwFromRollback ? 3 : 0, rollbackCalls);
    }

    [Fact]
    public void Successful_deferred_observation_resets_exception_budget()
    {
      var budget = new Stage02DeferredFailureBudget(3);
      Assert.True(budget.RegisterFailure().ShouldRetry);
      Assert.True(budget.RegisterFailure().ShouldRetry);

      budget.Reset();

      Stage02DeferredFailureDecision afterReset = budget.RegisterFailure();
      Assert.True(afterReset.ShouldRetry);
      Assert.False(afterReset.ShouldFailClosed);
      Assert.Equal(1, afterReset.FailureCount);
    }
  }
}
