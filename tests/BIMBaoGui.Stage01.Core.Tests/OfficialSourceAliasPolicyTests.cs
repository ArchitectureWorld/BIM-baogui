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
        " 备注 ");

      Assert.Equal(baseline, OfficialSourceAliasPolicy.CreateGuid(
        "INSTANCE",
        "ost_projectinformation",
        "project_information",
        "备注"));
      Assert.NotEqual(baseline, OfficialSourceAliasPolicy.CreateGuid(
        "TYPE", "OST_ProjectInformation", "PROJECT_INFORMATION", "备注"));
      Assert.NotEqual(baseline, OfficialSourceAliasPolicy.CreateGuid(
        "INSTANCE", "OST_Rooms", "PROJECT_INFORMATION", "备注"));
      Assert.NotEqual(baseline, OfficialSourceAliasPolicy.CreateGuid(
        "INSTANCE", "OST_ProjectInformation", "ROOM", "备注"));
      Assert.NotEqual(baseline, OfficialSourceAliasPolicy.CreateGuid(
        "INSTANCE", "OST_ProjectInformation", "PROJECT_INFORMATION", "说明"));

      Assert.Equal(
        new Guid("99d2e51c-a1b7-5bd5-8757-d757305ded16"),
        baseline);
    }

    [Fact]
    public void CreateGuid_PreservesGoldenBuildingCodeAlias()
    {
      Assert.Equal(
        new Guid("ffed4846-f6c9-58f3-bc99-003346a49344"),
        OfficialSourceAliasPolicy.CreateGuid(
          "INSTANCE",
          "OST_ProjectInformation",
          "PROJECT_INFORMATION",
          "建筑物编码"));
    }
  }
}
