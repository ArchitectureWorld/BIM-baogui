using BIMBaoGui.Stage01.Revit;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage02GroupCompletionGateTests
  {
    [Theory]
    [InlineData("Pending")]
    [InlineData("Started")]
    [InlineData("Error")]
    [InlineData("Unexpected")]
    public void Nonterminal_group_never_reports_completes_or_disposes(
      string status)
    {
      var gate = new Stage02GroupCompletionGate();
      int reportCalls = 0;
      int completedCalls = 0;
      int disposeCalls = 0;

      if (gate.TryClaimTerminal(status))
      {
        reportCalls++;
        completedCalls++;
        disposeCalls++;
      }

      Assert.Equal(0, reportCalls);
      Assert.Equal(0, completedCalls);
      Assert.Equal(0, disposeCalls);
    }

    [Theory]
    [InlineData("Committed")]
    [InlineData("RolledBack")]
    public void Terminal_group_grants_exactly_one_completion(string status)
    {
      var gate = new Stage02GroupCompletionGate();
      int reportCalls = 0;
      int completedCalls = 0;
      int disposeCalls = 0;

      for (int attempt = 0; attempt < 2; attempt++)
      {
        if (!gate.TryClaimTerminal(status)) continue;
        reportCalls++;
        completedCalls++;
        disposeCalls++;
      }

      Assert.Equal(1, reportCalls);
      Assert.Equal(1, completedCalls);
      Assert.Equal(1, disposeCalls);
    }

    [Fact]
    public void Execution_outcome_can_be_claimed_exactly_once()
    {
      var gate = new Stage02ExecutionOutcomeGate();

      Assert.True(gate.TryClaim());
      Assert.False(gate.TryClaim());
      Assert.True(gate.IsClaimed);
    }

    [Theory]
    [InlineData("Committed")]
    [InlineData("RolledBack")]
    public void Fatal_outcome_does_not_consume_late_terminal_cleanup(
      string transactionStatus)
    {
      var outcomeGate = new Stage02ExecutionOutcomeGate();
      var cleanupGate = new Stage02ExecutionCleanupGate();
      int reportCalls = 0;
      int callbackCalls = 0;
      int disposeCalls = 0;

      if (outcomeGate.TryClaim())
      {
        reportCalls++;
        callbackCalls++;
      }
      if (cleanupGate.TryClaimTerminal(transactionStatus, "RolledBack"))
        disposeCalls++;
      if (outcomeGate.TryClaim())
      {
        reportCalls++;
        callbackCalls++;
      }
      if (cleanupGate.TryClaimTerminal(transactionStatus, "RolledBack"))
        disposeCalls++;

      Assert.Equal(1, reportCalls);
      Assert.Equal(1, callbackCalls);
      Assert.Equal(1, disposeCalls);
    }

    [Fact]
    public void Group_only_terminal_cleanup_is_claimed_exactly_once()
    {
      var gate = new Stage02ExecutionCleanupGate();

      Assert.False(gate.TryClaimGroupOnlyTerminal("Pending"));
      Assert.True(gate.TryClaimGroupOnlyTerminal("RolledBack"));
      Assert.False(gate.TryClaimGroupOnlyTerminal("RolledBack"));
    }

    [Theory]
    [InlineData("Committed")]
    [InlineData("RolledBack")]
    public void Fatal_before_finalizer_requests_late_cleanup(
      string terminalStatus)
    {
      var coordinator = new Stage02LateCleanupCoordinator();

      Assert.False(coordinator.DeclareFailureOutcome().ShouldAttemptCleanup);
      Stage02LateCleanupDecision decision =
        coordinator.ObserveTerminal(terminalStatus);

      Assert.True(decision.ShouldAttemptCleanup);
      Assert.Equal(terminalStatus, decision.TerminalStatus);
    }

    [Theory]
    [InlineData("Committed")]
    [InlineData("RolledBack")]
    public void Finalizer_before_fatal_requests_cleanup_without_handoff_owner(
      string terminalStatus)
    {
      var coordinator = new Stage02LateCleanupCoordinator();

      Assert.False(
        coordinator.ObserveTerminal(terminalStatus).ShouldAttemptCleanup);
      Stage02LateCleanupDecision decision =
        coordinator.DeclareFailureOutcome();

      Assert.True(decision.ShouldAttemptCleanup);
      Assert.Equal(terminalStatus, decision.TerminalStatus);
    }

    [Fact]
    public void Completed_cleanup_makes_duplicate_terminal_callbacks_no_op()
    {
      var coordinator = new Stage02LateCleanupCoordinator();
      coordinator.DeclareFailureOutcome();
      Assert.True(
        coordinator.ObserveTerminal("Committed").ShouldAttemptCleanup);

      coordinator.MarkCleanupCompleted();

      Assert.False(
        coordinator.ObserveTerminal("Committed").ShouldAttemptCleanup);
      Assert.False(
        coordinator.ObserveTerminal("RolledBack").ShouldAttemptCleanup);
      Assert.False(coordinator.DeclareFailureOutcome().ShouldAttemptCleanup);
    }

    [Fact]
    public void Normal_cleanup_before_late_callback_is_also_a_no_op()
    {
      var coordinator = new Stage02LateCleanupCoordinator();
      coordinator.MarkCleanupCompleted();

      Assert.False(
        coordinator.ObserveTerminal("Committed").ShouldAttemptCleanup);
    }
  }
}
