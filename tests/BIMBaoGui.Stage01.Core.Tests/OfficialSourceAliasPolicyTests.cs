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
        " ProjectInformation ",
        " 备注 ");

      Assert.Equal(baseline, OfficialSourceAliasPolicy.CreateGuid(
        "INSTANCE",
        "ost_projectinformation",
        "projectinformation",
        "备注"));
      Assert.NotEqual(baseline, OfficialSourceAliasPolicy.CreateGuid(
        "TYPE", "OST_ProjectInformation", "ProjectInformation", "备注"));
      Assert.NotEqual(baseline, OfficialSourceAliasPolicy.CreateGuid(
        "INSTANCE", "OST_Rooms", "ProjectInformation", "备注"));
      Assert.NotEqual(baseline, OfficialSourceAliasPolicy.CreateGuid(
        "INSTANCE", "OST_ProjectInformation", "Room", "备注"));
      Assert.NotEqual(baseline, OfficialSourceAliasPolicy.CreateGuid(
        "INSTANCE", "OST_ProjectInformation", "ProjectInformation", "说明"));
    }
  }
}
