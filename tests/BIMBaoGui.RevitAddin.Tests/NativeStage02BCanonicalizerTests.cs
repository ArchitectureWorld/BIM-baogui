using System;
using System.Linq;
using BIMBaoGui.RevitAddin.Stage02B;
using BIMBaoGui.RevitAddin.Workflow;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02BCanonicalizerTests
  {
    [Fact]
    public void SealedRecordsHaveDeterministicHashesAcrossInputOrder()
    {
      NativeStage02BStorageSnapshot first = NativeStage02BCanonicalizer.SealSnapshot(
        new[] { Record("b"), Record("a") });
      NativeStage02BStorageSnapshot second = NativeStage02BCanonicalizer.SealSnapshot(
        new[] { Record(" a "), Record(" b ") });

      Assert.Equal(first.CanonicalJson, second.CanonicalJson);
      Assert.Equal(first.SnapshotHash, second.SnapshotHash);
      Assert.Equal(new[] { "a", "b" }, first.Records.Select(value => value.PropertyId));
      Assert.All(first.Records, value => Assert.True(
        NativeStage02BCanonicalizer.VerifyRecord(value)));
    }

    [Fact]
    public void TamperedSealedRecordIsRejectedAsHashMismatch()
    {
      NativeStage02BMetricRecord record = NativeStage02BCanonicalizer.SealRecord(
        Record("a"));
      record.RequestedCanonicalValue = "999";

      NativeStage02BCurrentResultDecision decision =
        NativeStage02BCurrentResultPolicy.Evaluate(record, Identity());

      Assert.False(decision.Current);
      Assert.Equal("RECORD_HASH_MISMATCH", decision.Code);
    }

    internal static NativeStage02BMetricRecord Record(string propertyId)
    {
      return new NativeStage02BMetricRecord
      {
        PropertyId = propertyId,
        Identity = "IfcSite|Pset|" + propertyId.Trim(),
        Unit = "-",
        RequestedCanonicalValue = "1.2",
        LastSuccessfulCanonicalValue = "1.2",
        LastAttemptRunId = "run-1",
        LastSuccessfulRunId = "run-1",
        WriteStatus = "SUCCEEDED",
        ReadbackStatus = "SUCCEEDED",
        ProjectionStatus = "INTERNAL_STORAGE_ONLY",
        IdentityContext = Identity(),
        UpdatedUtc = "2026-08-14T00:00:00.0000000+00:00"
      };
    }

    internal static NativeWorkflowIdentity Identity()
    {
      return new NativeWorkflowIdentity
      {
        DocumentFingerprint = "doc",
        ModelFileType = "总平模型",
        RulePackageId = "HBR-WUHAN-PLANNING",
        RulePackageVersion = "1.0.0",
        RulePackageSha256 = new string('a', 64)
      };
    }
  }
}
