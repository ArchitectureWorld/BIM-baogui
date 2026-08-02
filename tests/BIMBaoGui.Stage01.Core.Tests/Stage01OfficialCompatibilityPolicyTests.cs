using System.Collections.Generic;
using BIMBaoGui.Stage01.Hifc;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage01OfficialCompatibilityPolicyTests
  {
    [Fact]
    public void Evaluate_EmptyOrganizationRecordsAreCompatible()
    {
      Stage01OfficialCompatibilityDecision decision =
        Stage01OfficialCompatibilityPolicy.Evaluate(
          new[]
          {
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["name"] = " " }
          });

      Assert.True(decision.IsCompatible);
      Assert.Empty(decision.Blockers);
    }

    [Fact]
    public void Evaluate_NonEmptyOrganizationValueReturnsDeterministicBlocker()
    {
      Stage01OfficialCompatibilityDecision decision =
        Stage01OfficialCompatibilityPolicy.Evaluate(
          new[]
          {
            new Dictionary<string, string> { ["name"] = "测试单位" },
            new Dictionary<string, string> { ["code"] = "ORG-001" }
          });

      Assert.False(decision.IsCompatible);
      Assert.Equal(
        new[]
        {
          "BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT：IfcOrganization 的官方 Revit 写入/导出协议尚未确认；组织数据已保存在 HBR 初始化载荷中，但不伪装成 IfcProject 参数。"
        },
        decision.Blockers);
    }
  }
}
