using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.Infrastructure;
using BIMBaoGui.Stage01.Revit;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.Stage03;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage01ConditionSchemaMigrationTests
  {
    [Fact]
    public void LegacyCanonicalPayload_RemainsIntegrityValid_ThenReceivesCurrentDefaults()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      string[] legacyConditionIds = LegacyConditionIds(database);
      Assert.Equal(10, legacyConditionIds.Length);
      var legacy = new Stage01Model();
      for (int index = 0; index < legacyConditionIds.Length; index++)
        legacy.SetCondition(legacyConditionIds[index], index % 2 == 0);
      string payload = CanonicalPayload.Build(legacy);

      Stage01StoredPayloadIntegrityDecision integrity =
        Stage01StoredPayloadIntegrityPolicy.Evaluate(
          payload,
          CanonicalPayload.Sha256(payload));

      Assert.True(integrity.Success, integrity.Message);
      Assert.Equal(payload, integrity.CanonicalPayload);

      var restored = new Stage01Model();
      Assert.True(
        Stage01PayloadCodec.TryApply(payload, restored, out string error),
        error);

      Assert.True(
        Stage01RegistryProvider.Instance.ApplyMissingConditionDefaults(restored));

      Dictionary<string, bool> expectedDefaults = database.Package.Conditions
        .ToDictionary(
          condition => condition.ConditionId,
          condition => condition.DefaultActive,
          StringComparer.Ordinal);
      Assert.Equal(
        expectedDefaults.Keys.OrderBy(value => value, StringComparer.Ordinal),
        restored.Conditions.Keys.OrderBy(value => value, StringComparer.Ordinal));
      for (int index = 0; index < legacyConditionIds.Length; index++)
        Assert.Equal(index % 2 == 0, restored.Conditions[legacyConditionIds[index]]);
      foreach (KeyValuePair<string, bool> expected in expectedDefaults)
      {
        if (!legacyConditionIds.Contains(expected.Key, StringComparer.Ordinal))
          Assert.Equal(expected.Value, restored.Conditions[expected.Key]);
      }

      string once = CanonicalPayload.Build(restored);
      Assert.False(
        Stage01RegistryProvider.Instance.ApplyMissingConditionDefaults(restored));
      Assert.Equal(once, CanonicalPayload.Build(restored));
    }

    [Fact]
    public void LegacyConditionPayload_FailsStage03UntilCurrentDefaultsAreApplied()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      var legacy = new Stage01Model();
      legacy.SetValue(
        Stage01Keys.ModelFileType,
        PlanningTargetRequirementPolicy.SiteModel);
      string[] legacyConditionIds = LegacyConditionIds(database);
      Assert.Equal(10, legacyConditionIds.Length);
      foreach (string conditionId in legacyConditionIds)
        legacy.SetCondition(conditionId, false);
      string payload = CanonicalPayload.Build(legacy);
      var restored = new Stage01Model();
      Assert.True(
        Stage01PayloadCodec.TryApply(payload, restored, out string error),
        error);

      HBRFileContext before = CreateContext(restored);
      Stage03ActivationStateDecision beforeDecision = Evaluate(database, before);
      string[] missing = database.Package.Conditions
        .Select(condition => condition.ConditionId)
        .Except(restored.Conditions.Keys, StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

      Assert.Equal(4, missing.Length);
      Assert.False(beforeDecision.Success);
      Assert.All(missing, conditionId =>
        Assert.Contains(conditionId, beforeDecision.Message));

      Assert.True(
        Stage01RegistryProvider.Instance.ApplyMissingConditionDefaults(restored));
      HBRFileContext after = CreateContext(restored);
      Stage03ActivationStateDecision afterDecision = Evaluate(database, after);

      Assert.True(afterDecision.Success, afterDecision.Message);
    }

    [Fact]
    public void RestoreAndCommitPaths_ApplyConditionDefaultsBeforeConsumption()
    {
      string root = RepositoryRoot();
      string component = File.ReadAllText(Path.Combine(
        root,
        @"src\BIMBaoGui.Stage01\Stage01Component.cs"));
      string revitService = File.ReadAllText(Path.Combine(
        root,
        @"src\BIMBaoGui.Stage01\Revit\Stage01RevitService.cs"));

      string solve = Slice(
        component,
        "protected override void SolveInstance",
        "internal IReadOnlyList<ConditionDefinition> GetVisibleConditions");
      AssertOrdered(
        solve,
        "EnsureSystemValues();",
        "HBRFileContextFactory.Create(");

      string read = Slice(
        component,
        "public override bool Read",
        "private void ReadLegacyForm");
      AssertOrdered(
        read,
        "Stage01PayloadCodec.TryApply",
        "EnsureSystemValues();");

      string automaticRestore = Slice(
        component,
        "private void TryAutomaticallyLoadStoredPayload",
        "private void MergeOperationFailureIntoSnapshot");
      AssertOrdered(
        automaticRestore,
        "Stage01PayloadCodec.TryApply",
        "EnsureSystemValues();");

      string ensure = Slice(
        component,
        "private void EnsureSystemValues",
        "private bool IsInitializationPassed");
      Assert.Contains(
        "_registry.ApplyMissingConditionDefaults(_model);",
        ensure);

      string populate = Slice(
        revitService,
        "private static IReadOnlyList<string> PopulateModelFromDocumentCore",
        "public static bool EnqueueCommit");
      AssertOrdered(
        populate,
        "Stage01PayloadCodec.TryApply",
        "Stage01RegistryProvider.Instance.ApplyMissingConditionDefaults(model);",
        "GetProjectPosition(");

      string commit = Slice(
        revitService,
        "private static CommitResult Commit",
        "private static Stage01StorageDecision EvaluateStorage");
      AssertOrdered(
        commit,
        "Stage01RegistryProvider.Instance.ApplyMissingConditionDefaults(model);",
        "Stage01Validator.Validate(",
        "CanonicalPayload.Build(model)");
    }

    private static string[] LegacyConditionIds(HbrRuleDatabase database)
    {
      return RuleActivationCatalog.FromDatabase(database).ConditionRules.Keys
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
    }

    private static HBRFileContext CreateContext(Stage01Model model)
    {
      return HBRFileContextFactory.Create(
        model,
        new RevitDocumentSnapshot
        {
          DocumentPath = @"C:\tests\legacy-condition-model.rvt",
          DocumentTitle = "legacy-condition-model",
          RevitVersion = "2020"
        },
        true);
    }

    private static Stage03ActivationStateDecision Evaluate(
      HbrRuleDatabase database,
      HBRFileContext context)
    {
      return Stage03ActivationStatePolicy.Evaluate(
        database,
        context.ModelFileType,
        context.ProjectConditions,
        context.ActivatedRuleIds,
        context.NotApplicableRuleIds);
    }

    private static string RepositoryRoot()
    {
      string projectDirectory = Path.GetFullPath(Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        @"..\..\.."));
      return Path.GetFullPath(Path.Combine(projectDirectory, @"..\.."));
    }

    private static string Slice(string source, string start, string end)
    {
      int startIndex = source.IndexOf(start, StringComparison.Ordinal);
      int endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
      Assert.True(startIndex >= 0, "未找到源码起点：" + start);
      Assert.True(endIndex > startIndex, "未找到源码终点：" + end);
      return source.Substring(startIndex, endIndex - startIndex);
    }

    private static void AssertOrdered(string source, params string[] tokens)
    {
      int previous = -1;
      foreach (string token in tokens)
      {
        int current = source.IndexOf(token, previous + 1, StringComparison.Ordinal);
        Assert.True(current > previous, "源码顺序缺失：" + token);
        previous = current;
      }
    }
  }
}
