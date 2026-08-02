using System;
using BIMBaoGui.Stage01.Hifc;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class OfficialSourceParameterTypePolicyTests
  {
    [Theory]
    [InlineData("IfcReal", "NUMBER")]
    [InlineData("IfcInteger", "INTEGER")]
    [InlineData("IfcBoolean", "TEXT")]
    [InlineData("IfcText", "TEXT")]
    [InlineData("IfcLabel", "TEXT")]
    public void Resolve_UsesUnitNeutralRevitStorageForOfficialExporterValues(
      string ifcDataType,
      string expected)
    {
      Assert.Equal(expected, OfficialSourceParameterTypePolicy.Resolve(ifcDataType));
    }

    [Fact]
    public void Resolve_RejectsUnknownIfcDataTypes()
    {
      Assert.Throws<InvalidOperationException>(() =>
        OfficialSourceParameterTypePolicy.Resolve("IfcUnsupported"));
    }
  }
}
