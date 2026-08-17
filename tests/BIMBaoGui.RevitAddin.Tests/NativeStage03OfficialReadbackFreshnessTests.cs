using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage03;
using BIMBaoGui.RevitAddin.Workflow;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage03OfficialReadbackFreshnessTests
  {
    private const string Stage01Input =
      "1111111111111111111111111111111111111111111111111111111111111111";
    private const string Stage02AInput =
      "2222222222222222222222222222222222222222222222222222222222222222";
    private const string Stage02BInput =
      "3333333333333333333333333333333333333333333333333333333333333333";

    [Fact]
    public void Nonempty_source_result_hash_cannot_hide_stale_stage02a_input()
    {
      NativeWorkflowIdentity identity = Identity("document-current");
      NativeWorkflowResultEnvelope stage02A = Result(
        identity,
        new string('f', 64));

      string code = NativeStage03Scanner.OfficialReadbackFreshnessCode(
        NativeReportingSourceStage.Stage02A,
        identity,
        Result(identity, Stage01Input),
        Stage01Input,
        stage02A,
        Stage02AInput,
        Result(identity, Stage02BInput),
        Stage02BInput);

      Assert.NotEmpty(stage02A.ResultHash);
      Assert.Equal("WORKFLOW_INPUT_STALE", code);
    }

    [Fact]
    public void Official_readback_reports_source_identity_mismatch_code()
    {
      NativeWorkflowIdentity current = Identity("document-current");
      NativeWorkflowResultEnvelope stage01 = Result(
        Identity("document-old"),
        Stage01Input);

      string code = NativeStage03Scanner.OfficialReadbackFreshnessCode(
        NativeReportingSourceStage.Stage01,
        current,
        stage01,
        Stage01Input,
        Result(current, Stage02AInput),
        Stage02AInput,
        Result(current, Stage02BInput),
        Stage02BInput);

      Assert.Equal("WORKFLOW_DOCUMENT_MISMATCH", code);
    }

    [Fact]
    public void Official_readback_reports_source_result_hash_mismatch_code()
    {
      NativeWorkflowIdentity identity = Identity("document-current");
      NativeWorkflowResultEnvelope stage02B = Result(identity, Stage02BInput);
      stage02B.ResultHash = new string('0', 64);

      string code = NativeStage03Scanner.OfficialReadbackFreshnessCode(
        NativeReportingSourceStage.Stage02B,
        identity,
        Result(identity, Stage01Input),
        Stage01Input,
        Result(identity, Stage02AInput),
        Stage02AInput,
        stage02B,
        Stage02BInput);

      Assert.Equal("WORKFLOW_RESULT_HASH_MISMATCH", code);
    }

    private static NativeWorkflowIdentity Identity(string documentFingerprint)
    {
      return new NativeWorkflowIdentity
      {
        DocumentFingerprint = documentFingerprint,
        ModelFileType = "总平模型",
        RulePackageId = "HBR-WUHAN-PLANNING",
        RulePackageVersion = "1.0.0",
        RulePackageSha256 = new string('a', 64)
      };
    }

    private static NativeWorkflowResultEnvelope Result(
      NativeWorkflowIdentity identity,
      string inputHash)
    {
      return NativeWorkflowResultCanonicalizer.Build(
        "run",
        "TEST",
        "TEST",
        identity,
        inputHash,
        new[]
        {
          new NativeWorkflowItemEvidence
          {
            Identity = "ITEM",
            CurrentValue = "value",
            Source = "TEST",
            WriteSucceeded = true,
            ReadbackSucceeded = true,
            InputHash = new string('9', 64),
            UpdatedUtc = "2026-08-14T00:00:00.0000000Z"
          }
        },
        "2026-08-14T00:00:00.0000000Z");
    }
  }
}
