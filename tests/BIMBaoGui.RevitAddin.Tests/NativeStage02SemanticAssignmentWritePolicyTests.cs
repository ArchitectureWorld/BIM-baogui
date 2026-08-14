using System;
using BIMBaoGui.RevitAddin.Stage02;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02SemanticAssignmentWritePolicyTests
  {
    [Fact]
    public void Apply_creates_updates_and_removes_without_mutating_committed_payload()
    {
      NativeStage02SemanticAssignmentPayload empty = EmptyPayload();

      NativeStage02SemanticAssignmentPayload created =
        NativeStage02SemanticAssignmentWritePolicy.Apply(
          empty,
          SavePlan("A", "SITE_GREEN_OBJECT"));
      NativeStage02SemanticAssignmentPayload updated =
        NativeStage02SemanticAssignmentWritePolicy.Apply(
          created,
          SavePlan("A", "SITE_FIRE_FIELD"));
      NativeStage02SemanticAssignmentPayload removed =
        NativeStage02SemanticAssignmentWritePolicy.Apply(
          updated,
          RemovePlan("A"));

      Assert.Equal("SITE_GREEN_OBJECT", Assert.Single(created.Assignments).RoleId);
      Assert.Equal("SITE_FIRE_FIELD", Assert.Single(updated.Assignments).RoleId);
      Assert.Empty(removed.Assignments);
      Assert.Empty(empty.Assignments);
    }

    [Fact]
    public void Verify_accepts_exact_manual_record()
    {
      NativeStage02ElementPlan plan = SavePlan("A", "SITE_GREEN_OBJECT");
      NativeStage02SemanticAssignmentPayload actual =
        NativeStage02SemanticAssignmentWritePolicy.Apply(EmptyPayload(), plan);

      NativeStage02SemanticAssignmentReadbackDecision decision =
        NativeStage02SemanticAssignmentWritePolicy.Verify(actual, plan);

      Assert.True(decision.Success);
      Assert.Equal(string.Empty, decision.ErrorCode);
    }

    [Fact]
    public void Verify_rejects_missing_record_after_save()
    {
      AssertReadbackFailure(
        EmptyPayload(),
        SavePlan("A", "SITE_GREEN_OBJECT"));
    }

    [Fact]
    public void Verify_rejects_lingering_record_after_remove()
    {
      NativeStage02SemanticAssignmentPayload actual =
        NativeStage02SemanticAssignmentWritePolicy.Apply(
          EmptyPayload(),
          SavePlan("A", "SITE_GREEN_OBJECT"));

      AssertReadbackFailure(actual, RemovePlan("A"));
    }

    [Fact]
    public void Verify_rejects_wrong_role()
    {
      NativeStage02SemanticAssignmentPayload actual =
        NativeStage02SemanticAssignmentWritePolicy.Apply(
          EmptyPayload(),
          SavePlan("A", "SITE_FIRE_FIELD"));

      AssertReadbackFailure(actual, SavePlan("A", "SITE_GREEN_OBJECT"));
    }

    [Fact]
    public void Verify_rejects_wrong_carrier_category()
    {
      NativeStage02ElementPlan actualPlan = SavePlan("A", "SITE_GREEN_OBJECT");
      actualPlan.Element.Category = "OST_GenericModel";
      NativeStage02SemanticAssignmentPayload actual =
        NativeStage02SemanticAssignmentWritePolicy.Apply(
          EmptyPayload(),
          actualPlan);

      AssertReadbackFailure(actual, SavePlan("A", "SITE_GREEN_OBJECT"));
    }

    [Fact]
    public void Verify_rejects_wrong_carrier_element_kind()
    {
      NativeStage02ElementPlan actualPlan = SavePlan("A", "SITE_GREEN_OBJECT");
      actualPlan.Element.ElementKind = "DirectShape";
      NativeStage02SemanticAssignmentPayload actual =
        NativeStage02SemanticAssignmentWritePolicy.Apply(
          EmptyPayload(),
          actualPlan);

      AssertReadbackFailure(actual, SavePlan("A", "SITE_GREEN_OBJECT"));
    }

    [Fact]
    public void Corrupt_canonical_hash_is_rejected_before_readback_verification()
    {
      NativeStage02SemanticAssignmentStorageSnapshot snapshot =
        NativeStage02SemanticAssignmentStoragePolicy.CreateSnapshot(
          NativeStage02SemanticAssignmentWritePolicy.Apply(
            EmptyPayload(),
            SavePlan("A", "SITE_GREEN_OBJECT")),
          "2026-08-14T00:00:00Z");
      snapshot.PayloadSha256 = new string('0', 64);

      NativeStage02SemanticAssignmentStorageDecision decision =
        NativeStage02SemanticAssignmentStoragePolicy.Evaluate(
          snapshot,
          new[] { "A" });

      Assert.Equal(
        NativeStage02SemanticAssignmentStorageState.Corrupt,
        decision.State);
    }

    private static void AssertReadbackFailure(
      NativeStage02SemanticAssignmentPayload actual,
      NativeStage02ElementPlan plan)
    {
      NativeStage02SemanticAssignmentReadbackDecision decision =
        NativeStage02SemanticAssignmentWritePolicy.Verify(actual, plan);
      Assert.False(decision.Success);
      Assert.Equal("SEMANTIC_ASSIGNMENT_READBACK_FAILED", decision.ErrorCode);
    }

    private static NativeStage02SemanticAssignmentPayload EmptyPayload()
    {
      return new NativeStage02SemanticAssignmentPayload
      {
        SchemaVersion = NativeStage02SemanticAssignmentSchema.Version,
        RulePackageId = "HBR-WUHAN-PLANNING",
        RulePackageVersion = "1.0.0",
        Assignments = Array.Empty<NativeStage02SemanticAssignmentRecord>()
      };
    }

    private static NativeStage02ElementPlan SavePlan(
      string uniqueId,
      string roleId)
    {
      return new NativeStage02ElementPlan
      {
        Element = Element(uniqueId),
        EffectiveRoleId = roleId,
        AssignmentMode = NativeStage02AssignmentMode.Manual,
        AssignmentSource = "Override",
        AssignmentAction = NativeStage02AssignmentActions.SaveManualAssignment,
        ManualCarrierEvidence = "OST_BuildingPad|BuildingPad"
      };
    }

    private static NativeStage02ElementPlan RemovePlan(string uniqueId)
    {
      return new NativeStage02ElementPlan
      {
        Element = Element(uniqueId),
        AssignmentMode = NativeStage02AssignmentMode.Auto,
        AssignmentSource = "OverrideAuto",
        AssignmentAction = NativeStage02AssignmentActions.RemoveManualAssignment,
        ManualCarrierEvidence = "OST_BuildingPad|BuildingPad"
      };
    }

    private static NativeStage02ElementSnapshot Element(string uniqueId)
    {
      return new NativeStage02ElementSnapshot
      {
        UniqueId = uniqueId,
        ElementId = 42,
        Category = "OST_BuildingPad",
        ElementKind = "BuildingPad",
        IsModelElement = true
      };
    }
  }
}
