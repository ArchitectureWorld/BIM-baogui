using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.Stage01.Hifc
{
  internal sealed class Stage01OfficialCompatibilityDecision
  {
    public Stage01OfficialCompatibilityDecision(
      IReadOnlyList<string> blockers)
    {
      Blockers = blockers ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Blockers { get; }
    public bool IsCompatible => Blockers.Count == 0;
  }

  internal static class Stage01OfficialCompatibilityPolicy
  {
    internal const string PendingOrganizationContractBlocker =
      "BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT：IfcOrganization 的官方 Revit 写入/导出协议尚未确认；"
      + "组织数据已保存在 HBR 初始化载荷中，但不伪装成 IfcProject 参数。";

    public static Stage01OfficialCompatibilityDecision Evaluate(
      IEnumerable<Dictionary<string, string>> organizations)
    {
      bool hasOrganizationData = (organizations
        ?? Array.Empty<Dictionary<string, string>>())
        .Any(record => record != null
          && record.Values.Any(value => !string.IsNullOrWhiteSpace(value)));
      return new Stage01OfficialCompatibilityDecision(
        hasOrganizationData
          ? new[] { PendingOrganizationContractBlocker }
          : Array.Empty<string>());
    }
  }
}
