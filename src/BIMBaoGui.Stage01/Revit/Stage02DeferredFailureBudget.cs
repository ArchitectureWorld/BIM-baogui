using System;
using System.Threading;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class Stage02DeferredFailureDecision
  {
    internal bool ShouldRetry { get; set; }
    internal bool ShouldFailClosed { get; set; }
    internal int FailureCount { get; set; }
  }

  internal sealed class Stage02DeferredFailureBudget
  {
    private readonly int _maximumFailures;
    private int _failureCount;

    internal Stage02DeferredFailureBudget(int maximumFailures)
    {
      if (maximumFailures <= 0)
        throw new ArgumentOutOfRangeException(nameof(maximumFailures));
      _maximumFailures = maximumFailures;
    }

    internal Stage02DeferredFailureDecision RegisterFailure()
    {
      int count = Interlocked.Increment(ref _failureCount);
      bool exhausted = count >= _maximumFailures;
      return new Stage02DeferredFailureDecision
      {
        ShouldRetry = !exhausted,
        ShouldFailClosed = exhausted,
        FailureCount = count
      };
    }

    internal void Reset()
    {
      Interlocked.Exchange(ref _failureCount, 0);
    }
  }

  internal sealed class Stage02ExecutionOutcomeGate
  {
    private int _claimed;

    internal bool IsClaimed
    {
      get { return Volatile.Read(ref _claimed) != 0; }
    }

    internal bool TryClaim()
    {
      return Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;
    }
  }
}
