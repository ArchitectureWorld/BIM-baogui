using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage02;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02ManualRoleCatalogTests
  {
    [Fact]
    public void EmbeddedRulePackContainsApprovedGreenManualRole()
    {
      NativeStage02ManualRoleCatalog catalog =
        NativeStage02ManualRoleCatalog.Current;

      NativeStage02ManualRoleContract green =
        Assert.Single(catalog.Roles.Where(value =>
          value.RoleId == "SITE_GREEN_OBJECT"));
      Assert.Equal("绿地", green.DisplayName);
      Assert.Equal(new[] { "总平模型" }, green.ModelFileTypes);
      Assert.Equal("site.green", green.ConditionId);
      Assert.True(green.HasPropertyTemplate);
      Assert.Equal("BY_EXPORT_GUID", green.IfcOwnerStrategy);
      NativeStage02ManualCarrierDefinition carrier = Assert.Single(
        green.ManualCarriers);
      Assert.Equal("OST_BuildingPad", carrier.Category);
      Assert.Equal(new[] { "BuildingPad" }, carrier.ElementKinds);
    }

    [Fact]
    public void GreenRoleCannotEnterAutomaticRoleInventory()
    {
      NativeCarrierRoleDefinition runtimeRole = NativeStage02RuleCatalog.Current
        .CarrierRolesById["SITE_GREEN_OBJECT"];

      Assert.Empty(runtimeRole.RevitCategories);
      Assert.Empty(runtimeRole.AllowedElementKinds);
      Assert.Equal("MANUAL_SEMANTIC_ASSIGNMENT", runtimeRole.SelectionPolicy);
      Assert.DoesNotContain(
        "OST_BuildingPad",
        NativeStage02RuleCatalog.Current.AllRevitCategories);
    }

    [Fact]
    public void GreenRoleExposesFourObjectLevelProperties()
    {
      var properties = NativeStage02RuleCatalog.Current
        .PropertiesForRole("SITE_GREEN_OBJECT")
        .OrderBy(value => value.ParameterName, StringComparer.Ordinal)
        .ToArray();

      Assert.Equal(4, properties.Length);
      Assert.Equal(
        new[]
        {
          "HBR｜绿地对象属性集｜分类名称",
          "HBR｜绿地对象属性集｜投影面积",
          "HBR｜绿地对象属性集｜折算系数",
          "HBR｜绿地对象属性集｜绿地类型"
        }.OrderBy(value => value, StringComparer.Ordinal),
        properties.Select(value => value.ParameterName));
      Assert.All(properties, value =>
      {
        Assert.Equal("CONDITIONAL", value.RequirementLevel);
        Assert.Equal("site.green", value.ConditionId);
        Assert.Equal("BY_EXPORT_GUID", value.OwnerStrategy);
      });
    }

    [Fact]
    public void AvailableRolesRespectModelProfileAndStage01Condition()
    {
      NativeStage02ManualRoleCatalog catalog =
        NativeStage02ManualRoleCatalog.Current;

      Assert.Empty(catalog.AvailableRoles(
        "总平模型",
        new Dictionary<string, bool> { ["site.green"] = false }));
      Assert.Single(catalog.AvailableRoles(
        "总平模型",
        new Dictionary<string, bool> { ["site.green"] = true }));
      Assert.Empty(catalog.AvailableRoles(
        "单体建筑—地上",
        new Dictionary<string, bool> { ["site.green"] = true }));
    }
  }
}
