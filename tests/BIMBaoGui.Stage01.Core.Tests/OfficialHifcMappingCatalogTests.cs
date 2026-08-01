using System;
using System.Linq;
using BIMBaoGui.Stage01.Hifc;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class OfficialHifcMappingCatalogTests
  {
    [Fact]
    public void Instance_LoadsAllEmbeddedMappingsWithoutTypeInitializationFailure()
    {
      Exception captured = Record.Exception(() =>
      {
        OfficialHifcMappingCatalog catalog = OfficialHifcMappingCatalog.Instance;
        Assert.Equal(166, catalog.Mappings.Count);
        Assert.All(catalog.Mappings, mapping =>
        {
          Assert.False(string.IsNullOrWhiteSpace(mapping.PropertyId));
          Assert.False(string.IsNullOrWhiteSpace(mapping.ParameterName));
          Assert.False(string.IsNullOrWhiteSpace(mapping.IfcEntity));
          Assert.False(string.IsNullOrWhiteSpace(mapping.PropertySet));
          Assert.False(string.IsNullOrWhiteSpace(mapping.IfcProperty));
          Assert.False(string.IsNullOrWhiteSpace(mapping.SharedParameterType));
          Assert.False(string.IsNullOrWhiteSpace(mapping.OfficialSourceParameterName));
          Assert.NotEqual(Guid.Empty, mapping.ParameterGuid);
          Assert.NotEqual(Guid.Empty, mapping.OfficialSourceParameterGuid);
        });
      });

      if (captured is TypeInitializationException initializer
        && initializer.InnerException != null)
      {
        throw new Xunit.Sdk.XunitException(
          "OfficialHifcMappingCatalog static initialization failed: "
          + Flatten(initializer));
      }

      Assert.Null(captured);
    }

    [Fact]
    public void CategorylessMappings_AreRetainedButBlockedFromRevitWrites()
    {
      OfficialHifcMapping[] categoryless = OfficialHifcMappingCatalog.Instance
        .Mappings
        .Where(mapping => string.IsNullOrWhiteSpace(mapping.Category))
        .ToArray();

      Assert.NotEmpty(categoryless);
      Assert.All(categoryless, mapping => Assert.True(
        mapping.EntityPolicy.IsBlocked,
        mapping.IfcEntity + " must be blocked when no Revit category is defined."));
    }

    [Fact]
    public void Stage01ProjectName_ResolvesFromCanonicalFieldKey()
    {
      bool resolved = OfficialHifcMappingCatalog.Instance.TryResolveStage01FieldKey(
        "IfcProject|Pset_申报信息属性集|项目名称",
        out OfficialHifcMapping mapping);

      Assert.True(resolved);
      Assert.Equal("IfcProject", mapping.IfcEntity);
      Assert.Equal("申报信息属性集", mapping.PropertySet);
      Assert.Equal("项目名称", mapping.IfcProperty);
      Assert.Equal("项目名称", mapping.OfficialSourceParameterName);
    }

    [Fact]
    public void ProjectCarrierAliases_ShareOnlyTheSameOfficialSourceName()
    {
      OfficialHifcMappingCatalog catalog = OfficialHifcMappingCatalog.Instance;
      Assert.True(catalog.TryResolve(
        "HIFC.区划信息属性集.备注", out OfficialHifcMapping zoningRemark));
      Assert.True(catalog.TryResolve(
        "HIFC.申报信息属性集.备注", out OfficialHifcMapping filingRemark));
      Assert.True(catalog.TryResolve(
        "HIFC.地籍信息属性集.建筑物编码", out OfficialHifcMapping cadastralCode));
      Assert.True(catalog.TryResolve(
        "HIFC.登记信息属性集.建筑物编码", out OfficialHifcMapping registrationCode));

      Assert.Equal(
        zoningRemark.OfficialSourceParameterGuid,
        filingRemark.OfficialSourceParameterGuid);
      Assert.Equal(
        cadastralCode.OfficialSourceParameterGuid,
        registrationCode.OfficialSourceParameterGuid);
      Assert.NotEqual(
        zoningRemark.OfficialSourceParameterGuid,
        cadastralCode.OfficialSourceParameterGuid);
      Assert.NotEqual(zoningRemark.ParameterGuid, filingRemark.ParameterGuid);
      Assert.NotEqual(cadastralCode.ParameterGuid, registrationCode.ParameterGuid);
    }

    private static string Flatten(Exception exception)
    {
      return string.Join(
        " --> ",
        Generate(exception).Select(item =>
          item.GetType().FullName + ": " + item.Message));
    }

    private static System.Collections.Generic.IEnumerable<Exception> Generate(
      Exception exception)
    {
      for (Exception current = exception;
        current != null;
        current = current.InnerException)
        yield return current;
    }
  }
}
