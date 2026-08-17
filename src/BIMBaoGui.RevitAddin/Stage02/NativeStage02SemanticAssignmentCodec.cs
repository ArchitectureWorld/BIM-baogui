using System;
using System.Linq;
using System.Web.Script.Serialization;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02SemanticAssignmentCodec
  {
    internal static NativeStage02SemanticAssignmentPayload Parse(string json)
    {
      if (string.IsNullOrWhiteSpace(json)) return null;
      var serializer = new JavaScriptSerializer();
      PayloadDto dto;
      try
      {
        dto = serializer.Deserialize<PayloadDto>(json);
      }
      catch
      {
        return null;
      }
      if (dto == null) return null;
      return new NativeStage02SemanticAssignmentPayload
      {
        SchemaVersion = dto.schemaVersion ?? string.Empty,
        RulePackageId = dto.rulePackageId ?? string.Empty,
        RulePackageVersion = dto.rulePackageVersion ?? string.Empty,
        Assignments = (dto.assignments ?? Array.Empty<AssignmentDto>())
          .Where(value => value != null)
          .Select(value => new NativeStage02SemanticAssignmentRecord
          {
            ElementUniqueId = value.elementUniqueId ?? string.Empty,
            RoleId = value.roleId ?? string.Empty,
            AssignmentMode = string.Equals(
              value.assignmentMode,
              "MANUAL",
              StringComparison.OrdinalIgnoreCase)
              ? NativeStage02AssignmentMode.Manual
              : NativeStage02AssignmentMode.Auto,
            CarrierCategory = value.carrierCategory ?? string.Empty,
            CarrierElementKind = value.carrierElementKind ?? string.Empty,
            RulePackageSha256 = value.rulePackageSha256 ?? string.Empty,
            ElementSnapshotHash = value.elementSnapshotHash ?? string.Empty,
            ConfirmedUtc = value.confirmedUtc ?? string.Empty
          })
          .ToArray()
      };
    }

    private sealed class PayloadDto
    {
      public string schemaVersion { get; set; }
      public string rulePackageId { get; set; }
      public string rulePackageVersion { get; set; }
      public AssignmentDto[] assignments { get; set; }
    }

    private sealed class AssignmentDto
    {
      public string elementUniqueId { get; set; }
      public string roleId { get; set; }
      public string assignmentMode { get; set; }
      public string carrierCategory { get; set; }
      public string carrierElementKind { get; set; }
      public string rulePackageSha256 { get; set; }
      public string elementSnapshotHash { get; set; }
      public string confirmedUtc { get; set; }
    }
  }
}
