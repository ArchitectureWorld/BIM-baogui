using System;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeRuleCatalogTests
  {
    [Fact]
    public void LoadsStage01CatalogFromTheEmbeddedAuthoritativeRulePackage()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;

      Assert.Equal("HBR-WUHAN-PLANNING", catalog.Identity.PackageId);
      Assert.Equal("1.0.0", catalog.Identity.PackageVersion);
      Assert.Equal(64, catalog.Identity.RulePackageSha256.Length);
      Assert.Equal(102, catalog.Stage01Fields.Count);
      Assert.Equal(102, catalog.Stage01FieldsByKey.Count);
      Assert.Equal(14, catalog.Conditions.Count);
      Assert.Equal(3, catalog.ModelProfiles.Count);
      Assert.Equal("01_文件与项目身份", catalog.DefaultActiveGroup);
    }

    [Fact]
    public void FreezesSpatialAxisSemanticsFromTheSharedDatabase()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      NativeSpatialMapping x = catalog.SpatialMappings.Single(value =>
        string.Equals(value.SourceName, "X", StringComparison.Ordinal));
      NativeSpatialMapping y = catalog.SpatialMappings.Single(value =>
        string.Equals(value.SourceName, "Y", StringComparison.Ordinal));

      Assert.Equal(NativeStage01Keys.BaseX, x.FieldKey);
      Assert.Equal("NorthSouth", x.TargetName);
      Assert.Equal("m", x.Unit);
      Assert.Equal(NativeStage01Keys.BaseY, y.FieldKey);
      Assert.Equal("EastWest", y.TargetName);
      Assert.Equal("m", y.Unit);
    }

    [Fact]
    public void CreatesDefaultsFromTheSharedDatabaseAndNativePayloadProtocol()
    {
      NativeStage01Model model = NativeRuleCatalog.Current.CreateDefaultStage01Model();

      Assert.Equal("总平模型", model.GetValue(NativeStage01Keys.ModelFileType));
      Assert.Equal("项目总平面报规模型", model.GetValue(NativeStage01Keys.ModelScope));
      Assert.Equal("m", model.GetValue(NativeStage01Keys.LengthUnit));
      Assert.Equal("m²", model.GetValue(NativeStage01Keys.AreaUnit));
      Assert.Equal("°", model.GetValue(NativeStage01Keys.AngleUnit));
      Assert.Equal(
        NativeStage01Canonicalizer.PayloadSchemaVersion,
        model.GetValue(NativeStage01Keys.WorkflowVersion));
      Assert.True(Guid.TryParse(
        model.GetValue(NativeStage01Keys.FileGuid),
        out Guid fileGuid));
      Assert.NotEqual(Guid.Empty, fileGuid);
      Assert.Equal(14, model.Conditions.Count);
      Assert.Single(model.Organizations);
    }
  }
}
