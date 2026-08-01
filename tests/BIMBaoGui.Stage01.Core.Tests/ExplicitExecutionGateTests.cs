using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class ExplicitExecutionGateTests
  {
    [Fact]
    public void Observe_DoesNotTriggerForAnInitialTrueSample()
    {
      var gate = new ExplicitExecutionGate();

      Assert.False(gate.Observe(true));
    }

    [Fact]
    public void Observe_DoesNotTriggerForAnInitialFalseSample()
    {
      var gate = new ExplicitExecutionGate();

      Assert.False(gate.Observe(false));
    }

    [Fact]
    public void Observe_TriggersOnlyForSubsequentFalseToTrueTransitions()
    {
      var gate = new ExplicitExecutionGate();

      Assert.False(gate.Observe(false));
      Assert.True(gate.Observe(true));
      Assert.False(gate.Observe(true));
      Assert.False(gate.Observe(false));
      Assert.True(gate.Observe(true));
    }
  }
}
