using System.Threading;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class Stage02ExecutionCleanupGate
  {
    private int _claimed;

    internal bool IsClaimed
    {
      get { return Volatile.Read(ref _claimed) != 0; }
    }

    internal bool TryClaimTerminal(
      string transactionStatus,
      string groupStatus)
    {
      return Stage02TransactionStatePolicy.CanDispose(
          transactionStatus,
          groupStatus)
        && TryClaim();
    }

    internal bool TryClaimGroupOnlyTerminal(string groupStatus)
    {
      return Stage02TransactionStatePolicy.IsTerminal(groupStatus)
        && TryClaim();
    }

    private bool TryClaim()
    {
      return Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;
    }
  }
}
