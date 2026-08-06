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
    public void Evaluate_NonEmptyOrganizationValueDoesNotBlockThreeStageWorkflow()
    {
      Stage01OfficialCompatibilityDecision decision =
        Stage01OfficialCompatibilityPolicy.Evaluate(
          new[]
          {
            new Dictionary<string, string> { ["name"] = "测试单位" },
            new Dictionary<string, string> { ["code"] = "ORG-001" }
          });

      Assert.True(decision.IsCompatible);
      Assert.Empty(decision.Blockers);
    }
  }
}
