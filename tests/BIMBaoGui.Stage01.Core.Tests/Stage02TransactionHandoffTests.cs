using BIMBaoGui.Stage01.Revit;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage02TransactionHandoffTests
  {
    [Fact]
    public void Finalizer_before_end_call_return_is_completed_by_caller_once()
    {
      var handoff = new Stage02TransactionHandoff();

      Assert.False(handoff.NotifyFinalizerTerminal("Committed"));
      Stage02TransactionHandoffDecision decision =
        handoff.RegisterEndCallReturn("Committed");

      Assert.True(decision.CallerMustFinalize);
      Assert.False(decision.DeferredToFinalizer);
      Assert.Equal("Committed", decision.TerminalStatus);
      Assert.False(handoff.NotifyFinalizerTerminal("Committed"));
    }

    [Fact]
    public void Pending_end_call_transfers_single_completion_to_finalizer()
    {
      var handoff = new Stage02TransactionHandoff();

      Stage02TransactionHandoffDecision decision =
        handoff.RegisterEndCallReturn("Pending");

      Assert.True(decision.DeferredToFinalizer);
      Assert.False(decision.CallerMustFinalize);
      Assert.True(handoff.NotifyFinalizerTerminal("Committed"));
      Assert.False(handoff.NotifyFinalizerTerminal("Committed"));
    }

    [Theory]
    [InlineData("Committed")]
    [InlineData("RolledBack")]
    public void Synchronous_terminal_return_is_owned_by_caller(
      string status)
    {
      var handoff = new Stage02TransactionHandoff();

      Stage02TransactionHandoffDecision decision =
        handoff.RegisterEndCallReturn(status);

      Assert.True(decision.CallerMustFinalize);
      Assert.Equal(status, decision.TerminalStatus);
      Assert.False(handoff.NotifyFinalizerTerminal(status));
    }

    [Fact]
    public void Pending_with_terminal_race_before_registration_uses_caller()
    {
      var handoff = new Stage02TransactionHandoff();
      Assert.False(handoff.NotifyFinalizerTerminal("RolledBack"));

      Stage02TransactionHandoffDecision decision =
        handoff.RegisterEndCallReturn("Pending");

      Assert.True(decision.CallerMustFinalize);
      Assert.False(decision.DeferredToFinalizer);
      Assert.Equal("RolledBack", decision.TerminalStatus);
    }

    [Fact]
    public void Conflicting_terminal_callbacks_before_registration_fail_closed()
    {
      var handoff = new Stage02TransactionHandoff();
      Assert.False(handoff.NotifyFinalizerTerminal("Committed"));
      Assert.False(handoff.NotifyFinalizerTerminal("RolledBack"));

      Stage02TransactionHandoffDecision decision =
        handoff.RegisterEndCallReturn("Pending");

      Assert.True(decision.FailClosed);
      Assert.False(decision.CallerMustFinalize);
      Assert.False(decision.DeferredToFinalizer);
    }

    [Theory]
    [InlineData("Committed", "RolledBack")]
    [InlineData("RolledBack", "Committed")]
    public void Cached_finalizer_and_end_call_terminal_conflict_fail_closed(
      string finalizerStatus,
      string returnedStatus)
    {
      var handoff = new Stage02TransactionHandoff();
      Assert.False(handoff.NotifyFinalizerTerminal(finalizerStatus));

      Stage02TransactionHandoffDecision decision =
        handoff.RegisterEndCallReturn(returnedStatus);

      Assert.True(decision.FailClosed);
      Assert.False(decision.CallerMustFinalize);
      Assert.False(decision.DeferredToFinalizer);
      Assert.True(decision.TerminalConflict);
      Assert.Equal(finalizerStatus, decision.FinalizerTerminalStatus);
      Assert.Equal(returnedStatus, decision.EndCallTerminalStatus);
      Assert.False(handoff.NotifyFinalizerTerminal(finalizerStatus));
    }

    [Theory]
    [InlineData("Error")]
    [InlineData("Proceed")]
    [InlineData("Uninitialized")]
    [InlineData("Unexpected")]
    public void Nonterminal_return_never_grants_completion_ownership(
      string status)
    {
      var handoff = new Stage02TransactionHandoff();

      Stage02TransactionHandoffDecision decision =
        handoff.RegisterEndCallReturn(status);

      Assert.False(decision.CallerMustFinalize);
      Assert.False(decision.DeferredToFinalizer);
      Assert.True(decision.FailClosed);
    }
  }
}
