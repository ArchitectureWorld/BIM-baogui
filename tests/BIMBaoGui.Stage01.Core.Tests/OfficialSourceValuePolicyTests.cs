using System;
using BIMBaoGui.Stage01.Hifc;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class OfficialSourceValuePolicyTests
  {
    [Theory]
    [InlineData("true", "True")]
    [InlineData("1", "True")]
    [InlineData("是", "True")]
    [InlineData("false", "False")]
    [InlineData("0", "False")]
    [InlineData("否", "False")]
    public void Normalize_UsesBooleanParseCompatibleText(
      string raw,
      string expected)
    {
      Assert.Equal(expected, OfficialSourceValuePolicy.Normalize(
        "IfcBoolean",
        raw));
    }

    [Fact]
    public void Normalize_PreservesNonBooleanValues()
    {
      Assert.Equal("24", OfficialSourceValuePolicy.Normalize("IfcReal", "24"));
    }

    [Fact]
    public void Normalize_RejectsUnknownBooleanValues()
    {
      Assert.Throws<FormatException>(() =>
        OfficialSourceValuePolicy.Normalize("IfcBoolean", "maybe"));
    }
  }
}
