using BIMBaoGui.Stage01.Stage03;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage03HostCallbackStartGateTests
  {
    [Fact]
    public void PendingCallbackCanStartOnlyOnce()
    {
      var gate = new Stage03HostCallbackStartGate();

      Assert.True(gate.TryStart());
      Assert.False(gate.TryStart());
      Assert.False(gate.TryAbandon());
    }

    [Fact]
    public void AbandonBeforeStartRejectsLateCallback()
    {
      var gate = new Stage03HostCallbackStartGate();

      Assert.True(gate.TryAbandon());
      Assert.False(gate.TryStart());
    }

    [Fact]
    public void ErrorCompletionCanAbandonOnlyOnce()
    {
      var gate = new Stage03HostCallbackStartGate();

      Assert.True(gate.TryAbandon());
      Assert.False(gate.TryAbandon());
    }
  }
}
