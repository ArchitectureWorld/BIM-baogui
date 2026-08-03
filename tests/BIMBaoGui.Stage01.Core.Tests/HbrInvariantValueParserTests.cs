using BIMBaoGui.Stage01.Revit.Parameters;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HbrInvariantValueParserTests
  {
    [Theory]
    [InlineData("true", "1")]
    [InlineData("false", "0")]
    [InlineData("是", "1")]
    [InlineData("否", "0")]
    public void YesNo_uses_one_shared_deterministic_contract(
      string input,
      string expected)
    {
      HbrInvariantValueParseDecision decision =
        HbrInvariantValueParser.TryNormalize("Integer", "YesNo", input);

      Assert.True(decision.Success);
      Assert.Equal(expected, decision.NormalizedValue);
    }

    [Theory]
    [InlineData("Integer", "Integer", "1,000")]
    [InlineData("Integer", "YesNo", "1")]
    [InlineData("Integer", "YesNo", "0")]
    [InlineData("Integer", "Integer", "1.5")]
    [InlineData("Double", "Number", "NaN")]
    [InlineData("Double", "Number", "Infinity")]
    public void Numeric_contract_rejects_thousands_and_nonfinite_values(
      string storageType,
      string parameterType,
      string input)
    {
      HbrInvariantValueParseDecision decision =
        HbrInvariantValueParser.TryNormalize(
          storageType,
          parameterType,
          input);

      Assert.False(decision.Success);
      Assert.Equal("INVALID_VALUE", decision.ErrorCode);
    }

    [Theory]
    [InlineData("Integer", "Integer", "-12", "-12")]
    [InlineData("Double", "Number", "1234.5", "1234.5")]
    [InlineData("Double", "Number", "1,000.5", "1000.5")]
    [InlineData("Double", "Number", "1e3", "1000")]
    public void Numeric_contract_normalizes_with_stage01_compatible_culture(
      string storageType,
      string parameterType,
      string input,
      string expected)
    {
      HbrInvariantValueParseDecision decision =
        HbrInvariantValueParser.TryNormalize(
          storageType,
          parameterType,
          input);

      Assert.True(decision.Success);
      Assert.Equal(expected, decision.NormalizedValue);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    public void Internal_yesno_raw_value_is_idempotently_reusable(string input)
    {
      HbrInvariantValueParseDecision decision =
        HbrInvariantValueParser.TryNormalize(
          "Integer",
          "YesNo",
          input,
          true);

      Assert.True(decision.Success);
      Assert.Equal(input, decision.NormalizedValue);
      Assert.Equal(string.Empty, decision.ErrorCode);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    public void External_yesno_numeric_value_remains_rejected(string input)
    {
      HbrInvariantValueParseDecision decision =
        HbrInvariantValueParser.TryNormalize(
          "Integer",
          "YesNo",
          input,
          false);

      Assert.False(decision.Success);
      Assert.Equal("INVALID_VALUE", decision.ErrorCode);
    }
  }
}
