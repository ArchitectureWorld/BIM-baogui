using System.Threading;

namespace BIMBaoGui.Stage01.Stage03
{
  internal sealed class Stage03HostCallbackStartGate
  {
    private const int Pending = 0;
    private const int Started = 1;
    private const int Abandoned = 2;
    private int _state = Pending;

    internal bool TryStart()
    {
      return Interlocked.CompareExchange(
        ref _state,
        Started,
        Pending) == Pending;
    }

    internal bool TryAbandon()
    {
      return Interlocked.CompareExchange(
        ref _state,
        Abandoned,
        Pending) == Pending;
    }
  }
}
