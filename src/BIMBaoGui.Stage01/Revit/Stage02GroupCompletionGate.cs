using System.Threading;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class Stage02GroupCompletionGate
  {
    private int _claimed;

    internal bool TryClaimTerminal(string groupStatus)
    {
      return Stage02TransactionStatePolicy.IsTerminal(groupStatus)
        && Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;
    }
  }
}
