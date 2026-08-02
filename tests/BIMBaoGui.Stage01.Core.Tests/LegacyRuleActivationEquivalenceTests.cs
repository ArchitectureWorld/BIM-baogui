using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Context;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class LegacyRuleActivationEquivalenceTests
  {
    private static readonly string[] ExpectedConditionIds =
    {
      "site.civil_defense",
      "site.fire_field",
      "site.fire_lane",
      "site.green",
      "site.internal_roads",
      "site.other_land",
      "site.outdoor_parking",
      "site.road_centerline",
      "site.road_redline",
      "site.structures"
    };

    [Fact]
    public void Compile_ExactlyMatchesCanonicalSourceForEveryLegacyProfile()
    {
      RuleSource source = LoadUniqueRuleSource();
      string[] expectedNotApplicable = source.conditions
        .Where(condition => !string.IsNullOrWhiteSpace(condition.activationRuleId))
        .Select(condition => condition.activationRuleId)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

      foreach (RuleProfile profile in source.modelProfiles)
      {
        string[] expectedActivated = profile.activationRuleIds
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray();
        var emptyConditions = new Dictionary<string, bool>(StringComparer.Ordinal);

        RuleActivationResult actual = RuleActivationCatalog.Compile(
          profile.profileId,
          emptyConditions);

        Assert.Equal(expectedActivated, actual.Activated.ToArray());
        Assert.Equal(expectedNotApplicable, actual.NotApplicable.ToArray());
      }
    }

    [Fact]
    public void Compile_ActivatesExactCanonicalRuleForEveryLegacyCondition()
    {
      RuleSource source = LoadUniqueRuleSource();
      RuleProfile siteProfile = Assert.Single(
        source.modelProfiles,
        profile => profile.profileId == "总平模型");
      RuleCondition[] activationConditions = source.conditions
        .Where(condition => !string.IsNullOrWhiteSpace(condition.activationRuleId))
        .ToArray();

      Assert.Equal(10, activationConditions.Length);
      Assert.Equal(
        activationConditions.Length,
        activationConditions
          .Select(condition => condition.conditionId)
          .Distinct(StringComparer.Ordinal)
          .Count());
      Assert.Equal(
        activationConditions.Length,
        activationConditions
          .Select(condition => condition.activationRuleId)
          .Distinct(StringComparer.Ordinal)
          .Count());

      foreach (RuleCondition enabledCondition in activationConditions)
      {
        var enabledConditions = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
          { enabledCondition.conditionId, true }
        };
        string[] expectedActivated = siteProfile.activationRuleIds
          .Concat(new[] { enabledCondition.activationRuleId })
          .Distinct(StringComparer.Ordinal)
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray();
        string[] expectedNotApplicable = activationConditions
          .Where(condition => !string.Equals(
            condition.conditionId,
            enabledCondition.conditionId,
            StringComparison.Ordinal))
          .Select(condition => condition.activationRuleId)
          .Distinct(StringComparer.Ordinal)
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray();

        RuleActivationResult actual = RuleActivationCatalog.Compile(
          siteProfile.profileId,
          enabledConditions);

        Assert.Equal(expectedActivated, actual.Activated.ToArray());
        Assert.Equal(expectedNotApplicable, actual.NotApplicable.ToArray());
        Assert.Contains(enabledCondition.activationRuleId, actual.Activated);
        Assert.DoesNotContain(
          enabledCondition.activationRuleId,
          actual.NotApplicable);
      }
    }

    [Fact]
    public void CanonicalSource_ContainsCompleteUniqueLegacyActivationFixtures()
    {
      RuleSource source = LoadUniqueRuleSource();
      string[] expectedProfileIds =
      {
        "总平模型",
        "单体建筑—地上",
        "单体建筑—地下"
      };
      RuleCondition[] activationConditions = source.conditions
        .Where(condition => !string.IsNullOrWhiteSpace(condition.activationRuleId))
        .ToArray();

      Assert.Equal(3, source.modelProfiles.Length);
      Assert.Equal(
        expectedProfileIds.OrderBy(value => value, StringComparer.Ordinal),
        source.modelProfiles
          .Select(profile => profile.profileId)
          .OrderBy(value => value, StringComparer.Ordinal));
      Assert.Equal(
        source.modelProfiles.Length,
        source.modelProfiles
          .Select(profile => profile.profileId)
          .Distinct(StringComparer.Ordinal)
          .Count());
      Assert.All(
        source.modelProfiles,
        profile =>
        {
          Assert.NotEmpty(profile.activationRuleIds);
          Assert.Equal(
            profile.activationRuleIds.Length,
            profile.activationRuleIds.Distinct(StringComparer.Ordinal).Count());
          Assert.Equal(
            profile.activationRuleIds.OrderBy(
              value => value,
              StringComparer.Ordinal),
            profile.activationRuleIds);
        });

      Assert.Equal(10, activationConditions.Length);
      Assert.Equal(
        ExpectedConditionIds,
        activationConditions
          .Select(condition => condition.conditionId)
          .OrderBy(value => value, StringComparer.Ordinal));
      Assert.Equal(
        activationConditions.Length,
        activationConditions
          .Select(condition => condition.conditionId)
          .Distinct(StringComparer.Ordinal)
          .Count());
      Assert.Equal(
        activationConditions.Length,
        activationConditions
          .Select(condition => condition.activationRuleId)
          .Distinct(StringComparer.Ordinal)
          .Count());
    }

    private static RuleSource LoadUniqueRuleSource()
    {
      string repositoryRoot = FindRepositoryRoot();
      string sourceDirectory = Path.Combine(
        repositoryRoot,
        "specs",
        "hbr-rules",
        "v1",
        "source");
      string sourcePath = Assert.Single(
        Directory.GetFiles(
          sourceDirectory,
          "hbr_rule_source.v1.json",
          SearchOption.TopDirectoryOnly));
      var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
      RuleSource source = serializer.Deserialize<RuleSource>(
        File.ReadAllText(sourcePath));

      Assert.NotNull(source);
      Assert.NotNull(source.modelProfiles);
      Assert.NotNull(source.conditions);
      return source;
    }

    private static string FindRepositoryRoot()
    {
      DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
      while (current != null)
      {
        string sourceDirectory = Path.Combine(
          current.FullName,
          "specs",
          "hbr-rules",
          "v1",
          "source");
        if (Directory.Exists(sourceDirectory))
          return current.FullName;

        current = current.Parent;
      }

      throw new DirectoryNotFoundException(
        "Could not locate the repository root containing the canonical HBR source.");
    }

    private sealed class RuleSource
    {
      public RuleProfile[] modelProfiles { get; set; }
      public RuleCondition[] conditions { get; set; }
    }

    private sealed class RuleProfile
    {
      public string profileId { get; set; }
      public string[] activationRuleIds { get; set; }
    }

    private sealed class RuleCondition
    {
      public string conditionId { get; set; }
      public string activationRuleId { get; set; }
    }
  }
}
