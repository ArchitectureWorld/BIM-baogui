using BIMBaoGui.Stage01.Stage02;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage02PreConsumptionUiPolicyTests
  {
    [Theory]
    [InlineData("缺少 currentSelectionEvidence")]
    [InlineData("SelectionMode mismatch")]
    [InlineData("DocumentFingerprint mismatch")]
    public void Invalid_selection_is_normal_retryable_rejection_without_report(
      string message)
    {
      var exception = new Stage02ContractException(
        Stage02Codes.InvalidSelectionEvidence,
        message);

      Stage02PreConsumptionUiDecision decision =
        Stage02PreConsumptionUiPolicy.Decide(exception, false);

      Assert.True(decision.Handled);
      Assert.False(decision.ShouldWriteFailureReport);
      Assert.False(decision.RequiresNewPreview);
      Assert.Contains("预览未消费", decision.Status);
      Stage02Blocker blocker = Assert.Single(decision.Blockers);
      Assert.Equal(Stage02Codes.InvalidSelectionEvidence, blocker.Code);
      Assert.Equal(message, blocker.Message);
    }

    [Theory]
    [InlineData(Stage02Codes.FileContextChanged)]
    [InlineData(Stage02Codes.RulePackageIdentityMismatch)]
    [InlineData(Stage02Codes.DocumentFingerprintChanged)]
    [InlineData(Stage02Codes.ElementSetChanged)]
    [InlineData(Stage02Codes.RoleSnapshotChanged)]
    [InlineData(Stage02Codes.AmbiguousCarrier)]
    [InlineData("FUTURE_DOMAIN_DRIFT")]
    public void Unconsumed_domain_drift_requires_preview_without_report(
      string code)
    {
      var exception = new Stage02ContractException(code, "domain drift");
      int reportCalls = 0;
      int consumeCalls = 0;

      Stage02PreConsumptionUiDecision decision =
        Stage02PreConsumptionUiPolicy.Decide(exception, false);
      if (decision.ShouldWriteFailureReport) reportCalls++;
      if (!decision.Handled) consumeCalls++;

      Assert.True(decision.Handled);
      Assert.False(decision.ShouldWriteFailureReport);
      Assert.True(decision.RequiresNewPreview);
      Assert.Contains("必须重新生成预览", decision.Status);
      Stage02Blocker blocker = Assert.Single(decision.Blockers);
      Assert.Equal(code, blocker.Code);
      Assert.Equal(0, reportCalls);
      Assert.Equal(0, consumeCalls);
    }

    [Theory]
    [InlineData(Stage02Codes.InvalidSelectionEvidence)]
    [InlineData(Stage02Codes.FileContextChanged)]
    public void Consumed_contract_exception_is_not_preconsumption_rejection(
      string code)
    {
      var exception = new Stage02ContractException(code, "consumed");

      Assert.False(Stage02PreConsumptionUiPolicy
        .Decide(exception, true).Handled);
    }
  }
}
