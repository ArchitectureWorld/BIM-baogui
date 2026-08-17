using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage02;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02ManualPreviewCompilerTests
  {
    [Fact]
    public void Resolved_manual_role_and_assignment_are_frozen_in_preview_v3()
    {
      NativeStage02Preview preview = Compile(
        "集中绿地",
        "SITE_GREEN_OBJECT",
        new[]
        {
          Override("A", "SITE_GREEN_OBJECT")
        });

      NativeStage02ElementPlan element = Assert.Single(preview.Elements);
      Assert.Equal("HBR_NATIVE_STAGE02A_PREVIEW_V3", preview.SchemaVersion);
      Assert.Equal(NativeStage02IdentificationMode.Manual, preview.IdentificationMode);
      Assert.Equal("SITE_GREEN_OBJECT", preview.BulkRoleId);
      Assert.Equal(NativeStage02RoleMatchStatus.Matched, element.RoleMatchStatus);
      Assert.Equal("SITE_GREEN_OBJECT", element.EffectiveRoleId);
      Assert.Equal(NativeStage02AssignmentMode.Manual, element.AssignmentMode);
      Assert.Equal("Bulk", element.AssignmentSource);
      Assert.Equal(
        NativeStage02AssignmentActions.SaveManualAssignment,
        element.AssignmentAction);
      Assert.Equal(4, element.Fields.Count);
      Assert.Contains(
        "\"identificationMode\":\"Manual\"",
        preview.CanonicalJson);
      Assert.Contains(
        "\"bulkRoleId\":\"SITE_GREEN_OBJECT\"",
        preview.CanonicalJson);
      Assert.Contains(
        "\"assignmentAction\":\"SaveManualAssignment\"",
        preview.CanonicalJson);
      Assert.Matches("^[0-9a-f]{64}$", preview.PreviewHash);
    }

    [Fact]
    public void Changing_a_row_override_changes_the_preview_hash()
    {
      NativeStage02Preview manual = Compile(
        "集中绿地",
        "SITE_GREEN_OBJECT",
        new[] { Override("A", "SITE_GREEN_OBJECT") });
      NativeStage02Preview automatic = Compile(
        "集中绿地",
        "SITE_GREEN_OBJECT",
        new[]
        {
          Override("A", NativeStage02RoleAssignmentPolicy.AutoOverrideRoleId)
        });

      Assert.NotEqual(manual.PreviewHash, automatic.PreviewHash);
    }

    [Fact]
    public void Override_input_order_does_not_change_the_preview_hash()
    {
      NativeStage02Preview left = Compile(
        "集中绿地",
        "SITE_GREEN_OBJECT",
        new[]
        {
          Override("B", "SITE_GREEN_OBJECT"),
          Override("A", "SITE_GREEN_OBJECT")
        });
      NativeStage02Preview right = Compile(
        "集中绿地",
        "SITE_GREEN_OBJECT",
        new[]
        {
          Override("A", "SITE_GREEN_OBJECT"),
          Override("B", "SITE_GREEN_OBJECT")
        });

      Assert.Equal(left.CanonicalJson, right.CanonicalJson);
      Assert.Equal(left.PreviewHash, right.PreviewHash);
    }

    [Fact]
    public void Presentation_name_changes_stage02a_preview_hash()
    {
      NativeStage02Preview before = Compile(
        "集中绿地",
        "SITE_GREEN_OBJECT",
        new[] { Override("A", "SITE_GREEN_OBJECT") });
      NativeStage02Preview after = Compile(
        "展示名称已修改",
        "SITE_GREEN_OBJECT",
        new[] { Override("A", "SITE_GREEN_OBJECT") });

      Assert.NotEqual(before.CanonicalJson, after.CanonicalJson);
      Assert.NotEqual(before.PreviewHash, after.PreviewHash);
    }

    private static NativeStage02Preview Compile(
      string elementName,
      string bulkRoleId,
      IReadOnlyList<NativeStage02RoleOverride> overrides)
    {
      NativeStage02RuleCatalog catalog = NativeStage02RuleCatalog.Current;
      NativeStage02PropertyDefinition[] properties = catalog
        .PropertiesForRole("SITE_GREEN_OBJECT")
        .OrderBy(value => value.PropertyId, StringComparer.Ordinal)
        .ToArray();
      var parameters = properties.ToDictionary(
        value => value.ParameterGuid,
        value => new NativeStage02ParameterEvidence
        {
          ParameterGuid = value.ParameterGuid,
          Exists = false,
          ContractCompatible = true,
          BindingIncludesCategory = false
        });
      var resolvedRole = new NativeStage02RoleMatchResult(
        NativeStage02RoleMatchStatus.Matched,
        "SITE_GREEN_OBJECT",
        "Bulk",
        new[] { "SITE_GREEN_OBJECT" },
        string.Empty);
      var evidence = new NativeStage02ElementEvidence
      {
        Element = new NativeStage02ElementSnapshot
        {
          DocumentFingerprint = "doc-a",
          UniqueId = "A",
          ElementId = 42,
          Category = "OST_BuildingPad",
          CategoryName = "建筑地坪",
          ElementKind = "BuildingPad",
          ElementName = elementName,
          FamilyName = "建筑地坪",
          TypeName = "集中绿地",
          IsModelElement = true
        },
        ResolvedRoleMatch = resolvedRole,
        AssignmentMode = NativeStage02AssignmentMode.Manual,
        AssignmentSource = "Bulk",
        AssignmentAction = NativeStage02AssignmentActions.SaveManualAssignment,
        ManualCarrierEvidence = "OST_BuildingPad|BuildingPad",
        Parameters = parameters
      };
      return NativeStage02PreviewCompiler.Compile(
        new NativeStage02PreviewInput
        {
          DocumentFingerprint = "doc-a",
          ModelProfile = "总平模型",
          IdentificationMode = NativeStage02IdentificationMode.Manual,
          BulkRoleId = bulkRoleId,
          RoleOverrides = overrides,
          Conditions = new Dictionary<string, bool>(StringComparer.Ordinal)
          {
            ["site.green"] = true
          },
          Elements = new[] { evidence }
        },
        catalog);
    }

    private static NativeStage02RoleOverride Override(
      string elementUniqueId,
      string roleId)
    {
      return new NativeStage02RoleOverride
      {
        ElementUniqueId = elementUniqueId,
        RoleId = roleId
      };
    }
  }
}
