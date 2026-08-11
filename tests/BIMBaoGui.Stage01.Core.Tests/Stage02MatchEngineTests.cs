using System;
using System.Collections.Generic;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.Stage02;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage02MatchEngineTests
  {
    private const string ProfileId = "单体建筑—地上";

    [Fact]
    public void Match_requires_role_hint_when_category_has_multiple_active_roles()
    {
      var engine = new Stage02MatchEngine(HbrRuleDatabase.Current, ProfileId);

      Stage02MatchResult result = engine.Match(Element(
        "OST_ProjectInformation",
        string.Empty,
        string.Empty,
        "未分类信息"));

      Assert.False(result.Success);
      Assert.Contains(result.Blockers, x =>
        x.Code == Stage02Codes.AmbiguousCarrier
        && x.Message.Contains("多个载体角色"));
    }

    [Theory]
    [InlineData("项目", "PROJECT")]
    [InlineData("场地", "SITE")]
    [InlineData("建筑", "BUILDING")]
    public void Match_uses_name_alias_to_narrow_shared_category_candidates(
      string elementName,
      string expectedRoleId)
    {
      var engine = new Stage02MatchEngine(HbrRuleDatabase.Current, ProfileId);

      Stage02MatchResult result = engine.Match(Element(
        "OST_ProjectInformation",
        string.Empty,
        string.Empty,
        elementName));

      Assert.True(result.Success);
      Assert.Equal(expectedRoleId, result.RoleId);
      Assert.Equal(Stage02MatchSources.NameAlias, result.MatchSource);
    }

    [Fact]
    public void Match_does_not_fuzzy_match_alias_substrings()
    {
      var engine = new Stage02MatchEngine(HbrRuleDatabase.Current, ProfileId);

      Stage02MatchResult result = engine.Match(Element(
        "OST_ProjectInformation",
        string.Empty,
        string.Empty,
        "建筑主体"));

      Assert.False(result.Success);
      Assert.Contains(result.Blockers, x =>
        x.Code == Stage02Codes.AmbiguousCarrier);
    }

    [Fact]
    public void Match_prefers_explicit_hint_over_saved_role_category_and_alias()
    {
      var engine = new Stage02MatchEngine(HbrRuleDatabase.Current, ProfileId);

      Stage02MatchResult result = engine.Match(
        Element("OST_ProjectInformation", string.Empty, string.Empty, "建筑"),
        "SITE",
        "PROJECT");

      Assert.True(result.Success);
      Assert.Equal("SITE", result.RoleId);
      Assert.Equal(Stage02MatchSources.RoleHint, result.MatchSource);
    }

    [Fact]
    public void Match_rejects_explicit_hint_outside_category_allowed_roles()
    {
      var engine = new Stage02MatchEngine(HbrRuleDatabase.Current, ProfileId);

      Stage02MatchResult result = engine.Match(
        Element("OST_ProjectInformation", string.Empty, string.Empty, "项目"),
        "SLAB");

      Assert.False(result.Success);
      Assert.Contains(result.Blockers, x =>
        x.Code == Stage02Codes.CarrierCategoryMismatch);
    }

    [Fact]
    public void Match_prefers_saved_role_metadata_over_category_and_alias()
    {
      var engine = new Stage02MatchEngine(HbrRuleDatabase.Current, ProfileId);

      Stage02MatchResult result = engine.Match(
        Element("OST_ProjectInformation", string.Empty, string.Empty, "建筑"),
        null,
        "PROJECT");

      Assert.True(result.Success);
      Assert.Equal("PROJECT", result.RoleId);
      Assert.Equal(Stage02MatchSources.SavedRole, result.MatchSource);
    }

    [Fact]
    public void Match_prefers_unique_category_match_over_conflicting_name_alias()
    {
      var engine = new Stage02MatchEngine(HbrRuleDatabase.Current, ProfileId);

      Stage02MatchResult result = engine.Match(Element(
        "OST_Walls",
        string.Empty,
        string.Empty,
        "窗"));

      Assert.True(result.Success);
      Assert.Equal("WALL", result.RoleId);
      Assert.Equal(Stage02MatchSources.Category, result.MatchSource);
    }

    [Fact]
    public void Match_rejects_alias_when_revit_category_is_incompatible()
    {
      var engine = new Stage02MatchEngine(HbrRuleDatabase.Current, ProfileId);

      Stage02MatchResult result = engine.Match(Element(
        "OST_Unknown",
        "普通族",
        "标准窗类型",
        "未命名"));

      Assert.False(result.Success);
      Assert.Contains(result.Blockers, x =>
        x.Code == Stage02Codes.CarrierCategoryMismatch);
    }

    [Fact]
    public void Match_never_guesses_when_aliases_leave_multiple_candidates()
    {
      var engine = new Stage02MatchEngine(HbrRuleDatabase.Current, ProfileId);

      Stage02MatchResult result = engine.Match(Element(
        "OST_ProjectInformation",
        "项目建筑",
        string.Empty,
        "未命名"));

      Assert.False(result.Success);
      Assert.Contains(result.Blockers, x =>
        x.Code == Stage02Codes.AmbiguousCarrier);
    }

    [Fact]
    public void Match_rejects_missing_category_instead_of_guessing_from_kind()
    {
      var engine = new Stage02MatchEngine(HbrRuleDatabase.Current, ProfileId);

      Stage02MatchResult result = engine.Match(Element(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        "Roof"));

      Assert.False(result.Success);
      Assert.Contains(result.Blockers, x =>
        x.Code == Stage02Codes.CarrierCategoryMismatch);
    }

    [Theory]
    [InlineData("ROOF", null)]
    [InlineData(null, "ROOF")]
    public void Match_rejects_declared_role_when_category_is_missing(
      string roleHint,
      string savedRoleId)
    {
      var engine = new Stage02MatchEngine(HbrRuleDatabase.Current, ProfileId);

      Stage02MatchResult result = engine.Match(
        Element(
          string.Empty,
          string.Empty,
          string.Empty,
          string.Empty,
          "Roof"),
        roleHint,
        savedRoleId);

      Assert.False(result.Success);
      Assert.Contains(result.Blockers, x =>
        x.Code == Stage02Codes.CarrierCategoryMismatch);
    }

    [Fact]
    public void Match_fails_fast_for_unknown_profile_role_or_reference()
    {
      Stage02MatchResult profileResult = new Stage02MatchEngine(
        HbrRuleDatabase.Current,
        "不存在的模型").Match(Element("OST_Walls"));
      Stage02MatchResult roleResult = new Stage02MatchEngine(
        HbrRuleDatabase.Current,
        ProfileId).Match(Element("OST_Walls"), "NOT_A_ROLE");
      Stage02MatchResult referenceResult = new Stage02MatchEngine(
        HbrRuleDatabase.Current,
        ProfileId).Match(new Stage02ElementReference(
          "doc-fingerprint",
          101,
          string.Empty,
          "OST_Walls",
          string.Empty,
          string.Empty,
          string.Empty));

      Assert.Contains(profileResult.Blockers, x =>
        x.Code == Stage02Codes.UnknownModelProfile);
      Assert.Contains(roleResult.Blockers, x =>
        x.Code == Stage02Codes.UnknownCarrierRole);
      Assert.Contains(referenceResult.Blockers, x =>
        x.Code == Stage02Codes.InvalidElementReference);
    }

    [Fact]
    public void Match_result_blockers_are_read_only_defensive_copies()
    {
      var source = new List<Stage02Blocker>
      {
        new Stage02Blocker("TEST", "测试")
      };
      var result = Stage02MatchResult.Blocked(source);
      source.Clear();

      Assert.Single(result.Blockers);
      Assert.Throws<NotSupportedException>(() =>
        ((IList<Stage02Blocker>)result.Blockers).Add(
          new Stage02Blocker("OTHER", "其他")));
    }

    [Fact]
    public void Match_context_constructor_derives_profile_and_rule_identity()
    {
      var engine = new Stage02MatchEngine(
        HbrRuleDatabase.Current,
        Stage02PreviewCompilerTests.FileContext());

      Stage02MatchResult result = engine.Match(Element("OST_Walls"));

      Assert.True(result.Success);
      Assert.Equal("WALL", result.RoleId);
    }

    [Fact]
    public void Match_context_constructor_rejects_tampered_hash_or_schema()
    {
      HBRFileContext valid = Stage02PreviewCompilerTests.FileContext();
      HBRFileContext tamperedHash = valid.WithHash("tampered-context-hash");
      HBRFileContext wrongSchema =
        Stage02PreviewCompilerTests.FileContext("other-schema");

      Stage02MatchResult hashResult = new Stage02MatchEngine(
        HbrRuleDatabase.Current,
        tamperedHash).Match(Element("OST_Walls"));
      Stage02MatchResult schemaResult = new Stage02MatchEngine(
        HbrRuleDatabase.Current,
        wrongSchema).Match(Element("OST_Walls"));

      Assert.Contains(hashResult.Blockers, blocker =>
        blocker.Code == Stage02Codes.InvalidFileContext);
      Assert.Contains(schemaResult.Blockers, blocker =>
        blocker.Code == Stage02Codes.InvalidFileContext);
    }

    private static Stage02ElementReference Element(
      string category,
      string familyName = "",
      string typeName = "",
      string elementName = "",
      string elementKind = null)
    {
      return new Stage02ElementReference(
        "doc-fingerprint",
        "测试文档",
        101,
        "uid-101",
        category,
        elementKind ?? KindForCategory(category),
        familyName,
        typeName,
        elementName);
    }

    private static string KindForCategory(string category)
    {
      switch (category)
      {
        case "OST_ProjectInformation": return "ProjectInformation";
        case "OST_Walls": return "Wall";
        case "OST_Roofs": return "Roof";
        case "OST_Windows": return "FamilyInstance";
        default: return "Unknown";
      }
    }
  }
}
