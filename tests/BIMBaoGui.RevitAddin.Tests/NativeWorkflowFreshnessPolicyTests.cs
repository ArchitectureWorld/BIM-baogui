using System;
using BIMBaoGui.RevitAddin.Workflow;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeWorkflowFreshnessPolicyTests
  {
    [Fact]
    public void CrossDocumentRuleOrInputHashIsRejectedInFrozenOrder()
    {
      NativeWorkflowIdentity identity =
        NativeWorkflowResultCanonicalizerTests.TestIdentity("TOTAL_PLAN");
      NativeWorkflowResultEnvelope result =
        NativeWorkflowResultCanonicalizerTests.Build(
          identity,
          new[] { NativeWorkflowResultCanonicalizerTests.Item("A") });

      Assert.Equal(
        NativeWorkflowFreshnessState.Current,
        Evaluate(result, identity, result.InputSnapshotHash).State);

      NativeWorkflowIdentity otherDocument = Copy(identity);
      otherDocument.DocumentFingerprint = "OTHER_DOCUMENT";
      Assert.Equal(
        NativeWorkflowFreshnessState.DocumentMismatch,
        Evaluate(result, otherDocument, result.InputSnapshotHash).State);

      NativeWorkflowIdentity otherModel = Copy(identity);
      otherModel.ModelFileType = "单体建筑—地上";
      Assert.Equal(
        NativeWorkflowFreshnessState.ModelTypeMismatch,
        Evaluate(result, otherModel, result.InputSnapshotHash).State);

      NativeWorkflowIdentity otherRule = Copy(identity);
      otherRule.RulePackageSha256 = new string('f', 64);
      Assert.Equal(
        NativeWorkflowFreshnessState.RulePackageMismatch,
        Evaluate(result, otherRule, result.InputSnapshotHash).State);

      Assert.Equal(
        NativeWorkflowFreshnessState.InputStale,
        Evaluate(result, identity, new string('0', 64)).State);
    }

    [Fact]
    public void SchemaAndResultHashFailuresOutrankIdentityFailures()
    {
      NativeWorkflowIdentity identity =
        NativeWorkflowResultCanonicalizerTests.TestIdentity("TOTAL_PLAN");
      NativeWorkflowResultEnvelope result =
        NativeWorkflowResultCanonicalizerTests.Build(
          identity,
          new[] { NativeWorkflowResultCanonicalizerTests.Item("A") });
      NativeWorkflowIdentity other = Copy(identity);
      other.DocumentFingerprint = "OTHER";

      result.SchemaVersion = "BROKEN";
      Assert.Equal(
        NativeWorkflowFreshnessState.SchemaMismatch,
        Evaluate(result, other, new string('0', 64)).State);

      result.SchemaVersion = "HBR_NATIVE_WORKFLOW_RESULT_V1";
      result.ResultHash = new string('0', 64);
      Assert.Equal(
        NativeWorkflowFreshnessState.ResultHashMismatch,
        Evaluate(result, other, new string('0', 64)).State);
    }

    private static NativeWorkflowFreshnessDecision Evaluate(
      NativeWorkflowResultEnvelope result,
      NativeWorkflowIdentity identity,
      string inputHash)
    {
      return NativeWorkflowFreshnessPolicy.Evaluate(result, identity, inputHash);
    }

    private static NativeWorkflowIdentity Copy(NativeWorkflowIdentity source)
    {
      return new NativeWorkflowIdentity
      {
        DocumentFingerprint = source.DocumentFingerprint,
        ModelFileType = source.ModelFileType,
        RulePackageId = source.RulePackageId,
        RulePackageVersion = source.RulePackageVersion,
        RulePackageSha256 = source.RulePackageSha256
      };
    }
  }
}
