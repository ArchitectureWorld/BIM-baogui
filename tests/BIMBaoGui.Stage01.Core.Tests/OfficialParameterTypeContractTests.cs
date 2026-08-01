using System;
using BIMBaoGui.Stage01.Hifc;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class OfficialParameterTypeContractTests
  {
    [Theory]
    [InlineData(" text ", "TEXT", "text")]
    [InlineData("integer", "INTEGER", "Integer")]
    [InlineData("yesno", "YESNO", "YesNo")]
    [InlineData("length", "LENGTH", "Length")]
    [InlineData("area", "AREA", "Area")]
    [InlineData("volume", "VOLUME", "Volume")]
    [InlineData("angle", "ANGLE", "Angle")]
    [InlineData("number", "NUMBER", "Number")]
    public void IsCompatible_NormalizesEverySupportedDeclaredType(
      string declared,
      string normalized,
      string actual)
    {
      Assert.Equal(normalized, OfficialParameterTypeContract.Normalize(declared));
      Assert.True(OfficialParameterTypeContract.IsCompatible(declared, actual));
    }

    [Fact]
    public void IsCompatible_DoesNotTreatLengthAsNumberWhenBothUseDoubleStorage()
    {
      Assert.False(OfficialParameterTypeContract.IsCompatible("LENGTH", "Number"));
    }

    [Fact]
    public void Normalize_RejectsUnknownDeclaredTypes()
    {
      Assert.Throws<InvalidOperationException>(() =>
        OfficialParameterTypeContract.Normalize("MASS"));
    }

    [Fact]
    public void IsCompatible_RejectsUnknownRevitTypes()
    {
      Assert.Throws<InvalidOperationException>(() =>
        OfficialParameterTypeContract.IsCompatible("TEXT", "Material"));
    }
  }
}
