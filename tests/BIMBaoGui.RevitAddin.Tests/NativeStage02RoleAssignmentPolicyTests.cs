using System;
using System.Linq;
using BIMBaoGui.RevitAddin.Stage02;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02RoleAssignmentPolicyTests
  {
    [Fact]
    public void AutomaticModeAcceptsEmptyManualRoleInput()
    {
      NativeStage02RoleAssignmentDecision decision = Resolve(
        NativeStage02IdentificationMode.Automatic,
        new[] { "b", "a" });
      Assert.True(decision.Accepted);
      Assert.Empty(decision.Assignments);
      Assert.Equal(new[] { "a", "b" }, decision.SelectedUniqueIds);
    }

    [Fact]
    public void AutomaticModeRejectsManualRoleInput()
    {
      NativeStage02RoleAssignmentDecision decision = Resolve(
        NativeStage02IdentificationMode.Automatic,
        new[] { "a" },
        "SITE_GREEN_OBJECT");
      Assert.False(decision.Accepted);
      Assert.Equal(
        NativeStage02RoleAssignmentCodes.AutomaticModeInputConflict,
        decision.ErrorCode);
    }

    [Fact]
    public void ManualModeAcceptsBulkRole()
    {
      NativeStage02RoleAssignmentDecision decision = Resolve(
        NativeStage02IdentificationMode.Manual,
        new[] { "b", "a" },
        " SITE_GREEN_OBJECT ");
      Assert.True(decision.Accepted);
      Assert.Equal(new[] { "a", "b" },
        decision.Assignments.Select(value => value.ElementUniqueId));
      Assert.All(decision.Assignments, value =>
      {
        Assert.Equal("SITE_GREEN_OBJECT", value.RoleId);
        Assert.Equal(NativeStage02AssignmentMode.Manual, value.AssignmentMode);
        Assert.Equal("Bulk", value.Source);
      });
    }

    [Fact]
    public void ElementOverrideWinsOverBulkRole()
    {
      NativeStage02RoleAssignmentDecision decision = Resolve(
        NativeStage02IdentificationMode.Manual,
        new[] { "a", "b" },
        "SITE_GREEN_OBJECT",
        Override("b", "SITE_FIRE_FIELD"));
      Assert.True(decision.Accepted);
      Assert.Equal("SITE_GREEN_OBJECT", decision.Assignments[0].RoleId);
      Assert.Equal("Bulk", decision.Assignments[0].Source);
      Assert.Equal("SITE_FIRE_FIELD", decision.Assignments[1].RoleId);
      Assert.Equal("Override", decision.Assignments[1].Source);
    }

    [Fact]
    public void PerElementAutoOverrideRestoresAutomaticRecognition()
    {
      NativeStage02RoleAssignmentDecision decision = Resolve(
        NativeStage02IdentificationMode.Manual,
        new[] { "a", "b" },
        "SITE_GREEN_OBJECT",
        Override("b", NativeStage02RoleAssignmentPolicy.AutoOverrideRoleId));
      Assert.True(decision.Accepted);
      NativeStage02ResolvedAssignment restored = Assert.Single(
        decision.Assignments,
        value => value.ElementUniqueId == "b");
      Assert.Equal(NativeStage02AssignmentMode.Auto, restored.AssignmentMode);
      Assert.Empty(restored.RoleId);
      Assert.Equal("OverrideAuto", restored.Source);
    }

    [Fact]
    public void DuplicateOverrideWithSameRoleIsCanonicalized()
    {
      NativeStage02RoleAssignmentDecision decision = Resolve(
        NativeStage02IdentificationMode.Manual,
        new[] { "a" },
        string.Empty,
        Override("a", "SITE_GREEN_OBJECT"),
        Override(" a ", "SITE_GREEN_OBJECT"));
      Assert.True(decision.Accepted);
      Assert.Single(decision.Assignments);
      Assert.Equal("SITE_GREEN_OBJECT", decision.Assignments[0].RoleId);
    }

    [Fact]
    public void DuplicateOverrideWithDifferentRolesIsBlocked()
    {
      NativeStage02RoleAssignmentDecision decision = Resolve(
        NativeStage02IdentificationMode.Manual,
        new[] { "a" },
        string.Empty,
        Override("a", "SITE_GREEN_OBJECT"),
        Override("a", "SITE_FIRE_FIELD"));
      Assert.False(decision.Accepted);
      Assert.Equal(
        NativeStage02RoleAssignmentCodes.RoleAssignmentConflict,
        decision.ErrorCode);
    }

    [Fact]
    public void OverrideOutsideCurrentSelectionIsBlocked()
    {
      NativeStage02RoleAssignmentDecision decision = Resolve(
        NativeStage02IdentificationMode.Manual,
        new[] { "a" },
        string.Empty,
        Override("b", "SITE_GREEN_OBJECT"));
      Assert.False(decision.Accepted);
      Assert.Equal(
        NativeStage02RoleAssignmentCodes.OverrideElementNotSelected,
        decision.ErrorCode);
    }

    [Fact]
    public void CanonicalOrderDoesNotDependOnInputOrder()
    {
      NativeStage02RoleAssignmentDecision first = Resolve(
        NativeStage02IdentificationMode.Manual,
        new[] { "c", "a", " b ", "a" },
        "SITE_GREEN_OBJECT",
        Override("c", "SITE_FIRE_FIELD"));
      NativeStage02RoleAssignmentDecision second = Resolve(
        NativeStage02IdentificationMode.Manual,
        new[] { "b", "c", "a" },
        "SITE_GREEN_OBJECT",
        Override("c", "SITE_FIRE_FIELD"));
      Assert.True(first.Accepted);
      Assert.True(second.Accepted);
      Assert.Equal(first.SelectedUniqueIds, second.SelectedUniqueIds);
      Assert.Equal(first.Assignments.Select(ValueKey), second.Assignments.Select(ValueKey));
    }

    [Fact]
    public void FullModelRejectsCurrentSelectionManualInputs()
    {
      NativeStage02RoleAssignmentDecision decision =
        NativeStage02RoleAssignmentPolicy.Resolve(
          NativeStage02ScopeMode.FullModel,
          NativeStage02IdentificationMode.Manual,
          new[] { "a" },
          "SITE_GREEN_OBJECT",
          Array.Empty<NativeStage02RoleOverride>());
      Assert.False(decision.Accepted);
      Assert.Equal(NativeStage02RoleAssignmentCodes.ScopeInputConflict, decision.ErrorCode);
    }

    [Fact]
    public void PreviewRequestCloneDeepCopiesSemanticInputs()
    {
      var originalOverride = Override(" b ", " SITE_FIRE_FIELD ");
      var request = new NativeStage02PreviewRequest
      {
        ScopeMode = NativeStage02ScopeMode.CustomSelection,
        IdentificationMode = NativeStage02IdentificationMode.Manual,
        CustomUniqueIds = new[] { " b ", "a", "a" },
        BulkRoleId = " SITE_GREEN_OBJECT ",
        RoleOverrides = new[] { originalOverride }
      };
      NativeStage02PreviewRequest clone = request.Clone();
      originalOverride.RoleId = "CHANGED";
      Assert.Equal(NativeStage02ScopeMode.CustomSelection, clone.ScopeMode);
      Assert.Equal(NativeStage02IdentificationMode.Manual, clone.IdentificationMode);
      Assert.Equal(new[] { "a", "b" }, clone.CustomUniqueIds);
      Assert.Equal("SITE_GREEN_OBJECT", clone.BulkRoleId);
      Assert.Single(clone.RoleOverrides);
      Assert.Equal("b", clone.RoleOverrides[0].ElementUniqueId);
      Assert.Equal("SITE_FIRE_FIELD", clone.RoleOverrides[0].RoleId);
    }

    private static NativeStage02RoleAssignmentDecision Resolve(
      NativeStage02IdentificationMode mode,
      string[] selected,
      string bulkRoleId = "",
      params NativeStage02RoleOverride[] overrides)
    {
      return NativeStage02RoleAssignmentPolicy.Resolve(
        NativeStage02ScopeMode.CustomSelection,
        mode,
        selected,
        bulkRoleId,
        overrides ?? Array.Empty<NativeStage02RoleOverride>());
    }

    private static NativeStage02RoleOverride Override(string uniqueId, string roleId)
    {
      return new NativeStage02RoleOverride
      {
        ElementUniqueId = uniqueId,
        RoleId = roleId
      };
    }

    private static string ValueKey(NativeStage02ResolvedAssignment value)
    {
      return value.ElementUniqueId + "|" + value.RoleId + "|"
        + value.AssignmentMode + "|" + value.Source;
    }
  }
}
