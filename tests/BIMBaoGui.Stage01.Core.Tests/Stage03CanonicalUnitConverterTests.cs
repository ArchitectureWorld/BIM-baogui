using System;
using System.Globalization;
using System.Threading;
using BIMBaoGui.Stage01.Revit.Parameters;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage03CanonicalUnitConverterTests
  {
    [Theory]
    [InlineData("LENGTH", "m", 1d, 0.3048d)]
    [InlineData("LENGTH", "mm", 1d, 304.8d)]
    [InlineData("AREA", "m2", 1d, 0.09290304d)]
    [InlineData("VOLUME", "m3", 1d, 0.028316846592d)]
    [InlineData("ANGLE", "deg", Math.PI, 180d)]
    [InlineData("NUMBER", null, 1234.5d, 1234.5d)]
    public void Internal_Revit_values_convert_to_canonical_external_units(
      string parameterType,
      string canonicalUnit,
      double internalValue,
      double expected)
    {
      HbrCanonicalUnitConversionDecision decision =
        HbrCanonicalUnitConverter.TryFromInternalDouble(
          parameterType,
          canonicalUnit,
          internalValue);

      Assert.True(decision.Success, decision.Message);
      double actual = double.Parse(
        decision.Value,
        NumberStyles.Float,
        CultureInfo.InvariantCulture);
      Assert.Equal(expected, actual, 12);
      Assert.DoesNotContain(",", decision.Value);
    }

    [Theory]
    [InlineData("LENGTH", "m", 12.3456789d)]
    [InlineData("LENGTH", "mm", 12345.6789d)]
    [InlineData("AREA", "m2", 87.654321d)]
    [InlineData("VOLUME", "m3", 5.125d)]
    [InlineData("ANGLE", "deg", 17.75d)]
    [InlineData("NUMBER", null, 0.12345678901234566d)]
    public void External_and_internal_conversion_round_trip_invariantly(
      string parameterType,
      string canonicalUnit,
      double externalValue)
    {
      HbrCanonicalUnitConversionDecision internalDecision =
        HbrCanonicalUnitConverter.TryToInternalDouble(
          parameterType,
          canonicalUnit,
          externalValue);
      Assert.True(internalDecision.Success, internalDecision.Message);

      HbrCanonicalUnitConversionDecision canonicalDecision =
        HbrCanonicalUnitConverter.TryFromInternalDouble(
          parameterType,
          canonicalUnit,
          double.Parse(
            internalDecision.Value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture));

      Assert.True(canonicalDecision.Success, canonicalDecision.Message);
      Assert.Equal(
        externalValue,
        double.Parse(
          canonicalDecision.Value,
          NumberStyles.Float,
          CultureInfo.InvariantCulture),
        12);
    }

    [Fact]
    public void Formatting_is_not_affected_by_current_culture()
    {
      CultureInfo originalCulture = Thread.CurrentThread.CurrentCulture;
      try
      {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

        HbrCanonicalUnitConversionDecision decision =
          HbrCanonicalUnitConverter.TryFromInternalDouble(
            "NUMBER",
            null,
            1234.5d);

        Assert.True(decision.Success, decision.Message);
        Assert.Equal("1234.5", decision.Value);
      }
      finally
      {
        Thread.CurrentThread.CurrentCulture = originalCulture;
      }
    }

    [Theory]
    [InlineData("LENGTH", "cm", 1d)]
    [InlineData("AREA", "m", 1d)]
    [InlineData("UNKNOWN", null, 1d)]
    [InlineData("NUMBER", null, double.NaN)]
    [InlineData("NUMBER", null, double.PositiveInfinity)]
    public void Unsupported_or_nonfinite_values_fail_closed(
      string parameterType,
      string canonicalUnit,
      double value)
    {
      HbrCanonicalUnitConversionDecision decision =
        HbrCanonicalUnitConverter.TryFromInternalDouble(
          parameterType,
          canonicalUnit,
          value);

      Assert.False(decision.Success);
      Assert.Equal("INVALID_VALUE", decision.ErrorCode);
      Assert.NotEmpty(decision.Message);
    }

    [Theory]
    [InlineData("YESNO", 1, ".T.")]
    [InlineData("YESNO", 0, ".F.")]
    [InlineData("INTEGER", 17, "17")]
    [InlineData("INTEGER", -4, "-4")]
    public void Internal_integer_values_use_the_declared_canonical_contract(
      string parameterType,
      int internalValue,
      string expected)
    {
      HbrCanonicalUnitConversionDecision decision =
        HbrCanonicalUnitConverter.TryFromInternalInteger(
          parameterType,
          internalValue);

      Assert.True(decision.Success, decision.Message);
      Assert.Equal(expected, decision.Value);
    }

    [Theory]
    [InlineData("YESNO", -1)]
    [InlineData("YESNO", 2)]
    [InlineData("UNKNOWN", 1)]
    public void Invalid_integer_contracts_fail_closed(
      string parameterType,
      int internalValue)
    {
      HbrCanonicalUnitConversionDecision decision =
        HbrCanonicalUnitConverter.TryFromInternalInteger(
          parameterType,
          internalValue);

      Assert.False(decision.Success);
      Assert.Equal("INVALID_VALUE", decision.ErrorCode);
    }
  }
}
