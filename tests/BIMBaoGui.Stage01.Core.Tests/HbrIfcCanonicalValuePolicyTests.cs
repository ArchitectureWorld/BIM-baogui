using BIMBaoGui.Stage01.Mvd;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HbrIfcCanonicalValuePolicyTests
  {
    [Theory]
    [InlineData("IfcBoolean", ".T.", ".T.", false)]
    [InlineData("IfcBoolean", ".F.", ".F.", false)]
    [InlineData("IfcInteger", "+17", "+17", false)]
    [InlineData("IfcReal", "1", "1.0", false)]
    [InlineData("IfcReal", "-1.25", "-1.25", false)]
    [InlineData("IfcLabel", "标签", "标签", true)]
    [InlineData("IfcText", "多行文本", "多行文本", true)]
    [InlineData("IfcDate", "2024-02-29", "2024-02-29", true)]
    [InlineData(
      "IfcDateTime",
      "2026-08-04T12:34:56+08:00",
      "2026-08-04T12:34:56+08:00",
      true)]
    public void Supported_values_share_one_pre_export_canonical_contract(
      string declaredType,
      string value,
      string expected,
      bool requiresStringEncoding)
    {
      HbrIfcCanonicalValueDecision decision =
        HbrIfcCanonicalValuePolicy.Validate(declaredType, value);

      Assert.True(decision.Success, decision.Message);
      Assert.Equal(expected, decision.NormalizedValue);
      Assert.Equal(requiresStringEncoding, decision.RequiresStringEncoding);
    }

    [Theory]
    [InlineData("IfcBoolean", "1")]
    [InlineData("IfcBoolean", "true")]
    [InlineData("IfcInteger", "1.0")]
    [InlineData("IfcReal", "NaN")]
    [InlineData("IfcReal", " 1.0")]
    [InlineData("IfcLabel", "")]
    [InlineData("IfcText", "")]
    [InlineData("IfcDate", "2023-02-29")]
    [InlineData("IfcDateTime", "2026-08-04T12:34:56")]
    [InlineData("IfcUnknown", "value")]
    public void Invalid_values_fail_before_raw_ifc_export(
      string declaredType,
      string value)
    {
      HbrIfcCanonicalValueDecision decision =
        HbrIfcCanonicalValuePolicy.Validate(declaredType, value);

      Assert.False(decision.Success);
      Assert.Equal("INVALID_VALUE", decision.ErrorCode);
      Assert.NotEmpty(decision.Message);
    }

    [Fact]
    public void IfcLabel_rejects_more_than_255_characters()
    {
      HbrIfcCanonicalValueDecision decision =
        HbrIfcCanonicalValuePolicy.Validate(
          "IfcLabel",
          new string('x', 256));

      Assert.False(decision.Success);
      Assert.Equal("INVALID_VALUE", decision.ErrorCode);
    }

    [Fact]
    public void IfcLabel_rejects_whitespace_only_value()
    {
      HbrIfcCanonicalValueDecision decision =
        HbrIfcCanonicalValuePolicy.Validate("IfcLabel", "   ");

      Assert.False(decision.Success);
      Assert.Equal("INVALID_VALUE", decision.ErrorCode);
    }

    [Fact]
    public void IfcText_rejects_whitespace_only_value()
    {
      HbrIfcCanonicalValueDecision decision =
        HbrIfcCanonicalValuePolicy.Validate("IfcText", " \t ");

      Assert.False(decision.Success);
      Assert.Equal("INVALID_VALUE", decision.ErrorCode);
    }
  }
}
