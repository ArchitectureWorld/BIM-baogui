using System;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02RuleCatalogTests
  {
    [Fact]
    public void LoadsAllCarrierRolesAndPropertiesFromTheEmbeddedDatabase()
    {
      NativeStage02RuleCatalog catalog = NativeStage02RuleCatalog.Current;

      Assert.Equal("HBR-WUHAN-PLANNING", catalog.Identity.PackageId);
      Assert.Equal("1.0.0", catalog.Identity.PackageVersion);
      Assert.Equal(64, catalog.Identity.RulePackageSha256.Length);
      Assert.Equal(14, catalog.CarrierRoles.Count);
      Assert.Equal(14, catalog.CarrierRolesById.Count);
      Assert.Equal(359, catalog.Properties.Count);
      Assert.Equal(359, catalog.PropertiesById.Count);
      Assert.Equal(359, catalog.PropertiesByParameterGuid.Count);
      Assert.Equal(57, catalog.Properties.Count(value =>
        value.RuntimeDecision.Status == NativeRuntimeStatuses.NotImplemented));
      Assert.Equal(302, catalog.Properties.Count(value =>
        value.RuntimeDecision.Status
          == NativeRuntimeStatuses.UnclassifiedRequirement));
    }

    [Fact]
    public void FreezesRepresentativeRevitCarrierContracts()
    {
      NativeStage02RuleCatalog catalog = NativeStage02RuleCatalog.Current;

      NativeCarrierRoleDefinition project = catalog.CarrierRolesById["PROJECT"];
      Assert.Equal("IfcProject", project.IfcEntity);
      Assert.Contains("OST_ProjectInformation", project.RevitCategories);
      Assert.Contains("ProjectInformation", project.AllowedElementKinds);

      NativeCarrierRoleDefinition door = catalog.CarrierRolesById["DOOR"];
      Assert.Equal("IfcDoor", door.IfcEntity);
      Assert.Contains("OST_Doors", door.RevitCategories);
      Assert.Contains("FamilyInstance", door.AllowedElementKinds);

      NativeCarrierRoleDefinition organization =
        catalog.CarrierRolesById["ORGANIZATION"];
      Assert.Equal(
        "USER_SELECTED_EXPORTABLE_GENERIC_MODEL",
        organization.SelectionPolicy);
      Assert.Contains("OST_GenericModel", organization.RevitCategories);
    }

    [Fact]
    public void EveryStage02PropertyRetainsExactIfcAndRevitIdentity()
    {
      NativeStage02RuleCatalog catalog = NativeStage02RuleCatalog.Current;

      foreach (NativeStage02PropertyDefinition property in catalog.Properties)
      {
        Assert.NotEqual(Guid.Empty, property.ParameterGuid);
        Assert.False(string.IsNullOrWhiteSpace(property.PropertyId));
        Assert.False(string.IsNullOrWhiteSpace(property.IfcEntity));
        Assert.False(string.IsNullOrWhiteSpace(property.IfcPropertySet));
        Assert.False(string.IsNullOrWhiteSpace(property.IfcProperty));
        Assert.False(string.IsNullOrWhiteSpace(property.DeclaredIfcType));
        Assert.False(string.IsNullOrWhiteSpace(property.ParameterName));
        Assert.False(string.IsNullOrWhiteSpace(property.StorageType));
        Assert.False(string.IsNullOrWhiteSpace(property.ParameterType));
        Assert.NotEmpty(property.CarrierRoleIds);
        Assert.Contains("STAGE02", property.StageOwnership);
      }
    }
  }
}
