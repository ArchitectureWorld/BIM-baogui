using BIMBaoGui.RevitAddin.Stage02B;
using BIMBaoGui.RevitAddin.Workflow;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02BCurrentResultPolicyTests
  {
    [Fact]
    public void FailedLatestAttemptInvalidatesOldSuccess()
    {
      NativeWorkflowIdentity identity = NativeStage02BCanonicalizerTests.Identity();
      NativeStage02BMetricRecord record = NativeStage02BCanonicalizer.SealRecord(
        new NativeStage02BMetricRecord
        {
          PropertyId = "201a00ac-3672-5ded-83d2-ed96f81bfabf",
          Identity = "IfcSite|Pset_场地信息属性集|容积率",
          RequestedCanonicalValue = "1.2",
          LastSuccessfulCanonicalValue = "1.2",
          LastAttemptRunId = "run-new",
          LastSuccessfulRunId = "run-old",
          WriteStatus = "FAILED",
          ReadbackStatus = "SUCCEEDED",
          IdentityContext = identity
        });

      NativeStage02BCurrentResultDecision decision =
        NativeStage02BCurrentResultPolicy.Evaluate(record, identity);

      Assert.False(decision.Current);
      Assert.Equal("LATEST_ATTEMPT_FAILED", decision.Code);
    }

    [Fact]
    public void ExportRequiresPropertyScopedVerifiedCarrierAndProbe()
    {
      NativeStage02BMetricRecord record = NativeStage02BCanonicalizer.SealRecord(
        NativeStage02BCanonicalizerTests.Record("property-a"));
      record.OfficialCarrierStatus =
        BIMBaoGui.RevitAddin.Rules.NativeOfficialCarrierEvidenceStatus.Verified;
      record.OfficialProjectionCarrierId = "carrier-a";
      record.OfficialCarrierProbeRef = "probe-a";
      record = NativeStage02BCanonicalizer.SealRecord(record);

      NativeStage02BCurrentResultDecision decision =
        NativeStage02BCurrentResultPolicy.Evaluate(
          record, NativeStage02BCanonicalizerTests.Identity());

      Assert.True(decision.Current);
      Assert.True(decision.ExportReady);
      Assert.Equal("1.2", decision.CurrentCanonicalValue);
    }
  }
}
