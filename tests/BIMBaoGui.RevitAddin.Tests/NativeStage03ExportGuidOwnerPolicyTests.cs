using System;
using BIMBaoGui.HifcCore;
using BIMBaoGui.RevitAddin.Stage03;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage03ExportGuidOwnerPolicyTests
  {
    [Fact]
    public void By_export_guid_maps_to_global_id_owner_contract()
    {
      Guid exportGuid = Guid.Parse(
        "00112233-4455-6677-8899-aabbccddeeff");

      NativeStage03ExportGuidOwnerDecision decision =
        NativeStage03ExportGuidOwnerPolicy.Resolve(
          "BY_EXPORT_GUID",
          "IfcSlab",
          exportGuid);

      Assert.True(decision.Success);
      Assert.Equal("IfcSlab", decision.OwnerEntity);
      Assert.Equal(HifcOwnerStrategies.GlobalId, decision.HifcOwnerStrategy);
      Assert.Equal(exportGuid.ToString("D"), decision.ExportGuid);
      Assert.Equal(IfcGlobalId.Encode(exportGuid), decision.OwnerGlobalId);
      Assert.Equal("OWNER_GUID_READY", decision.Status);
    }

    [Fact]
    public void Empty_export_guid_is_rejected_without_fallback()
    {
      NativeStage03ExportGuidOwnerDecision decision =
        NativeStage03ExportGuidOwnerPolicy.Resolve(
          "BY_EXPORT_GUID",
          "IfcSlab",
          Guid.Empty);

      Assert.False(decision.Success);
      Assert.Equal("OWNER_EXPORT_GUID_EMPTY", decision.Status);
      Assert.Equal(string.Empty, decision.OwnerGlobalId);
      Assert.Equal(string.Empty, decision.HifcOwnerStrategy);
    }

    [Fact]
    public void Unsupported_owner_strategy_is_rejected_without_type_fallback()
    {
      NativeStage03ExportGuidOwnerDecision decision =
        NativeStage03ExportGuidOwnerPolicy.Resolve(
          HifcOwnerStrategies.SingleEntityByType,
          "IfcSlab",
          Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));

      Assert.False(decision.Success);
      Assert.Equal("OWNER_STRATEGY_UNSUPPORTED", decision.Status);
      Assert.Equal(string.Empty, decision.OwnerGlobalId);
      Assert.Equal(string.Empty, decision.HifcOwnerStrategy);
    }

    [Fact]
    public void Empty_owner_entity_is_rejected_before_guid_projection()
    {
      NativeStage03ExportGuidOwnerDecision decision =
        NativeStage03ExportGuidOwnerPolicy.Resolve(
          "BY_EXPORT_GUID",
          " ",
          Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));

      Assert.False(decision.Success);
      Assert.Equal("OWNER_ENTITY_EMPTY", decision.Status);
      Assert.Equal(string.Empty, decision.OwnerGlobalId);
      Assert.Equal(string.Empty, decision.HifcOwnerStrategy);
    }
  }
}
