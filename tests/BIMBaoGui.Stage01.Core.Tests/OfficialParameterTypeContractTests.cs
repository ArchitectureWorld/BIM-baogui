using System;
using BIMBaoGui.Stage01.Hifc;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class OfficialParameterTypeContractTests
  {
    [Theory]
    [InlineData(" text ", "TEXT", "text", "String", "Text", "None")]
    [InlineData("integer", "INTEGER", "Integer", "Integer", "Integer", "None")]
    [InlineData("yesno", "YESNO", "YesNo", "Integer", "YesNo", "None")]
    [InlineData("length", "LENGTH", "Length", "Double", "Double", "Meters")]
    [InlineData("area", "AREA", "Area", "Double", "Double", "SquareMeters")]
    [InlineData("volume", "VOLUME", "Volume", "Double", "Double", "CubicMeters")]
    [InlineData("angle", "ANGLE", "Angle", "Double", "Double", "Degrees")]
    [InlineData("number", "NUMBER", "Number", "Double", "Double", "None")]
    public void Resolve_NormalizesEverySupportedDeclaredTypeAndRoute(
      string declared,
      string normalized,
      string actual,
      string storageKind,
      string valueRoute,
      string unitRoute)
    {
      OfficialParameterTypeDecision decision = OfficialParameterTypeContract.Resolve(declared);

      Assert.Equal(normalized, decision.SemanticType);
      Assert.Equal(storageKind, decision.StorageKind.ToString());
      Assert.Equal(valueRoute, decision.ValueRoute.ToString());
      Assert.Equal(unitRoute, decision.UnitRoute.ToString());
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

    [Theory]
    [InlineData(null, "Text")]
    [InlineData("", "Text")]
    [InlineData("TEXT", null)]
    [InlineData("TEXT", "")]
    public void IsCompatible_RejectsNullOrBlankSemanticNames(
      string expected,
      string actual)
    {
      Assert.Throws<InvalidOperationException>(() =>
        OfficialParameterTypeContract.IsCompatible(expected, actual));
    }

    [Fact]
    public void Resolve_UsesOneDeclaredDecisionForWriteAndReadRoutes()
    {
      OfficialParameterTypeDecision write = OfficialParameterTypeContract.Resolve("AREA");
      OfficialParameterTypeDecision readback = OfficialParameterTypeContract.Resolve("AREA");

      Assert.Equal(write.ValueRoute, readback.ValueRoute);
      Assert.Equal(OfficialParameterUnitRoute.SquareMeters, write.UnitRoute);
      Assert.NotEqual(OfficialParameterUnitRoute.CubicMeters, write.UnitRoute);
    }

    [Fact]
    public void IsCompatible_DoesNotReadSemanticWhenStorageKindMismatches()
    {
      bool semanticRead = false;

      OfficialParameterCompatibilityResult result = OfficialParameterTypeContract.CheckCompatibility(
        "TEXT",
        OfficialParameterStorageKind.Double,
        () =>
        {
          semanticRead = true;
          throw new Xunit.Sdk.XunitException("语义 accessor 不应被调用。");
        });

      Assert.False(result.StorageMatches);
      Assert.False(semanticRead);
    }

    [Fact]
    public void IsCompatible_RejectsNullSemanticAccessorBeforeStorageShortCircuit()
    {
      Assert.Throws<ArgumentNullException>(() =>
        OfficialParameterTypeContract.CheckCompatibility(
          "TEXT",
          OfficialParameterStorageKind.Double,
          null));
    }
  }
}
