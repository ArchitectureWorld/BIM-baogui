using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
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
    public void ExportRejectsVerifiedRecordWithRefsOutsideItsCatalogProperty()
    {
      NativeStage02BMetricDefinition metric = NativeStage02BMetricCatalog.Current
        .MetricsFor("总平模型")
        .Single(value => value.PropertyId == "201a00ac-3672-5ded-83d2-ed96f81bfabf");
      NativeStage02BMetricRecord record = NativeStage02BCanonicalizer.SealRecord(
        new NativeStage02BMetricRecord
        {
          PropertyId = metric.PropertyId,
          Identity = metric.Identity,
          RequestedCanonicalValue = "1.2",
          LastSuccessfulCanonicalValue = "1.2",
          LastAttemptRunId = "run-current",
          LastSuccessfulRunId = "run-current",
          WriteStatus = "SUCCEEDED",
          ReadbackStatus = "SUCCEEDED",
          OfficialCarrierStatus = NativeOfficialCarrierEvidenceStatus.Verified,
          OfficialProjectionCarrierId = "not-a-catalog-carrier",
          OfficialCarrierProbeRef = "not-a-catalog-probe",
          IdentityContext = NativeStage02BCanonicalizerTests.Identity()
        });

      NativeStage02BCurrentResultDecision decision =
        NativeStage02BCurrentResultPolicy.Evaluate(
          record, NativeStage02BCanonicalizerTests.Identity());

      Assert.True(decision.Current);
      Assert.False(decision.ExportReady);
      Assert.Equal("1.2", decision.CurrentCanonicalValue);
    }

    [Fact]
    public void AllCurrentActualMetricsRemainNotExportReadyWithoutCatalogEvidence()
    {
      foreach (NativeStage02BMetricDefinition metric in NativeStage02BMetricCatalog
        .Current.MetricsFor("总平模型"))
      {
        NativeStage02BMetricRecord record = NativeStage02BCanonicalizer.SealRecord(
          new NativeStage02BMetricRecord
          {
            PropertyId = metric.PropertyId,
            Identity = metric.Identity,
            RequestedCanonicalValue = "1.2",
            LastSuccessfulCanonicalValue = "1.2",
            LastAttemptRunId = "run-current",
            LastSuccessfulRunId = "run-current",
            WriteStatus = "SUCCEEDED",
            ReadbackStatus = "SUCCEEDED",
            OfficialCarrierStatus = NativeOfficialCarrierEvidenceStatus.Verified,
            OfficialProjectionCarrierId = "cross-property-carrier",
            OfficialCarrierProbeRef = "cross-property-probe",
            IdentityContext = NativeStage02BCanonicalizerTests.Identity()
          });

        NativeStage02BCurrentResultDecision decision =
          NativeStage02BCurrentResultPolicy.Evaluate(
            record, NativeStage02BCanonicalizerTests.Identity());

        Assert.True(decision.Current);
        Assert.False(decision.ExportReady);
        Assert.Equal("CURRENT_PROPERTY_EVIDENCE_UNRESOLVED", decision.Code);
      }
    }
  }
}
