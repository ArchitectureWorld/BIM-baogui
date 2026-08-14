using System;
using BIMBaoGui.HifcCore;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal sealed class NativeStage03ExportGuidOwnerDecision
  {
    internal bool Success { get; set; }
    internal string OwnerEntity { get; set; } = string.Empty;
    internal string HifcOwnerStrategy { get; set; } = string.Empty;
    internal string ExportGuid { get; set; } = string.Empty;
    internal string OwnerGlobalId { get; set; } = string.Empty;
    internal string Status { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
  }

  internal static class NativeStage03ExportGuidOwnerPolicy
  {
    private const string ByExportGuid = "BY_EXPORT_GUID";

    internal static NativeStage03ExportGuidOwnerDecision Resolve(
      string ruleOwnerStrategy,
      string ownerEntity,
      Guid exportGuid)
    {
      string strategy = (ruleOwnerStrategy ?? string.Empty).Trim();
      string entity = (ownerEntity ?? string.Empty).Trim();
      if (!string.Equals(strategy, ByExportGuid, StringComparison.Ordinal))
      {
        return Failure(
          entity,
          "OWNER_STRATEGY_UNSUPPORTED",
          "仅支持 BY_EXPORT_GUID 绿地对象 owner 策略。");
      }
      if (entity.Length == 0)
      {
        return Failure(
          string.Empty,
          "OWNER_ENTITY_EMPTY",
          "IFC owner entity 不能为空。");
      }
      if (exportGuid == Guid.Empty)
      {
        return Failure(
          entity,
          "OWNER_EXPORT_GUID_EMPTY",
          "Revit ExportUtils.GetExportId 返回空 GUID。");
      }

      return new NativeStage03ExportGuidOwnerDecision
      {
        Success = true,
        OwnerEntity = entity,
        HifcOwnerStrategy = HifcOwnerStrategies.GlobalId,
        ExportGuid = exportGuid.ToString("D"),
        OwnerGlobalId = IfcGlobalId.Encode(exportGuid),
        Status = "OWNER_GUID_READY"
      };
    }

    private static NativeStage03ExportGuidOwnerDecision Failure(
      string entity,
      string status,
      string message)
    {
      return new NativeStage03ExportGuidOwnerDecision
      {
        Success = false,
        OwnerEntity = entity ?? string.Empty,
        Status = status,
        Message = message
      };
    }
  }
}
