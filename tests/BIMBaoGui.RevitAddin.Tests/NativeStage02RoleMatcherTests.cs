using System;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage02;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02RoleMatcherTests
  {
    [Fact]
    public void UniqueCategoryAndKindMatchWithoutGuessingNames()
    {
      NativeStage02RoleMatchResult result = NativeStage02RoleMatcher.Match(
        Candidate("OST_Doors", "FamilyInstance", "任意门实例"),
        new[] { Role("DOOR", "OST_Doors", "FamilyInstance", "门") },
        "单体建筑—地上");
      Assert.Equal(NativeStage02RoleMatchStatus.Matched, result.Status);
      Assert.Equal("DOOR", result.RoleId);
      Assert.Equal("CATEGORY_KIND", result.MatchSource);
    }

    [Fact]
    public void SharedCategoryUsesOnlyExactNormalizedAliases()
    {
      NativeCarrierRoleDefinition[] roles =
      {
        Role("ZONE_A", "OST_GenericModel", "FamilyInstance", "Ａ 区"),
        Role("ZONE_B", "OST_GenericModel", "FamilyInstance", "B区")
      };
      NativeStage02ElementSnapshot candidate = Candidate(
        "OST_GenericModel", "FamilyInstance", "  A   区  ");
      NativeStage02RoleMatchResult result = NativeStage02RoleMatcher.Match(
        candidate, roles, "总平模型");
      Assert.Equal(NativeStage02RoleMatchStatus.Matched, result.Status);
      Assert.Equal("ZONE_A", result.RoleId);
      Assert.Equal("EXACT_ALIAS", result.MatchSource);
    }

    [Fact]
    public void SubstringEditDistanceAndPunctuationGuessingAreRejected()
    {
      NativeStage02RoleMatchResult result = NativeStage02RoleMatcher.Match(
        Candidate("OST_GenericModel", "FamilyInstance", "这是组织模型"),
        new[] { Role("ORGANIZATION", "OST_GenericModel", "FamilyInstance", "组织") },
        "总平模型");
      Assert.Equal(NativeStage02RoleMatchStatus.NameNotMatched, result.Status);
      Assert.Empty(result.RoleId);
    }

    [Fact]
    public void MultipleExactMatchesAreReportedAsAmbiguous()
    {
      NativeCarrierRoleDefinition[] roles =
      {
        Role("ROLE_A", "OST_GenericModel", "FamilyInstance", "共享角色"),
        Role("ROLE_B", "OST_GenericModel", "FamilyInstance", "共享角色")
      };
      NativeStage02RoleMatchResult result = NativeStage02RoleMatcher.Match(
        Candidate("OST_GenericModel", "FamilyInstance", "共享角色"),
        roles, "总平模型");
      Assert.Equal(NativeStage02RoleMatchStatus.NameAmbiguous, result.Status);
      Assert.Equal(new[] { "ROLE_A", "ROLE_B" }, result.CandidateRoleIds);
    }

    [Fact]
    public void ExplicitAssignedRoleWinsOnlyInsideTheCompatibleCandidateSet()
    {
      NativeStage02ElementSnapshot candidate = Candidate(
        "OST_GenericModel", "FamilyInstance", "未知名称");
      candidate.AssignedRoleId = "ROLE_B";
      NativeCarrierRoleDefinition[] roles =
      {
        Role("ROLE_A", "OST_GenericModel", "FamilyInstance", "A"),
        Role("ROLE_B", "OST_GenericModel", "FamilyInstance", "B")
      };
      NativeStage02RoleMatchResult accepted = NativeStage02RoleMatcher.Match(
        candidate, roles, "总平模型");
      Assert.Equal(NativeStage02RoleMatchStatus.Matched, accepted.Status);
      Assert.Equal("ROLE_B", accepted.RoleId);
      Assert.Equal("ASSIGNED_ROLE", accepted.MatchSource);

      candidate.AssignedRoleId = "ROLE_OUTSIDE";
      NativeStage02RoleMatchResult rejected = NativeStage02RoleMatcher.Match(
        candidate, roles, "总平模型");
      Assert.Equal(NativeStage02RoleMatchStatus.AssignedRoleConflict, rejected.Status);
    }

    [Fact]
    public void VerifiedManualRoleCanBypassAutomaticCarrierSetOnlyForManualPolicy()
    {
      NativeStage02ElementSnapshot buildingPad = Candidate(
        "OST_BuildingPad", "BuildingPad", "集中绿地");
      buildingPad.AssignedRoleId = "SITE_GREEN_OBJECT";
      NativeCarrierRoleDefinition manual = Role(
        "SITE_GREEN_OBJECT", "", "", "绿地");
      manual.RevitCategories = Array.Empty<string>();
      manual.AllowedElementKinds = Array.Empty<string>();
      manual.SelectionPolicy = "MANUAL_SEMANTIC_ASSIGNMENT";

      NativeStage02RoleMatchResult result = NativeStage02RoleMatcher.Match(
        buildingPad, new[] { manual }, "总平模型");

      Assert.Equal(NativeStage02RoleMatchStatus.Matched, result.Status);
      Assert.Equal("SITE_GREEN_OBJECT", result.RoleId);
      Assert.Equal("VERIFIED_MANUAL_ASSIGNMENT", result.MatchSource);
    }

    [Fact]
    public void ModelProfileCategoryAndElementKindAreFailClosed()
    {
      NativeCarrierRoleDefinition role = Role(
        "DOOR", "OST_Doors", "FamilyInstance", "门");
      role.ModelFileTypes = new[] { "单体建筑—地上" };
      Assert.Equal(
        NativeStage02RoleMatchStatus.NotApplicable,
        NativeStage02RoleMatcher.Match(
          Candidate("OST_Doors", "FamilyInstance", "门"),
          new[] { role }, "总平模型").Status);
      Assert.Equal(
        NativeStage02RoleMatchStatus.NotApplicable,
        NativeStage02RoleMatcher.Match(
          Candidate("OST_Doors", "Wall", "门"),
          new[] { role }, "单体建筑—地上").Status);
    }

    private static NativeCarrierRoleDefinition Role(
      string roleId, string category, string kind, params string[] nameAliases)
    {
      return new NativeCarrierRoleDefinition
      {
        RoleId = roleId,
        DisplayName = roleId,
        IfcEntity = "IfcBuildingElementProxy",
        ModelFileTypes = new[] { "总平模型", "单体建筑—地上", "单体建筑—地下" },
        RevitCategories = string.IsNullOrEmpty(category) ? Array.Empty<string>() : new[] { category },
        AllowedElementKinds = string.IsNullOrEmpty(kind) ? Array.Empty<string>() : new[] { kind },
        NameAliases = nameAliases ?? Array.Empty<string>(),
        FamilyAliases = Array.Empty<string>(),
        TypeAliases = Array.Empty<string>(),
        SelectionPolicy = "ENTITY_ROLE_SELECTION",
        IfcOwnerStrategy = "BY_EXPORT_GUID"
      };
    }

    private static NativeStage02ElementSnapshot Candidate(
      string category, string kind, string name)
    {
      return new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = new string('a', 64),
        UniqueId = "uid-1",
        ElementId = 1,
        Category = category,
        ElementKind = kind,
        ElementName = name,
        FamilyName = string.Empty,
        TypeName = string.Empty,
        IsModelElement = true
      };
    }
  }
}
