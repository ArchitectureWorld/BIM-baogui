using System;
using BIMBaoGui.Stage01.Hifc;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class OfficialSourceAliasPolicyTests
  {
    [Fact]
    public void CreateGuid_UsesOnlyTheSameRevitCarrierIdentity()
    {
      Guid baseline = OfficialSourceAliasPolicy.CreateGuid(
        " instance ",
        " OST_ProjectInformation ",
        " PROJECT_INFORMATION ",
        " 材质和装饰 ",
        " 备注 ",
        " TEXT ");

      Assert.Equal(baseline, OfficialSourceAliasPolicy.CreateGuid(
        "INSTANCE",
        "ost_projectinformation",
        "project_information",
        "材质和装饰",
        "备注",
        "text"));
      Assert.NotEqual(baseline, OfficialSourceAliasPolicy.CreateGuid(
        "TYPE", "OST_ProjectInformation", "PROJECT_INFORMATION", "材质和装饰", "备注", "TEXT"));
      Assert.NotEqual(baseline, OfficialSourceAliasPolicy.CreateGuid(
        "INSTANCE", "OST_Rooms", "PROJECT_INFORMATION", "材质和装饰", "备注", "TEXT"));
      Assert.NotEqual(baseline, OfficialSourceAliasPolicy.CreateGuid(
        "INSTANCE", "OST_ProjectInformation", "ROOM", "材质和装饰", "备注", "TEXT"));
      Assert.NotEqual(baseline, OfficialSourceAliasPolicy.CreateGuid(
        "INSTANCE", "OST_ProjectInformation", "PROJECT_INFORMATION", "阶段化", "备注", "TEXT"));
      Assert.NotEqual(baseline, OfficialSourceAliasPolicy.CreateGuid(
        "INSTANCE", "OST_ProjectInformation", "PROJECT_INFORMATION", "材质和装饰", "说明", "TEXT"));
      Assert.NotEqual(baseline, OfficialSourceAliasPolicy.CreateGuid(
        "INSTANCE", "OST_ProjectInformation", "PROJECT_INFORMATION", "材质和装饰", "备注", "NUMBER"));

      Assert.Equal(
        new Guid("99d2e51c-a1b7-5bd5-8757-d757305ded16"),
        OfficialSourceAliasPolicy.CreateLegacyGuid(
          "INSTANCE",
          "OST_ProjectInformation",
          "PROJECT_INFORMATION",
          "备注"));
      Assert.NotEqual(
        OfficialSourceAliasPolicy.CreateLegacyGuid(
          "INSTANCE",
          "OST_ProjectInformation",
          "PROJECT_INFORMATION",
          "备注"),
        baseline);
    }

    [Fact]
    public void CreateLegacyGuid_PreservesGoldenBuildingCodeAlias()
    {
      Assert.Equal(
        new Guid("ffed4846-f6c9-58f3-bc99-003346a49344"),
        OfficialSourceAliasPolicy.CreateLegacyGuid(
          "INSTANCE",
          "OST_ProjectInformation",
          "PROJECT_INFORMATION",
          "建筑物编码"));
    }
  }
}
