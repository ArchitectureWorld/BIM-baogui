using System;
using System.IO;

namespace BIMBaoGui.Stage01.Rules
{
  public static class HbrRuntimeStatuses
  {
    public const string Supported = "SUPPORTED";
    public const string NotImplemented = "NOT_IMPLEMENTED";
    public const string UnclassifiedRequirement =
      "UNCLASSIFIED_REQUIREMENT";
    public const string OfficialEvidenceOnly = "OFFICIAL_EVIDENCE_ONLY";
  }

  public static class HbrRuntimeReasonCodes
  {
    public const string Supported = "SUPPORTED";
    public const string OwnerStrategyNotImplemented =
      "OWNER_STRATEGY_NOT_IMPLEMENTED";
    public const string RequirementLevelUnclassified =
      "REQUIREMENT_LEVEL_UNCLASSIFIED";
    public const string OfficialEvidenceOnly = "OFFICIAL_EVIDENCE_ONLY";
  }

  public sealed class HbrRuntimeStatusDecision
  {
    internal HbrRuntimeStatusDecision(
      string status,
      string reasonCode,
      string reason)
    {
      if (string.IsNullOrWhiteSpace(status)
        || string.IsNullOrWhiteSpace(reasonCode)
        || string.IsNullOrWhiteSpace(reason))
        throw new InvalidDataException(
          "HBR runtime status decision must be complete.");
      Status = status;
      ReasonCode = reasonCode;
      Reason = reason;
    }

    public string Status { get; }
    public string ReasonCode { get; }
    public string Reason { get; }
  }
}
