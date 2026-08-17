using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.RevitAddin.Stage02;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02WorkbenchRequestPolicyTests
  {
    [Fact]
    public void Manual_request_trims_and_sorts_bulk_and_row_assignments()
    {
      NativeStage02PreviewRequest request =
        NativeStage02WorkbenchRequestPolicy.Build(
          NativeStage02ScopeMode.CustomSelection,
          NativeStage02IdentificationMode.Manual,
          " SITE_GREEN_OBJECT ",
          new Dictionary<string, string>
          {
            [" B "] = " SITE_GREEN_OBJECT ",
            [" A "] = NativeStage02RoleAssignmentPolicy.AutoOverrideRoleId
          });

      Assert.Equal(NativeStage02IdentificationMode.Manual, request.IdentificationMode);
      Assert.Equal("SITE_GREEN_OBJECT", request.BulkRoleId);
      Assert.Equal(
        new[] { "A", "B" },
        request.RoleOverrides.Select(value => value.ElementUniqueId).ToArray());
      Assert.Equal(
        new[]
        {
          NativeStage02RoleAssignmentPolicy.AutoOverrideRoleId,
          "SITE_GREEN_OBJECT"
        },
        request.RoleOverrides.Select(value => value.RoleId).ToArray());
    }

    [Fact]
    public void Automatic_request_discards_manual_inputs()
    {
      NativeStage02PreviewRequest request =
        NativeStage02WorkbenchRequestPolicy.Build(
          NativeStage02ScopeMode.CustomSelection,
          NativeStage02IdentificationMode.Automatic,
          "SITE_GREEN_OBJECT",
          new Dictionary<string, string>
          {
            ["A"] = "SITE_GREEN_OBJECT"
          });

      Assert.Equal(NativeStage02IdentificationMode.Automatic, request.IdentificationMode);
      Assert.Equal(string.Empty, request.BulkRoleId);
      Assert.Empty(request.RoleOverrides);
    }

    [Fact]
    public void Full_model_request_forces_automatic_mode_and_discards_manual_inputs()
    {
      NativeStage02PreviewRequest request =
        NativeStage02WorkbenchRequestPolicy.Build(
          NativeStage02ScopeMode.FullModel,
          NativeStage02IdentificationMode.Manual,
          "SITE_GREEN_OBJECT",
          new Dictionary<string, string>
          {
            ["A"] = "SITE_GREEN_OBJECT"
          });

      Assert.Equal(NativeStage02ScopeMode.FullModel, request.ScopeMode);
      Assert.Equal(NativeStage02IdentificationMode.Automatic, request.IdentificationMode);
      Assert.Equal(string.Empty, request.BulkRoleId);
      Assert.Empty(request.RoleOverrides);
    }

    [Fact]
    public void Request_clone_deep_copies_and_sorts_confirmations()
    {
      var original = new NativeStage02RoleConfirmation
      {
        ElementUniqueId = " B ",
        RoleId = " SITE_GREEN_OBJECT ",
        ElementSnapshotHash = "hash-b",
        RulePackageSha256 = new string('a', 64),
        ConfirmedUtc = "2026-08-17T00:00:00Z"
      };
      var request = new NativeStage02PreviewRequest
      {
        Confirmations = new[]
        {
          original,
          new NativeStage02RoleConfirmation
          {
            ElementUniqueId = "A",
            RoleId = "SITE_TOTAL_LAND",
            ElementSnapshotHash = "hash-a",
            RulePackageSha256 = new string('a', 64),
            ConfirmedUtc = "2026-08-17T00:00:00Z"
          }
        }
      };

      NativeStage02PreviewRequest clone = request.Clone();
      original.RoleId = "CHANGED";

      Assert.Equal(new[] { "A", "B" }, clone.Confirmations
        .Select(value => value.ElementUniqueId));
      Assert.Equal("SITE_GREEN_OBJECT", clone.Confirmations[1].RoleId);
      Assert.NotSame(original, clone.Confirmations[1]);
    }

    [Fact]
    public void Request_clone_rejects_conflicting_roles_for_one_element()
    {
      var request = new NativeStage02PreviewRequest
      {
        Confirmations = new[]
        {
          new NativeStage02RoleConfirmation
          {
            ElementUniqueId = "A",
            RoleId = "SITE_GREEN_OBJECT"
          },
          new NativeStage02RoleConfirmation
          {
            ElementUniqueId = "A",
            RoleId = "SITE_TOTAL_LAND"
          }
        }
      };

      InvalidOperationException error = Assert.Throws<InvalidOperationException>(
        () => request.Clone());
      Assert.Contains("ROLE_CONFIRMATION_CONFLICT", error.Message);
    }

    [Fact]
    public void Workbench_build_preserves_confirmation_input_for_preview_refresh()
    {
      var confirmations = new[]
      {
        new NativeStage02RoleConfirmation
        {
          ElementUniqueId = "A",
          RoleId = "SITE_GREEN_OBJECT",
          ElementSnapshotHash = "hash",
          RulePackageSha256 = new string('a', 64)
        }
      };

      NativeStage02PreviewRequest request =
        NativeStage02WorkbenchRequestPolicy.Build(
          NativeStage02ScopeMode.FullModel,
          NativeStage02IdentificationMode.Automatic,
          string.Empty,
          new Dictionary<string, string>(),
          confirmations);

      Assert.Equal("A", Assert.Single(request.Confirmations).ElementUniqueId);
    }
  }
}
