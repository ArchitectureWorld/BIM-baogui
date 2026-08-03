using System;
using BIMBaoGui.Stage01.Stage02;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage02ConfirmationUiPolicyTests
  {
    [Theory]
    [InlineData(Stage02Codes.PreviewAlreadyConsumed)]
    [InlineData(Stage02Codes.DocumentFingerprintChanged)]
    [InlineData(Stage02Codes.ElementSetChanged)]
    [InlineData(Stage02Codes.OldValueChanged)]
    [InlineData(Stage02Codes.PreviewHashChanged)]
    [InlineData("UNKNOWN_CONFIRMATION_BLOCKER")]
    public void Stale_or_unknown_confirmation_blocker_requires_new_preview(
      string blockerCode)
    {
      Stage02ConfirmationUiDecision decision =
        Stage02ConfirmationUiPolicy.Decide(new[]
        {
          new Stage02Blocker(blockerCode, "blocked")
        });

      Assert.True(decision.RequiresNewPreview);
      Assert.DoesNotContain("未消费预览", decision.Status);
    }

    [Fact]
    public void Missing_independent_evidence_can_retry_same_unconsumed_preview()
    {
      Stage02ConfirmationUiDecision decision =
        Stage02ConfirmationUiPolicy.Decide(new[]
        {
          new Stage02Blocker(
            Stage02Codes.InvalidSelectionEvidence,
            "provide current evidence")
        });

      Assert.False(decision.RequiresNewPreview);
      Assert.Contains("预览未消费", decision.Status);
    }

    [Fact]
    public void Empty_blocker_set_is_fail_closed()
    {
      Stage02ConfirmationUiDecision decision =
        Stage02ConfirmationUiPolicy.Decide(Array.Empty<Stage02Blocker>());

      Assert.True(decision.RequiresNewPreview);
    }
  }
}
