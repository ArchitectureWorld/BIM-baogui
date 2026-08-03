using System;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class Stage02LateCleanupDecision
  {
    internal bool ShouldAttemptCleanup { get; set; }
    internal string TerminalStatus { get; set; } = string.Empty;
  }

  internal sealed class Stage02LateCleanupCoordinator
  {
    private readonly object _sync = new object();
    private bool _failureOutcomeDeclared;
    private bool _cleanupCompleted;
    private string _terminalStatus = string.Empty;

    internal Stage02LateCleanupDecision DeclareFailureOutcome()
    {
      lock (_sync)
      {
        _failureOutcomeDeclared = true;
        return Decide();
      }
    }

    internal Stage02LateCleanupDecision ObserveTerminal(string terminalStatus)
    {
      lock (_sync)
      {
        if (Stage02TransactionStatePolicy.IsTerminal(terminalStatus)
          && _terminalStatus.Length == 0)
        {
          _terminalStatus = terminalStatus;
        }
        return Decide();
      }
    }

    internal void MarkCleanupCompleted()
    {
      lock (_sync)
      {
        _cleanupCompleted = true;
      }
    }

    private Stage02LateCleanupDecision Decide()
    {
      bool shouldAttempt = _failureOutcomeDeclared
        && !_cleanupCompleted
        && Stage02TransactionStatePolicy.IsTerminal(_terminalStatus);
      return new Stage02LateCleanupDecision
      {
        ShouldAttemptCleanup = shouldAttempt,
        TerminalStatus = shouldAttempt ? _terminalStatus : string.Empty
      };
    }
  }
}
