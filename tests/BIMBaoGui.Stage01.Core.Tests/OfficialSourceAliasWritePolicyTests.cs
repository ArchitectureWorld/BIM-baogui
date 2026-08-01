using System;
using System.Linq;
using BIMBaoGui.Stage01.Hifc;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class OfficialSourceAliasWritePolicyTests
  {
    [Fact]
    public void Fold_KeepsAliasesOnDifferentTargets()
    {
      OfficialSourceAliasWrite<string>[] folded = OfficialSourceAliasWritePolicy.Fold(
        Alias(1, "first", "same"),
        Alias(2, "second", "same"))
        .ToArray();

      Assert.Equal(2, folded.Length);
      Assert.Equal(new[] { "first", "second" }, folded.Select(item => item.Item));
    }

    [Fact]
    public void Fold_CollapsesEqualValuesForTheSameTargetAndGuid()
    {
      OfficialSourceAliasWrite<string>[] folded = OfficialSourceAliasWritePolicy.Fold(
        Alias(1, "first", "same", "PsetA", "属性A"),
        Alias(1, "second", "same", "PsetB", "属性B"))
        .ToArray();

      Assert.Single(folded);
      Assert.Equal("first", folded[0].Item);
    }

    [Theory]
    [InlineData("same", "SAME")]
    [InlineData("same", " same ")]
    public void Fold_RejectsOrdinallyDifferentValuesAndReportsEveryProperty(
      string firstValue,
      string secondValue)
    {
      InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
        OfficialSourceAliasWritePolicy.Fold(
          Alias(1, "first", firstValue, "PsetA", "属性A"),
          Alias(1, "second", secondValue, "PsetB", "属性B"))
          .ToArray());

      Assert.Contains("OFFICIAL_SOURCE_VALUE_CONFLICT", error.Message);
      Assert.Contains("PsetA.属性A", error.Message);
      Assert.Contains("PsetB.属性B", error.Message);
    }

    [Fact]
    public void Fold_ValidatesTheEntireAliasGroupBeforeSelectingOneWrite()
    {
      InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
        OfficialSourceAliasWritePolicy.Fold(
          Alias(1, "first", "same", "PsetA", "属性A"),
          Alias(1, "second", "same", "PsetB", "属性B"),
          Alias(1, "third", "different", "PsetC", "属性C"))
          .ToArray());

      Assert.Contains("PsetA.属性A", error.Message);
      Assert.Contains("PsetB.属性B", error.Message);
      Assert.Contains("PsetC.属性C", error.Message);
    }

    private static OfficialSourceAliasWrite<string> Alias(
      int targetId,
      string item,
      string value,
      string propertySet = "Pset",
      string property = "属性")
    {
      return new OfficialSourceAliasWrite<string>
      {
        TargetElementId = targetId,
        AliasGuid = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Item = item,
        RawValue = value,
        OfficialSourceName = "备注",
        PropertySet = propertySet,
        IfcProperty = property
      };
    }
  }
}
