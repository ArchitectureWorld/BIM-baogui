using BIMBaoGui.RevitAddin.Stage02;
using BIMBaoGui.RevitAddin.Workflow;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02RoleConfirmationPolicyTests
  {
    [Fact]
    public void Automatic_candidate_without_confirmation_is_blocked()
    {
      var element = Element();
      var candidate = Candidate();
      NativeStage02RoleConfirmationDecision decision =
        NativeStage02RoleConfirmationPolicy.Resolve(
          element, candidate, null, null, Identity(), "snapshot-hash");

      Assert.False(decision.Confirmed);
      Assert.Equal("ROLE_CONFIRMATION_REQUIRED", decision.Code);
    }

    [Fact]
    public void Confirmation_is_rejected_after_element_or_rule_change()
    {
      var confirmation = new NativeStage02RoleConfirmation
      {
        ElementUniqueId = "A",
        RoleId = "SITE_GREEN_OBJECT",
        ElementSnapshotHash = "old",
        RulePackageSha256 = new string('a', 64)
      };
      NativeStage02RoleConfirmationDecision decision =
        NativeStage02RoleConfirmationPolicy.Resolve(
          Element(), Candidate(), null, confirmation, Identity(), "new");

      Assert.False(decision.Confirmed);
      Assert.Equal("ROLE_CONFIRMATION_STALE", decision.Code);
    }

    [Fact]
    public void Every_scope_requires_confirmation()
    {
      var fullModel = new NativeStage02PreviewRequest
      {
        ScopeMode = NativeStage02ScopeMode.FullModel,
        Confirmations = System.Array.Empty<NativeStage02RoleConfirmation>()
      };
      var customSelection = new NativeStage02PreviewRequest
      {
        ScopeMode = NativeStage02ScopeMode.CustomSelection,
        Confirmations = System.Array.Empty<NativeStage02RoleConfirmation>()
      };

      Assert.Empty(fullModel.Clone().Confirmations);
      Assert.Empty(customSelection.Clone().Confirmations);
      Assert.Equal(
        "ROLE_CONFIRMATION_REQUIRED",
        NativeStage02RoleConfirmationPolicy.Resolve(
          Element(), Candidate(), null, null, Identity(), "hash").Code);
    }

    [Fact]
    public void Current_explicit_confirmation_wins_and_assigned_role_does_not_stale_it()
    {
      NativeStage02ElementSnapshot element = Element();
      string snapshotHash = NativeStage02ElementSnapshotCanonicalizer.Sha256(element);
      var confirmation = new NativeStage02RoleConfirmation
      {
        ElementUniqueId = "A",
        RoleId = "SITE_GREEN_OBJECT",
        ElementSnapshotHash = snapshotHash,
        RulePackageSha256 = new string('a', 64),
        ConfirmedUtc = "2026-08-17T00:00:00Z"
      };

      element.AssignedRoleId = "SITE_GREEN_OBJECT";
      string afterSave = NativeStage02ElementSnapshotCanonicalizer.Sha256(element);
      NativeStage02RoleConfirmationDecision decision =
        NativeStage02RoleConfirmationPolicy.Resolve(
          element, Candidate(), null, confirmation, Identity(), afterSave);

      Assert.Equal(snapshotHash, afterSave);
      Assert.True(decision.Confirmed);
      Assert.Equal("ExplicitConfirmation", decision.Source);
      Assert.Equal("SITE_GREEN_OBJECT", decision.ResolvedRoleId);
    }

    [Fact]
    public void Current_persisted_assignment_is_a_saved_confirmation()
    {
      var persisted = new NativeStage02SemanticAssignmentRecord
      {
        ElementUniqueId = "A",
        RoleId = "SITE_GREEN_OBJECT",
        AssignmentMode = NativeStage02AssignmentMode.Manual,
        RulePackageSha256 = new string('a', 64),
        ElementSnapshotHash = "snapshot-hash",
        ConfirmedUtc = "2026-08-17T00:00:00Z"
      };

      NativeStage02RoleConfirmationDecision decision =
        NativeStage02RoleConfirmationPolicy.Resolve(
          Element(), Candidate(), persisted, null, Identity(), "snapshot-hash");

      Assert.True(decision.Confirmed);
      Assert.Equal("PersistedConfirmation", decision.Source);
    }

    [Fact]
    public void V100_assignment_is_preserved_but_requires_reconfirmation()
    {
      const string json = "{\"schemaVersion\":\"1.0.0\","
        + "\"rulePackageId\":\"HBR-WUHAN-PLANNING\","
        + "\"rulePackageVersion\":\"1.0.0\","
        + "\"assignments\":[{\"elementUniqueId\":\"A\","
        + "\"roleId\":\"SITE_GREEN_OBJECT\",\"assignmentMode\":\"MANUAL\","
        + "\"carrierCategory\":\"OST_BuildingPad\","
        + "\"carrierElementKind\":\"BuildingPad\"}]}";
      NativeStage02SemanticAssignmentStorageDecision decision =
        NativeStage02SemanticAssignmentStoragePolicy.Evaluate(
          new NativeStage02SemanticAssignmentStorageSnapshot
          {
            SchemaVersion = "1.0.0",
            RulePackageId = "HBR-WUHAN-PLANNING",
            RulePackageVersion = "1.0.0",
            CanonicalJson = json,
            PayloadSha256 = NativeStage02SemanticAssignmentCanonicalizer.Sha256(json),
            Payload = NativeStage02SemanticAssignmentCodec.Parse(json)
          },
          new[] { "A" });

      Assert.Equal(
        NativeStage02SemanticAssignmentStorageState.NeedsReconfirmation,
        decision.State);
      Assert.Equal("A", Assert.Single(decision.Payload.Assignments).ElementUniqueId);
    }

    private static NativeStage02ElementSnapshot Element()
    {
      return new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "doc",
        UniqueId = "A",
        ElementId = 1,
        Category = "OST_BuildingPad",
        ElementKind = "BuildingPad",
        IsModelElement = true
      };
    }

    private static NativeStage02SemanticCandidate Candidate()
    {
      return new NativeStage02SemanticCandidate
      {
        RoleId = "SITE_GREEN_OBJECT",
        Confidence = "HIGH"
      };
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
