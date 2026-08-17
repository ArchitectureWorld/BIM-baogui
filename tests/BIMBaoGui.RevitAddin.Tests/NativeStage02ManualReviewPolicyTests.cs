using System;
using BIMBaoGui.RevitAddin.Stage02;
using BIMBaoGui.RevitAddin.Workflow;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02ManualReviewPolicyTests
  {
    [Fact]
    public void Missing_and_rejected_records_are_red()
    {
      NativeStage02GeometryCheckEvidence missing =
        NativeStage02ManualReviewPolicy.VerifyCurrent(
          null, "check", "用地关系有效", Identity(),
          new[] { "A" }, new[] { "snapshot" }, new[] { "geometry" });
      NativeStage02ManualReviewRecord rejected = Seal("REJECTED");
      NativeStage02GeometryCheckEvidence rejectedDecision =
        NativeStage02ManualReviewPolicy.VerifyCurrent(
          rejected, "check", "用地关系有效", Identity(),
          new[] { "A" }, new[] { "snapshot" }, new[] { "geometry" });

      Assert.Equal(NativeStage02GeometryCheckState.ManualReviewRequired, missing.State);
      Assert.Equal("MANUAL_REVIEW_REQUIRED", missing.Code);
      Assert.Equal(NativeStage02GeometryCheckState.Failed, rejectedDecision.State);
      Assert.Equal("MANUAL_REVIEW_REJECTED", rejectedDecision.Code);
    }

    [Theory]
    [InlineData("DOCUMENT")]
    [InlineData("RULE")]
    [InlineData("ELEMENT_SET")]
    [InlineData("SNAPSHOT")]
    [InlineData("GEOMETRY")]
    public void Any_current_fact_change_makes_record_stale(string change)
    {
      NativeStage02ManualReviewRecord approved = Seal("APPROVED");
      NativeWorkflowIdentity identity = Identity();
      string[] ids = { "A" };
      string[] snapshots = { "snapshot" };
      string[] geometry = { "geometry" };
      string ruleText = "用地关系有效";
      if (change == "DOCUMENT") identity.DocumentFingerprint = "other";
      if (change == "RULE") identity.RulePackageSha256 = new string('b', 64);
      if (change == "ELEMENT_SET") ids = new[] { "B" };
      if (change == "SNAPSHOT") snapshots = new[] { "new-snapshot" };
      if (change == "GEOMETRY") geometry = new[] { "new-geometry" };

      NativeStage02GeometryCheckEvidence decision =
        NativeStage02ManualReviewPolicy.VerifyCurrent(
          approved, "check", ruleText, identity, ids, snapshots, geometry);

      Assert.Equal(NativeStage02GeometryCheckState.ManualReviewRequired, decision.State);
      Assert.Equal("MANUAL_REVIEW_STALE", decision.Code);
    }

    [Theory]
    [InlineData("用地关系有效")]
    [InlineData("与道路红线关系有效")]
    [InlineData("消防道路范围有效")]
    [InlineData("与服务建筑关系有效")]
    [InlineData("停车范围有效")]
    [InlineData("范围关系有效")]
    public void Exact_six_manual_rules_accept_only_current_approved_record(string ruleText)
    {
      NativeStage02ManualReviewRecord approved = NativeStage02ManualReviewPolicy.Seal(
        new NativeStage02ManualReviewRecord
        {
          CheckId = "check",
          RuleText = ruleText,
          DocumentFingerprint = "doc",
          RulePackageSha256 = new string('a', 64),
          ElementUniqueIds = new[] { "A" },
          ElementSnapshotHashes = new[] { "snapshot" },
          GeometryEvidenceHashes = new[] { "geometry" },
          Decision = "APPROVED",
          Reviewer = "reviewer",
          Basis = "current RVT review",
          ReviewedUtc = "2026-08-17T00:00:00Z"
        });

      NativeStage02GeometryCheckEvidence decision =
        NativeStage02ManualReviewPolicy.VerifyCurrent(
          approved, "check", ruleText, Identity(),
          new[] { "A" }, new[] { "snapshot" }, new[] { "geometry" });

      Assert.Equal(NativeStage02GeometryCheckState.ManualReviewApproved, decision.State);
      Assert.Equal("MANUAL_REVIEW_APPROVED_CURRENT", decision.Code);
      Assert.Matches("^[0-9a-f]{64}$", approved.RecordHash);
    }

    [Theory]
    [InlineData("", "basis")]
    [InlineData("reviewer", "")]
    public void Reviewer_and_basis_are_required(string reviewer, string basis)
    {
      Assert.Throws<InvalidOperationException>(() =>
        NativeStage02ManualReviewPolicy.Seal(
          new NativeStage02ManualReviewRecord
          {
            CheckId = "check",
            RuleText = "用地关系有效",
            DocumentFingerprint = "doc",
            RulePackageSha256 = new string('a', 64),
            ElementUniqueIds = new[] { "A" },
            ElementSnapshotHashes = new[] { "snapshot" },
            GeometryEvidenceHashes = new[] { "geometry" },
            Decision = "APPROVED",
            Reviewer = reviewer,
            Basis = basis,
            ReviewedUtc = "2026-08-17T00:00:00Z"
          }));
    }

    private static NativeStage02ManualReviewRecord Seal(string decision)
    {
      return NativeStage02ManualReviewPolicy.Seal(
        new NativeStage02ManualReviewRecord
        {
          CheckId = "check",
          RuleText = "用地关系有效",
          DocumentFingerprint = "doc",
          RulePackageSha256 = new string('a', 64),
          ElementUniqueIds = new[] { "A" },
          ElementSnapshotHashes = new[] { "snapshot" },
          GeometryEvidenceHashes = new[] { "geometry" },
          Decision = decision,
          Reviewer = "reviewer",
          Basis = "current RVT review",
          ReviewedUtc = "2026-08-17T00:00:00Z"
        });
    }

    private static NativeWorkflowIdentity Identity()
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
