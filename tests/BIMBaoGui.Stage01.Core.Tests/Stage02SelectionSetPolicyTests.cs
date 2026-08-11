using BIMBaoGui.Stage01.Stage02;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage02SelectionSetPolicyTests
  {
    [Fact]
    public void Live_selection_set_is_order_insensitive()
    {
      Stage02SelectionSetDecision decision =
        Stage02SelectionSetPolicy.Evaluate(
          new[] { "uid-b", "uid-a" },
          new[] { "uid-a", "uid-b" });

      Assert.True(decision.Success);
      Assert.Null(decision.Blocker);
    }

    [Fact]
    public void Added_removed_empty_or_duplicate_live_selection_is_drift()
    {
      string[] expected = { "uid-a", "uid-b" };
      string[][] changed =
      {
        new[] { "uid-a", "uid-b", "uid-c" },
        new[] { "uid-a" },
        new string[0],
        new[] { "uid-a", "uid-a" }
      };

      foreach (string[] live in changed)
      {
        Stage02SelectionSetDecision decision =
          Stage02SelectionSetPolicy.Evaluate(expected, live);

        Assert.False(decision.Success);
        Assert.NotNull(decision.Blocker);
        Assert.Equal(Stage02Codes.ElementSetChanged, decision.Blocker.Code);
      }
    }
  }
}
