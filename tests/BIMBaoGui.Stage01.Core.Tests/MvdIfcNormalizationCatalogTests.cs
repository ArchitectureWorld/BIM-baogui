using System.Linq;
using BIMBaoGui.Stage01.Hifc;
using BIMBaoGui.Stage01.Mvd;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class MvdIfcNormalizationCatalogTests
  {
    [Fact]
    public void Catalog_joins_MVD_names_to_official_sample_names()
    {
      MvdIfcNormalizationRule x = MvdIfcNormalizationCatalog.Instance.Rules
        .Single(rule => rule.Entity == "IfcProject"
          && rule.CanonicalProperty == "基点坐标X");

      Assert.Equal("Pset_申报信息属性集", x.CanonicalPropertySet);
      Assert.Contains("申报信息属性集", x.PropertySetAliases);
      Assert.Contains("Pset_申报信息属性集", x.PropertySetAliases);
      Assert.Contains("基点坐标 X", x.PropertyAliases);
      Assert.Contains("基点坐标X", x.PropertyAliases);
      Assert.Equal("IfcReal", x.TargetType);
      Assert.Equal("m", x.Unit);
      Assert.Contains(
        "HIFC.申报信息属性集.基点坐标X",
        x.InternalAliases);
    }

    [Fact]
    public void Catalog_preserves_IfcLabel_requirement()
    {
      MvdIfcNormalizationRule projectName =
        MvdIfcNormalizationCatalog.Instance.Rules.Single(
          rule => rule.Entity == "IfcProject"
            && rule.CanonicalPropertySet == "Pset_申报信息属性集"
            && rule.CanonicalProperty == "项目名称");

      Assert.Equal("IfcLabel", projectName.TargetType);
    }

    [Fact]
    public void Catalog_includes_official_building_and_storey_rules()
    {
      MvdIfcNormalizationRule buildingName =
        MvdIfcNormalizationCatalog.Instance.Rules.Single(
          rule => rule.Entity == "IfcBuilding"
            && rule.CanonicalPropertySet == "Pset_建筑技术信息属性集"
            && rule.CanonicalProperty == "建筑名称");
      MvdIfcNormalizationRule storeyHeight =
        MvdIfcNormalizationCatalog.Instance.Rules.Single(
          rule => rule.Entity == "IfcBuildingStorey"
            && rule.CanonicalPropertySet == "Pset_建筑楼层信息属性集"
            && rule.CanonicalProperty == "建筑层高");

      Assert.Equal("IfcLabel", buildingName.TargetType);
      Assert.Equal("IfcReal", storeyHeight.TargetType);
      Assert.Equal("mm", storeyHeight.Unit);
    }

    [Fact]
    public void Catalog_covers_all_unique_official_mappings_without_drift()
    {
      OfficialHifcMapping[] mappings =
        OfficialHifcMappingCatalog.Instance.Mappings.ToArray();
      Assert.Equal(166, mappings.Length);
      Assert.Equal(
        166,
        mappings.Select(mapping =>
          mapping.IfcEntity
          + "|Pset_"
          + mapping.PropertySet
          + "|"
          + mapping.IfcProperty)
          .Distinct(System.StringComparer.OrdinalIgnoreCase)
          .Count());

      foreach (OfficialHifcMapping mapping in mappings)
      {
        MvdIfcNormalizationRule rule =
          MvdIfcNormalizationCatalog.Instance.Rules.Single(
            candidate => candidate.Entity == mapping.IfcEntity
              && candidate.CanonicalPropertySet
                == "Pset_" + mapping.PropertySet
              && candidate.CanonicalProperty == mapping.IfcProperty);
        Assert.Equal(mapping.IfcDataType, rule.TargetType);
        Assert.Equal(mapping.Unit, rule.Unit);
        Assert.Contains(mapping.ParameterName, rule.InternalAliases);
      }
    }

    [Theory]
    [InlineData("Pset_申报信息属性集", "基点坐标X")]
    [InlineData("申报信息属性集", "基点坐标 X")]
    public void TryResolve_accepts_official_and_MVD_aliases(
      string propertySet,
      string property)
    {
      bool found = MvdIfcNormalizationCatalog.Instance.TryResolve(
        "IfcProject",
        propertySet,
        property,
        out MvdIfcNormalizationRule rule);

      Assert.True(found);
      Assert.Equal("Pset_申报信息属性集", rule.CanonicalPropertySet);
      Assert.Equal("基点坐标X", rule.CanonicalProperty);
    }
  }
}
