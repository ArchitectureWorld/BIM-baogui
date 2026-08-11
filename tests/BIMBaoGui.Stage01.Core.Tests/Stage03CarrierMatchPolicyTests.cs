using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.Stage03;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage03CarrierMatchPolicyTests
  {
    [Fact]
    public void Evaluate_RejectsUserSelectedRoleWithoutSavedRoleEvenWhenAliasMatches()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      HbrCarrierRole role = database.CarrierRolesById["ORGANIZATION"];
      var candidate = OrganizationCandidate(savedRoleId: string.Empty);

      Stage03CarrierMatchDecision decision =
        Stage03CarrierMatchPolicy.Evaluate(
          role,
          candidate,
          database.Package.CarrierRoles);

      Assert.False(decision.Accepted);
      Assert.Equal(Stage03FieldStatus.CarrierNameMismatch, decision.Status);
    }

    [Fact]
    public void Evaluate_AcceptsUserSelectedRoleWithMatchingSavedRole()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      HbrCarrierRole role = database.CarrierRolesById["ORGANIZATION"];
      var candidate = OrganizationCandidate(savedRoleId: "ORGANIZATION");

      Stage03CarrierMatchDecision decision =
        Stage03CarrierMatchPolicy.Evaluate(
          role,
          candidate,
          database.Package.CarrierRoles);

      Assert.True(decision.Accepted, decision.Message);
      Assert.Equal(Stage03FieldStatus.Pass, decision.Status);
      Assert.Equal("SAVED_ROLE", decision.MatchSource);
    }

    [Fact]
    public void Evaluate_AcceptsOrdinaryRoleWhenCategoryAndKindAreUnique()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      HbrCarrierRole role = database.CarrierRolesById["WALL"];
      var candidate = new Stage03CarrierCandidateSnapshot
      {
        UniqueId = "wall-element",
        Category = "OST_Walls",
        ElementKind = "Wall",
        ElementName = "任意墙"
      };

      Stage03CarrierMatchDecision decision =
        Stage03CarrierMatchPolicy.Evaluate(
          role,
          candidate,
          database.Package.CarrierRoles);

      Assert.True(decision.Accepted, decision.Message);
      Assert.Equal("CATEGORY", decision.MatchSource);
    }

    [Fact]
    public void Evaluate_AcceptsProjectInformationSingleEntityRoles()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      HbrCarrierRole[] roles = new[] { "PROJECT", "SITE", "BUILDING" }
        .Select(roleId => database.CarrierRolesById[roleId])
        .ToArray();
      var candidate = new Stage03CarrierCandidateSnapshot
      {
        UniqueId = "project-information",
        Category = "OST_ProjectInformation",
        ElementKind = "ProjectInformation",
        ElementName = "项目信息"
      };

      foreach (HbrCarrierRole role in roles)
      {
        Stage03CarrierMatchDecision decision =
          Stage03CarrierMatchPolicy.Evaluate(role, candidate, roles);

        Assert.True(decision.Accepted, role.RoleId + ": " + decision.Message);
        Assert.Equal("SINGLE_ENTITY_BY_TYPE", decision.MatchSource);
      }
    }

    [Fact]
    public void Evaluate_UsesSavedRoleToDisambiguateSharedCategory()
    {
      HbrCarrierRole first = Role("FIRST", "甲别名");
      HbrCarrierRole second = Role("SECOND", "乙别名");
      HbrCarrierRole[] roles = { first, second };
      Stage03CarrierCandidateSnapshot candidate = SharedCandidate(
        elementName: "无匹配名称",
        savedRoleId: "SECOND");

      Stage03CarrierMatchDecision accepted =
        Stage03CarrierMatchPolicy.Evaluate(second, candidate, roles);
      Stage03CarrierMatchDecision rejected =
        Stage03CarrierMatchPolicy.Evaluate(first, candidate, roles);

      Assert.True(accepted.Accepted, accepted.Message);
      Assert.Equal("SAVED_ROLE", accepted.MatchSource);
      Assert.False(rejected.Accepted);
      Assert.Equal(Stage03FieldStatus.CarrierNameMismatch, rejected.Status);
    }

    [Fact]
    public void Evaluate_UsesUniqueAliasToDisambiguateSharedCategory()
    {
      HbrCarrierRole first = Role("FIRST", "甲别名");
      HbrCarrierRole second = Role("SECOND", "乙别名");
      HbrCarrierRole[] roles = { first, second };
      Stage03CarrierCandidateSnapshot candidate = SharedCandidate(
        elementName: " 乙别名 ",
        savedRoleId: string.Empty);

      Stage03CarrierMatchDecision accepted =
        Stage03CarrierMatchPolicy.Evaluate(second, candidate, roles);
      Stage03CarrierMatchDecision rejected =
        Stage03CarrierMatchPolicy.Evaluate(first, candidate, roles);

      Assert.True(accepted.Accepted, accepted.Message);
      Assert.Equal("NAME_ALIAS", accepted.MatchSource);
      Assert.False(rejected.Accepted);
      Assert.Equal(Stage03FieldStatus.CarrierNameMismatch, rejected.Status);
    }

    [Fact]
    public void Evaluate_RejectsSharedCategoryWhenNoAliasMatches()
    {
      HbrCarrierRole first = Role("FIRST", "甲别名");
      HbrCarrierRole second = Role("SECOND", "乙别名");
      Stage03CarrierCandidateSnapshot candidate = SharedCandidate(
        elementName: "无匹配名称",
        savedRoleId: string.Empty);

      Stage03CarrierMatchDecision decision =
        Stage03CarrierMatchPolicy.Evaluate(
          first,
          candidate,
          new[] { first, second });

      Assert.False(decision.Accepted);
      Assert.Equal(Stage03FieldStatus.CarrierNameMismatch, decision.Status);
    }

    [Fact]
    public void Evaluate_RejectsSharedCategoryWhenAliasesAreAmbiguous()
    {
      HbrCarrierRole first = Role("FIRST", "共用别名");
      HbrCarrierRole second = Role("SECOND", "共用别名");
      Stage03CarrierCandidateSnapshot candidate = SharedCandidate(
        elementName: "共用别名",
        savedRoleId: string.Empty);

      Stage03CarrierMatchDecision decision =
        Stage03CarrierMatchPolicy.Evaluate(
          first,
          candidate,
          new[] { first, second });

      Assert.False(decision.Accepted);
      Assert.Equal(Stage03FieldStatus.AmbiguousCarrier, decision.Status);
    }

    [Fact]
    public void Aggregation_ReportsAmbiguousRejectionAlongsideAcceptedCandidate()
    {
      var accepted = new Stage03CarrierMatchDecision(
        true,
        Stage03FieldStatus.Pass,
        "NAME_ALIAS",
        string.Empty);
      var ambiguous = new Stage03CarrierMatchDecision(
        false,
        Stage03FieldStatus.AmbiguousCarrier,
        string.Empty,
        "共享类别无法唯一匹配。");

      Assert.False(
        Stage03CarrierScanAggregationPolicy
          .ShouldReportAlongsideAccepted(accepted));
      Assert.True(
        Stage03CarrierScanAggregationPolicy
          .ShouldReportAlongsideAccepted(ambiguous));
    }

    [Fact]
    public void Aggregation_DoesNotReportUnrelatedNameMismatchAlongsideAcceptedCandidate()
    {
      var mismatch = new Stage03CarrierMatchDecision(
        false,
        Stage03FieldStatus.CarrierNameMismatch,
        string.Empty,
        "属于另一个角色。");

      Assert.False(
        Stage03CarrierScanAggregationPolicy
          .ShouldReportAlongsideAccepted(mismatch));
    }

    private static Stage03CarrierCandidateSnapshot OrganizationCandidate(
      string savedRoleId)
    {
      return new Stage03CarrierCandidateSnapshot
      {
        UniqueId = "organization-element",
        Category = "OST_GenericModel",
        ElementKind = "FamilyInstance",
        ElementName = "组织",
        FamilyName = "测试组织族",
        TypeName = "测试组织类型",
        SavedRoleId = savedRoleId
      };
    }

    private static Stage03CarrierCandidateSnapshot SharedCandidate(
      string elementName,
      string savedRoleId)
    {
      return new Stage03CarrierCandidateSnapshot
      {
        UniqueId = "shared-element",
        Category = "OST_GenericModel",
        ElementKind = "FamilyInstance",
        ElementName = elementName,
        FamilyName = "无匹配族",
        TypeName = "无匹配类型",
        SavedRoleId = savedRoleId
      };
    }

    private static HbrCarrierRole Role(string roleId, string nameAlias)
    {
      return new HbrCarrierRole(
        new HbrCarrierRoleDto
        {
          roleId = roleId,
          displayName = roleId,
          modelFileTypes = new List<string> { "SITE_MODEL" },
          ifcEntity = "IfcBuildingElementProxy",
          revitCategories = new List<string> { "OST_GenericModel" },
          allowedElementKinds = new List<string> { "FamilyInstance" },
          nameAliases = new List<string> { nameAlias },
          familyAliases = new List<string>(),
          typeAliases = new List<string>(),
          cardinality = new HbrCardinalityDto { min = 0, max = null },
          selectionPolicy = "ENTITY_ROLE_SELECTION",
          ifcOwnerStrategy = "BY_EXPORT_GUID"
        },
        "test.carrierRole");
    }
  }
}
