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
  }
}
