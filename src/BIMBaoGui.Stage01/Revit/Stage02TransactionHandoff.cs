using System;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class Stage02TransactionHandoffDecision
  {
    internal bool CallerMustFinalize { get; set; }
    internal bool DeferredToFinalizer { get; set; }
    internal bool FailClosed { get; set; }
    internal bool TerminalConflict { get; set; }
    internal string TerminalStatus { get; set; } = string.Empty;
    internal string FinalizerTerminalStatus { get; set; } = string.Empty;
    internal string EndCallTerminalStatus { get; set; } = string.Empty;
  }

  internal sealed class Stage02TransactionHandoff
  {
    private readonly object _sync = new object();
    private Owner _owner;
    private bool _registered;
    private bool _completionClaimed;
    private string _terminalStatus = string.Empty;

    internal bool NotifyFinalizerTerminal(string terminalStatus)
    {
      if (!Stage02TransactionStatePolicy.IsTerminal(terminalStatus))
        return false;
      lock (_sync)
      {
        if (_terminalStatus.Length == 0)
          _terminalStatus = terminalStatus;
        else if (!string.Equals(
          _terminalStatus,
          terminalStatus,
          StringComparison.Ordinal))
        {
          _owner = Owner.FailClosed;
          return false;
        }
        if (_owner != Owner.Finalizer || _completionClaimed)
          return false;
        _completionClaimed = true;
        return true;
      }
    }

    internal Stage02TransactionHandoffDecision RegisterEndCallReturn(
      string returnedStatus)
    {
      lock (_sync)
      {
        if (_registered)
          return FailClosed();
        _registered = true;
        if (_owner == Owner.FailClosed)
          return FailClosed();
        if (_terminalStatus.Length > 0)
        {
          if (Stage02TransactionStatePolicy.IsTerminal(returnedStatus)
            && !string.Equals(
              _terminalStatus,
              returnedStatus,
              StringComparison.Ordinal))
          {
            _owner = Owner.FailClosed;
            return TerminalConflict(_terminalStatus, returnedStatus);
          }
          _owner = Owner.Caller;
          if (_completionClaimed) return FailClosed();
          _completionClaimed = true;
          return Caller(_terminalStatus);
        }
        if (Stage02TransactionStatePolicy.IsTerminal(returnedStatus))
        {
          _terminalStatus = returnedStatus;
          _owner = Owner.Caller;
          _completionClaimed = true;
          return Caller(returnedStatus);
        }
        if (Stage02TransactionStatePolicy.IsPending(returnedStatus))
        {
          _owner = Owner.Finalizer;
          return new Stage02TransactionHandoffDecision
          {
            DeferredToFinalizer = true
          };
        }
        _owner = Owner.FailClosed;
        return FailClosed();
      }
    }

    private static Stage02TransactionHandoffDecision Caller(string status)
    {
      return new Stage02TransactionHandoffDecision
      {
        CallerMustFinalize = true,
        TerminalStatus = status ?? string.Empty
      };
    }

    private static Stage02TransactionHandoffDecision FailClosed()
    {
      return new Stage02TransactionHandoffDecision { FailClosed = true };
    }

    private static Stage02TransactionHandoffDecision TerminalConflict(
      string finalizerStatus,
      string endCallStatus)
    {
      return new Stage02TransactionHandoffDecision
      {
        FailClosed = true,
        TerminalConflict = true,
        FinalizerTerminalStatus = finalizerStatus ?? string.Empty,
        EndCallTerminalStatus = endCallStatus ?? string.Empty
      };
    }

    private enum Owner
    {
      Undecided,
      Caller,
      Finalizer,
      FailClosed
    }
  }
}
