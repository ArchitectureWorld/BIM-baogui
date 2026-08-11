using BIMBaoGui.Stage01.Revit.Parameters;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HbrParameterTextValuePolicyTests
  {
    [Fact]
    public void Evaluate_PreservesWhitespaceButMarksNoBusinessValue()
    {
      const string text = " \t ";

      HbrParameterTextValueDecision decision =
        HbrParameterTextValuePolicy.Evaluate(text);

      Assert.Equal(text, decision.RawValue);
      Assert.Equal(text, decision.CanonicalValue);
      Assert.False(decision.HasBusinessValue);
    }

    [Fact]
    public void Evaluate_PreservesNonBlankValueAndMarksBusinessValue()
    {
      const string text = "  HBR value\t";

      HbrParameterTextValueDecision decision =
        HbrParameterTextValuePolicy.Evaluate(text);

      Assert.Equal(text, decision.RawValue);
      Assert.Equal(text, decision.CanonicalValue);
      Assert.True(decision.HasBusinessValue);
    }
  }
}
