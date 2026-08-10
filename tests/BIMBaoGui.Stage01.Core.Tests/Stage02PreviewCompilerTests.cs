using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.Stage02;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage02PreviewCompilerTests
  {
    private const string ProfileId = "单体建筑—地上";
    private const string ConditionalId = "building.roof";

    [Fact]
    public void Preview_sorts_by_unique_id_then_property_id_and_is_byte_deterministic()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      HbrRuleProperty wallA = PropertiesFor("WALL").ElementAt(0);
      HbrRuleProperty wallB = PropertiesFor("WALL").ElementAt(1);
      Stage02MatchedElement first = Matched(
        "uid-b",
        202,
        "WALL",
        Operation(wallB, "old-b", "new-b"),
        Operation(wallA, "old-a", "new-a"));
      Stage02MatchedElement second = Matched(
        "uid-a",
        101,
        "WALL",
        Operation(wallA, "old-c", "new-c"));
      var compiler = new Stage02PreviewCompiler(database);

      Stage02Preview left = compiler.Compile(Request(first, second));
      Stage02Preview right = compiler.Compile(Request(second, first));

      Assert.Equal(new[] { "uid-a", "uid-b" },
        left.Elements.Select(x => x.Element.UniqueId));
      Assert.Equal(
        left.Elements[1].Operations.Select(x => x.PropertyId)
          .OrderBy(x => x, StringComparer.Ordinal),
        left.Elements[1].Operations.Select(x => x.PropertyId));
      Assert.Equal(
        Encoding.UTF8.GetBytes(left.CanonicalPayload),
        Encoding.UTF8.GetBytes(right.CanonicalPayload));
      Assert.Equal(left.PreviewHash, right.PreviewHash);
      Assert.Matches("^[0-9a-f]{64}$", left.PreviewHash);
    }

    [Fact]
    public void Preview_projects_database_runtime_decision_without_adding_blocker()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      HbrRuleProperty property = PropertiesFor("WALL").First();
      Stage02WriteOperation input = Operation(property, "old", "suggested");
      var compiler = new Stage02PreviewCompiler(database);

      Stage02Preview preview = compiler.Compile(Request(Matched(
        "uid-runtime",
        101,
        "WALL",
        input)));

      Stage02WriteOperation projected = preview.Elements.Single()
        .Operations.Single(x => string.Equals(
          x.PropertyId,
          property.PropertyId,
          StringComparison.Ordinal));
      HbrRuntimeStatusDecision expected =
        database.GetRuntimeStatusDecision(property);
      Assert.Equal(expected.Status, projected.RuntimeStatus);
      Assert.Equal(expected.ReasonCode, projected.RuntimeBlockCode);
      Assert.Equal(expected.Reason, projected.RuntimeBlockReason);
      Assert.Equal(input.Blockers.Count, projected.Blockers.Count);
    }

    [Fact]
    public void Preview_runtime_decision_is_canonical_and_overwrites_forged_input()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      HbrRuleProperty property = PropertiesFor("WALL").First();
      Stage02WriteOperation forged = Operation(property, "old", "suggested")
        .WithRuntimeDecision(
          "SUPPORTED",
          "FORGED_RUNTIME_CODE",
          "伪造运行原因");
      Stage02PreviewRequest request = Request(Matched(
        "uid-runtime-forged",
        102,
        "WALL",
        forged));

      Stage02Preview preview = new Stage02PreviewCompiler(database)
        .Compile(request);

      Stage02WriteOperation projected = preview.Elements.Single()
        .Operations.Single(x => string.Equals(
          x.PropertyId,
          property.PropertyId,
          StringComparison.Ordinal));
      HbrRuntimeStatusDecision expected =
        database.GetRuntimeStatusDecision(property);
      Assert.Equal(expected.Status, projected.RuntimeStatus);
      Assert.Equal(expected.ReasonCode, projected.RuntimeBlockCode);
      Assert.Equal(expected.Reason, projected.RuntimeBlockReason);
      Assert.NotEqual(forged.RuntimeStatus, projected.RuntimeStatus);
      Assert.NotEqual(forged.RuntimeBlockCode, projected.RuntimeBlockCode);
      Assert.NotEqual(forged.RuntimeBlockReason, projected.RuntimeBlockReason);

      Stage02WriteOperation[] mutations =
      {
        projected.WithRuntimeDecision(
          projected.RuntimeStatus + "_ALTERED",
          projected.RuntimeBlockCode,
          projected.RuntimeBlockReason),
        projected.WithRuntimeDecision(
          projected.RuntimeStatus,
          projected.RuntimeBlockCode + "_ALTERED",
          projected.RuntimeBlockReason),
        projected.WithRuntimeDecision(
          projected.RuntimeStatus,
          projected.RuntimeBlockCode,
          projected.RuntimeBlockReason + "_ALTERED")
      };
      foreach (Stage02WriteOperation mutation in mutations)
      {
        Stage02MatchedElement changedElement = preview.Elements.Single()
          .WithOperations(preview.Elements.Single().Operations.Select(x =>
            string.Equals(
              x.PropertyId,
              mutation.PropertyId,
              StringComparison.Ordinal)
                ? mutation
                : x));
        string changedCanonical = Stage02Canonicalizer.BuildPreview(
          request,
          new[] { changedElement });
        Assert.NotEqual(preview.CanonicalPayload, changedCanonical);
        Assert.NotEqual(
          preview.PreviewHash,
          Stage02Hash.Sha256(changedCanonical));
      }
    }

    [Fact]
    public void Runtime_decision_survives_existing_operation_copy_methods()
    {
      HbrRuleProperty property = PropertiesFor("WALL").First();
      Stage02WriteOperation stamped = Operation(property, "old", "suggested")
        .WithRuntimeDecision(
          "NOT_IMPLEMENTED",
          "OWNER_STRATEGY_NOT_IMPLEMENTED",
          "运行原因");

      Stage02WriteOperation metadataCopy = stamped.WithRuleMetadata(
        stamped.ObservedState,
        property.Revit.BindingScope,
        property.Revit.StorageType,
        property.Revit.ParameterType,
        property.Requirement.Level,
        property.Requirement.ConditionId);
      Stage02WriteOperation observedCopy = metadataCopy.WithObservedState(
        metadataCopy.ObservedState);

      foreach (Stage02WriteOperation copy in new[]
      {
        metadataCopy,
        observedCopy
      })
      {
        Assert.Equal(stamped.RuntimeStatus, copy.RuntimeStatus);
        Assert.Equal(stamped.RuntimeBlockCode, copy.RuntimeBlockCode);
        Assert.Equal(stamped.RuntimeBlockReason, copy.RuntimeBlockReason);
      }
    }

    [Fact]
    public void Preview_hash_changes_for_rule_sha_old_suggested_role_or_unique_id()
    {
      HbrRuleProperty property = PropertiesFor("WALL").First();
      Stage02Preview baseline = Compile(Request(Matched(
        "uid-1",
        1,
        "WALL",
        Operation(property, "old", "suggested"))));
      string alternateSha = FlipSha(
        HbrRuleDatabase.Current.Package.RulePackageSha256);
      HbrRuleDatabase alternateDatabase = CloneDatabaseWithRuleSha(
        alternateSha);

      Stage02Preview[] changed =
      {
        Compile(RequestWith(
          MatchedForDatabase(alternateDatabase, "uid-1", 1, "WALL",
            Operation(property, "old", "suggested")),
          ruleSha: alternateSha), alternateDatabase),
        Compile(Request(Matched("uid-1", 1, "WALL",
          Operation(property, "changed-old", "suggested")))),
        Compile(Request(Matched("uid-1", 1, "WALL",
          Operation(property, "old", "changed-suggestion")))),
        Compile(Request(Matched("uid-2", 1, "WALL",
          Operation(property, "old", "suggested"))))
      };

      Assert.All(changed, item =>
        Assert.NotEqual(baseline.PreviewHash, item.PreviewHash));

      Stage02MatchedElement project = Matched(
        "uid-role", 9, "PROJECT");
      Stage02MatchedElement site = Matched(
        "uid-role", 9, "SITE");
      Assert.NotEqual(
        Compile(Request(project)).PreviewHash,
        Compile(Request(site)).PreviewHash);
    }

    [Fact]
    public void Preview_hash_ignores_ui_only_element_id()
    {
      HbrRuleProperty property = PropertiesFor("WALL").First();

      Stage02Preview first = Compile(Request(Matched(
        "uid-1", 11, "WALL", Operation(property, "old", "new"))));
      Stage02Preview second = Compile(Request(Matched(
        "uid-1", 99, "WALL", Operation(property, "old", "new"))));

      Assert.Equal(first.CanonicalPayload, second.CanonicalPayload);
      Assert.Equal(first.PreviewHash, second.PreviewHash);
    }

    [Fact]
    public void Preview_preserves_reviewable_old_value_and_hash_snapshot()
    {
      HbrRuleProperty property = PropertiesFor("WALL").First();

      Stage02Preview preview = Compile(Request(Matched(
        "uid-1", 1, "WALL", Operation(property, "旧值", "建议值"))));
      Stage02WriteOperation operation = preview.Elements[0].Operations.Single(
        x => x.PropertyId == property.PropertyId);

      Assert.Equal("旧值", operation.OldValue);
      Assert.Equal("建议值", operation.SuggestedValue);
      Assert.Equal("uid-1", operation.TargetUniqueId);
      Assert.Matches("^[0-9a-f]{64}$", operation.OldValueHash);
      Assert.Equal(property.Revit.BindingScope, operation.BindingScope);
      Assert.Equal(property.Revit.StorageType, operation.StorageType);
      Assert.Equal(property.Revit.ParameterType, operation.ParameterType);
      Assert.Equal(property.Requirement.Level, operation.RequirementLevel);
      Assert.Equal(property.Requirement.ConditionId ?? string.Empty,
        operation.ConditionId);
      Assert.Equal("APPLICABLE", operation.Applicability);
      Assert.Equal("NO_CHANGE", operation.BindingAction);
      Assert.Equal("SET", operation.ValueAction);
      Assert.Equal(operation.ValueAction, operation.Action);
      Assert.True(operation.ParameterExists);
      Assert.True(operation.BindingExists);
      Assert.Equal("GUID", operation.ParameterMatchSource);
      Assert.Equal(property.Revit.BindingScope, operation.ObservedBindingScope);
      Assert.Equal(property.Revit.StorageType, operation.ObservedStorageType);
      Assert.Contains("OST_Walls", operation.BoundCategories);
      Assert.False(operation.IsReadOnly);
      Assert.Equal(property.Revit.StorageType, operation.OldValueKind);
      Assert.Equal("旧值", operation.OldDisplayValue);
      Assert.Equal("canonical-guid", operation.SourceParameterIdentity);
      Assert.Equal("旧值", operation.SourceValue);
      Assert.Equal("EXACT", operation.SuggestionConfidence);
      Assert.Empty(operation.Blockers);
      Assert.NotEqual(
        operation.OldValueHash,
        new Stage02CurrentPropertySnapshot(property.PropertyId, "其他值")
          .OldValueHash);
    }

    [Fact]
    public void Preview_hash_covers_complete_observed_action_and_blocker_state()
    {
      HbrRuleProperty property = PropertiesFor("WALL").First();
      Stage02WriteOperation baselineOperation = Operation(
        property,
        "old",
        "suggested");
      Stage02Preview baseline = Compile(Request(Matched(
        "uid-1", 1, "WALL", baselineOperation)));
      var mutations = new Dictionary<string, Stage02WriteOperation>
      {
        ["bindingExists"] = CopyOperation(
          baselineOperation, bindingExists: false),
        ["parameterExists"] = CopyOperation(
          baselineOperation, parameterExists: false),
        ["parameterMatchSource"] = CopyOperation(
          baselineOperation, parameterMatchSource: "LEGACY_NAME"),
        ["observedBindingScope"] = CopyOperation(
          baselineOperation, observedBindingScope: "TYPE"),
        ["observedStorageType"] = CopyOperation(
          baselineOperation, observedStorageType: "INTEGER"),
        ["boundCategories"] = CopyOperation(
          baselineOperation,
          boundCategories: new[] { "OST_Floors", "OST_Walls" }),
        ["isReadOnly"] = CopyOperation(
          baselineOperation, isReadOnly: true),
        ["oldValueKind"] = CopyOperation(
          baselineOperation, rawValueKind: "INTEGER"),
        ["oldValue"] = CopyOperation(
          baselineOperation, rawValue: "changed-old"),
        ["oldDisplayValue"] = CopyOperation(
          baselineOperation, displayValue: "changed-display"),
        ["sourceParameterIdentity"] = CopyOperation(
          baselineOperation, sourceParameterIdentity: "legacy-name"),
        ["sourceValue"] = CopyOperation(
          baselineOperation, sourceValue: "changed-source"),
        ["suggestedValue"] = CopyOperation(
          baselineOperation, suggestedValue: "changed-suggestion"),
        ["valueSource"] = CopyOperation(
          baselineOperation, valueSource: "STAGE01"),
        ["suggestionConfidence"] = CopyOperation(
          baselineOperation, suggestionConfidence: "INFERRED"),
        ["bindingAction"] = CopyOperation(
          baselineOperation, bindingAction: "MERGE_CATEGORIES"),
        ["valueAction"] = CopyOperation(
          baselineOperation, valueAction: "SKIP"),
        ["blockers"] = CopyOperation(
          baselineOperation,
          blockers: new[] { new Stage02Blocker("TEST", "changed") })
      };

      foreach (KeyValuePair<string, Stage02WriteOperation> mutation in
        mutations)
      {
        Stage02Preview changed = Compile(Request(Matched(
          "uid-1", 1, "WALL", mutation.Value)));
        Assert.True(
          !string.Equals(
            baseline.PreviewHash,
            changed.PreviewHash,
            StringComparison.Ordinal),
          mutation.Key + " 未进入 PreviewHash。");
      }

      Stage02WriteOperation changedApplicability = CopyOperation(
        baseline.Elements[0].Operations.Single(operation => string.Equals(
          operation.PropertyId,
          baselineOperation.PropertyId,
          StringComparison.Ordinal)),
        applicability: "NOT_APPLICABLE");
      Stage02MatchedElement changedElement = baseline.Elements[0]
        .WithOperations(baseline.Elements[0].Operations.Select(operation =>
          string.Equals(
            operation.PropertyId,
            changedApplicability.PropertyId,
            StringComparison.Ordinal)
              ? changedApplicability
              : operation));
      Stage02PreviewRequest changedRequest = Request(changedElement);
      string changedCanonical = Stage02Canonicalizer.BuildPreview(
        changedRequest,
        new[] { changedElement });
      Assert.NotEqual(baseline.CanonicalPayload, changedCanonical);
      Assert.NotEqual(
        baseline.PreviewHash,
        Stage02Hash.Sha256(changedCanonical));

      Assert.Contains(
        "\"targetUniqueId\":\"uid-1\"",
        baseline.CanonicalPayload);
      Assert.Contains("\"bindingExists\":true", baseline.CanonicalPayload);
      Assert.Contains("\"parameterExists\":true", baseline.CanonicalPayload);
      Assert.Contains("\"boundCategories\":[", baseline.CanonicalPayload);
      Assert.Contains(
        "\"suggestionConfidence\":\"EXACT\"",
        baseline.CanonicalPayload);
      Assert.Contains(
        "\"parameterType\":\"" + property.Revit.ParameterType + "\"",
        baseline.CanonicalPayload);
    }

    [Fact]
    public void Preview_rejects_unknown_profile_role_property_or_duplicate_identity()
    {
      HbrRuleProperty property = PropertiesFor("WALL").First();
      Stage02MatchedElement valid = Matched(
        "uid-1", 1, "WALL", Operation(property, "old", "new"));
      var compiler = new Stage02PreviewCompiler(HbrRuleDatabase.Current);

      AssertCode(Stage02Codes.UnknownModelProfile, () =>
        compiler.Compile(RequestWith(valid, activeProfileId: "未知模型")));
      AssertCode(Stage02Codes.UnknownCarrierRole, () =>
        compiler.Compile(Request(Matched(
          "uid-1", 1, "UNKNOWN", Operation(property, "old", "new")))));
      AssertCode(Stage02Codes.UnknownProperty, () =>
        compiler.Compile(Request(Matched(
          "uid-1",
          1,
          "WALL",
          new Stage02WriteOperation(
            "UNKNOWN.PROPERTY",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "未知参数",
            "old",
            "new",
            "TEST",
            "SET")))));
      AssertCode(Stage02Codes.PropertyCarrierMismatch, () =>
        compiler.Compile(Request(Matched(
          "uid-1", 1, "ROOF", Operation(property, "old", "new")))));
      AssertCode(Stage02Codes.DuplicateElementIdentity, () =>
        compiler.Compile(Request(valid, valid)));
      AssertCode(Stage02Codes.DuplicatePropertyOperation, () =>
        compiler.Compile(Request(Matched(
          "uid-1",
          1,
          "WALL",
          Operation(property, "old", "new"),
          Operation(property, "old", "new")))));
    }

    [Fact]
    public void Preview_rejects_zero_or_missing_role_property_closure()
    {
      HbrRuleProperty property = PropertiesFor("WALL").First();
      var compiler = new Stage02PreviewCompiler(HbrRuleDatabase.Current);
      Stage02ElementReference emptyElement = Element(
        "uid-empty", 1, "OST_Walls");
      Stage02ElementReference missingElement = Element(
        "uid-missing", 2, "OST_Walls");
      var engine = new Stage02MatchEngine(HbrRuleDatabase.Current, ProfileId);
      var empty = new Stage02MatchedElement(
        emptyElement,
        engine.Match(emptyElement),
        Array.Empty<Stage02WriteOperation>());
      var missing = new Stage02MatchedElement(
        missingElement,
        engine.Match(missingElement),
        new[] { Operation(property, "old", "new") });

      AssertCode(Stage02Codes.PropertySetMismatch, () =>
        compiler.Compile(Request(empty)));
      AssertCode(Stage02Codes.PropertySetMismatch, () =>
        compiler.Compile(Request(missing)));
    }

    [Fact]
    public void Preview_rejects_rule_identity_not_matching_injected_database()
    {
      HbrRuleProperty property = PropertiesFor("WALL").First();
      Stage02MatchedElement element = Matched(
        "uid-1", 1, "WALL", Operation(property, "old", "new"));
      var compiler = new Stage02PreviewCompiler(HbrRuleDatabase.Current);

      AssertCode(Stage02Codes.RulePackageIdentityMismatch, () =>
        compiler.Compile(RequestWith(element, rulePackageId: "other-id")));
      AssertCode(Stage02Codes.RulePackageIdentityMismatch, () =>
        compiler.Compile(RequestWith(
          element,
          rulePackageVersion: "other-version")));
      AssertCode(Stage02Codes.RulePackageIdentityMismatch, () =>
        compiler.Compile(RequestWith(
          element,
          ruleSha: FlipSha(
            HbrRuleDatabase.Current.Package.RulePackageSha256))));
    }

    [Fact]
    public void Preview_canonical_contains_document_element_and_match_metadata()
    {
      Stage02Preview preview = Compile(Request(Matched(
        "uid-1",
        1,
        "WALL",
        Operation(PropertiesFor("WALL").First(), "old", "new"))));

      Assert.Equal("测试文档", preview.DocumentTitle);
      Assert.Contains("\"documentTitle\":\"测试文档\"", preview.CanonicalPayload);
      Assert.Contains("\"elementKind\":\"Wall\"", preview.CanonicalPayload);
      Assert.Contains(
        "\"matchSource\":\"CATEGORY\"",
        preview.CanonicalPayload);
    }

    [Fact]
    public void Preview_request_derives_identity_and_profile_from_file_context()
    {
      HBRFileContext context = FileContext();
      Stage02MatchedElement element = Matched(
        "uid-1",
        1,
        "WALL",
        Operation(PropertiesFor("WALL").First(), "old", "new"));

      var request = new Stage02PreviewRequest(
        context,
        "context-nonce",
        new[] { element });
      Stage02Preview preview = Compile(request);

      Assert.Equal(context.FileGuid, preview.FileGuid);
      Assert.Equal(context.RevitDocumentFingerprint,
        preview.DocumentFingerprint);
      Assert.Equal(context.RevitDocumentTitle, preview.DocumentTitle);
      Assert.Equal(context.ModelFileType, preview.ActiveProfileId);
      Assert.Equal(context.FileContextHash, preview.FileContextHash);
      Assert.Equal(context.RulePackageSha256, preview.RulePackageSha256);
    }

    [Fact]
    public void Preview_request_accepts_initialized_legacy_official_protocol_flag()
    {
      HBRFileContext context = FileContext(
        officialProtocolCompatible: false);

      var request = new Stage02PreviewRequest(
        context,
        "legacy-official-protocol-flag",
        new[] { Matched("uid-1", 1, "WALL") });

      Assert.True(context.IsReady);
      Assert.Equal(context.FileContextHash, request.FileContextHash);
      Assert.False(context.OfficialProtocolCompatible);
    }

    [Fact]
    public void Preview_request_defensively_copies_project_conditions()
    {
      HBRFileContext context = FileContext(
        projectConditions: new Dictionary<string, bool>
        {
          [ConditionalId] = false
        });
      var request = new Stage02PreviewRequest(
        context,
        "context-nonce",
        new[] { Matched("uid-1", 1, "WALL") });
      ((IDictionary<string, bool>)context.ProjectConditions)[ConditionalId] =
        true;

      PropertyInfo property = typeof(Stage02PreviewRequest).GetProperty(
        "ProjectConditions",
        BindingFlags.Public | BindingFlags.Instance);

      Assert.NotNull(property);
      var frozen = Assert.IsAssignableFrom<IReadOnlyDictionary<string, bool>>(
        property.GetValue(request));
      Assert.False(frozen[ConditionalId]);
      Assert.Throws<NotSupportedException>(() =>
        ((IDictionary<string, bool>)frozen)[ConditionalId] = true);
    }

    [Theory]
    [InlineData(false, "APPLICABLE", "SET")]
    [InlineData(true, "NOT_APPLICABLE", "NO_WRITE")]
    public void Preview_rejects_caller_forged_conditional_state(
      bool conditionValue,
      string applicability,
      string valueAction)
    {
      HbrRuleProperty source = PropertiesFor("WALL").First();
      HbrRuleDatabase database = CloneDatabaseWithRequirement(
        source,
        "CONDITIONAL",
        ConditionalId);
      HbrRuleProperty conditional = database.PropertiesById[
        source.PropertyId];
      Stage02WriteOperation operation = CopyOperation(
        Operation(conditional, "old", "suggested"),
        applicability: applicability,
        valueAction: valueAction);
      HBRFileContext context = FileContext(
        projectConditions: new Dictionary<string, bool>
        {
          [ConditionalId] = conditionValue
        });
      Stage02MatchedElement matched = MatchedForDatabase(
        database,
        "uid-1",
        1,
        "WALL",
        operation);

      AssertCode("CONDITION_STATE_MISMATCH", () => Compile(
        new Stage02PreviewRequest(
          context,
          "conditional-forgery",
          new[] { matched }),
        database));
    }

    [Theory]
    [InlineData("true", "APPLICABLE", "SET", null)]
    [InlineData("false", "NOT_APPLICABLE", "NO_WRITE", null)]
    [InlineData(
      "missing",
      "UNKNOWN",
      "NO_WRITE",
      "CONDITION_STATE_MISSING")]
    public void Preview_compiles_conditional_state_from_project_context(
      string contextState,
      string expectedApplicability,
      string expectedAction,
      string expectedBlocker)
    {
      HbrRuleProperty source = PropertiesFor("WALL").First();
      HbrRuleDatabase database = CloneDatabaseWithRequirement(
        source,
        "CONDITIONAL",
        ConditionalId);
      HbrRuleProperty conditional = database.PropertiesById[
        source.PropertyId];
      Stage02WriteOperation operation = CopyOperation(
        Operation(conditional, "old", "suggested"),
        applicability: expectedApplicability,
        valueAction: expectedAction);
      var conditions = new Dictionary<string, bool>();
      if (string.Equals(contextState, "true", StringComparison.Ordinal))
        conditions[ConditionalId] = true;
      else if (string.Equals(contextState, "false", StringComparison.Ordinal))
        conditions[ConditionalId] = false;
      HBRFileContext context = FileContext(projectConditions: conditions);
      Stage02MatchedElement matched = MatchedForDatabase(
        database,
        "uid-1",
        1,
        "WALL",
        operation);

      Stage02Preview preview = Compile(
        new Stage02PreviewRequest(
          context,
          "conditional-" + contextState,
          new[] { matched }),
        database);
      Stage02WriteOperation compiled = preview.Elements[0].Operations.Single(
        item => string.Equals(
          item.PropertyId,
          conditional.PropertyId,
          StringComparison.Ordinal));

      Assert.Equal(expectedApplicability, compiled.Applicability);
      Assert.Equal(expectedAction, compiled.ValueAction);
      if (expectedBlocker == null)
        Assert.DoesNotContain(compiled.Blockers, blocker =>
          blocker.Code == "CONDITION_STATE_MISSING");
      else
        Assert.Contains(compiled.Blockers, blocker =>
          blocker.Code == expectedBlocker
          && !string.IsNullOrWhiteSpace(blocker.Message));
    }

    [Theory]
    [InlineData("NOT_APPLICABLE", null)]
    [InlineData("UNCLASSIFIED", ConditionalId)]
    [InlineData("CONDITIONAL", null)]
    [InlineData("CONDITIONAL", "unknown.condition")]
    public void Preview_rejects_invalid_or_forged_requirement_state(
      string requirementLevel,
      string conditionId)
    {
      HbrRuleProperty source = PropertiesFor("WALL").First();
      HbrRuleDatabase database = CloneDatabaseWithRequirement(
        source,
        requirementLevel,
        conditionId);
      HbrRuleProperty changed = database.PropertiesById[source.PropertyId];
      Stage02MatchedElement matched = MatchedForDatabase(
        database,
        "uid-1",
        1,
        "WALL",
        Operation(changed, "old", "suggested"));

      AssertCode(
        string.Equals(
          requirementLevel,
          "NOT_APPLICABLE",
          StringComparison.Ordinal)
            ? "CONDITION_STATE_MISMATCH"
            : string.Equals(
                requirementLevel,
                "CONDITIONAL",
                StringComparison.Ordinal)
              && !string.IsNullOrEmpty(conditionId)
                ? "UNKNOWN_CONDITION"
            : "INVALID_REQUIREMENT_CONTRACT",
        () => Compile(
          new Stage02PreviewRequest(
            FileContext(),
            "requirement-contract",
            new[] { matched }),
          database));
    }

    [Theory]
    [InlineData("REQUIRED", "APPLICABLE", "SKIP")]
    [InlineData("OPTIONAL", "APPLICABLE", "SKIP")]
    [InlineData("UNCLASSIFIED", "APPLICABLE", "SKIP")]
    [InlineData("NOT_APPLICABLE", "NOT_APPLICABLE", "NO_WRITE")]
    public void Preview_compiles_valid_nonconditional_requirement_state(
      string requirementLevel,
      string applicability,
      string valueAction)
    {
      HbrRuleProperty source = PropertiesFor("WALL").First();
      HbrRuleDatabase database = CloneDatabaseWithRequirement(
        source,
        requirementLevel,
        null);
      HbrRuleProperty changed = database.PropertiesById[source.PropertyId];
      Stage02WriteOperation operation = CopyOperation(
        Operation(changed, "old", "suggested"),
        applicability: applicability,
        valueAction: valueAction);
      Stage02MatchedElement matched = MatchedForDatabase(
        database,
        "uid-1",
        1,
        "WALL",
        operation);

      Stage02Preview preview = Compile(
        new Stage02PreviewRequest(
          FileContext(),
          "valid-requirement-" + requirementLevel,
          new[] { matched }),
        database);
      Stage02WriteOperation compiled = preview.Elements[0].Operations.Single(
        item => string.Equals(
          item.PropertyId,
          changed.PropertyId,
          StringComparison.Ordinal));

      Assert.Equal(applicability, compiled.Applicability);
      Assert.Equal(valueAction, compiled.ValueAction);
      Assert.Equal(requirementLevel, compiled.RequirementLevel);
      Assert.Empty(compiled.ConditionId);
    }

    [Fact]
    public void Preview_request_rejects_tampered_file_context_hash_or_schema()
    {
      HBRFileContext valid = FileContext();

      AssertCode(Stage02Codes.InvalidFileContext, () =>
        new Stage02PreviewRequest(
          valid.WithHash("tampered-context-hash"),
          "nonce",
          new[] { Matched("uid-1", 1, "WALL") }));
      AssertCode(Stage02Codes.InvalidFileContext, () =>
        new Stage02PreviewRequest(
          FileContext("other-schema"),
          "nonce",
          new[] { Matched("uid-1", 1, "WALL") }));
    }

    [Fact]
    public void Preview_revalidates_role_and_match_source_provenance()
    {
      var ambiguous = new Stage02MatchedElement(
        Element(
          "uid-project",
          1,
          "OST_ProjectInformation",
          "ProjectInformation"),
        "BUILDING",
        Stage02MatchSources.Category,
        OperationsFor("BUILDING"));
      var forgedSource = new Stage02MatchedElement(
        Element("uid-wall", 2, "OST_Walls"),
        "WALL",
        Stage02MatchSources.NameAlias,
        OperationsFor("WALL"));

      AssertCode(Stage02Codes.InvalidMatchEvidence, () =>
        Compile(Request(ambiguous)));
      AssertCode(Stage02Codes.InvalidMatchEvidence, () =>
        Compile(Request(forgedSource)));
    }

    [Theory]
    [InlineData(Stage02MatchSources.RoleHint)]
    [InlineData(Stage02MatchSources.SavedRole)]
    public void Preview_rejects_privileged_source_without_match_engine_proof(
      string forgedSource)
    {
      var forged = new Stage02MatchedElement(
        Element(
          "uid-privileged",
          7,
          "OST_ProjectInformation",
          "ProjectInformation"),
        "BUILDING",
        forgedSource,
        OperationsFor("BUILDING"));

      AssertCode(Stage02Codes.InvalidMatchEvidence, () =>
        Compile(Request(forged)));
    }

    [Fact]
    public void Preview_models_defensively_copy_all_collections()
    {
      HbrRuleProperty property = PropertiesFor("WALL").First();
      var operations = new List<Stage02WriteOperation>(
        OperationsFor("WALL"));
      Stage02ElementReference reference = Element(
        "uid-1", 1, "OST_Walls");
      Stage02MatchResult match = new Stage02MatchEngine(
        HbrRuleDatabase.Current,
        ProfileId).Match(reference);
      var elements = new List<Stage02MatchedElement>
      {
        new Stage02MatchedElement(
          reference,
          match,
          operations)
      };
      var compiler = new Stage02PreviewCompiler(HbrRuleDatabase.Current);

      Stage02Preview preview = compiler.Compile(RequestFrom(elements));
      operations.Clear();
      elements.Clear();

      Assert.Single(preview.Elements);
      Assert.Equal(
        PropertiesFor("WALL").Count(),
        preview.Elements[0].Operations.Count);
      Assert.Throws<NotSupportedException>(() =>
        ((IList<Stage02MatchedElement>)preview.Elements).Clear());
      Assert.Throws<NotSupportedException>(() =>
        ((IList<Stage02WriteOperation>)preview.Elements[0].Operations).Clear());
    }

    internal static Stage02Preview Compile(
      Stage02PreviewRequest request,
      HbrRuleDatabase database = null)
    {
      return new Stage02PreviewCompiler(database ?? HbrRuleDatabase.Current)
        .Compile(request);
    }

    internal static Stage02PreviewRequest Request(
      params Stage02MatchedElement[] elements)
    {
      return RequestFrom(elements);
    }

    internal static Stage02PreviewRequest RequestFrom(
      IEnumerable<Stage02MatchedElement> elements)
    {
      HbrRulePackage package = HbrRuleDatabase.Current.Package;
      return new Stage02PreviewRequest(
        "file-guid",
        "doc-fingerprint",
        "测试文档",
        "context-hash",
        ProfileId,
        package.PackageId,
        package.PackageVersion,
        package.RulePackageSha256,
        "fixed-nonce",
        elements);
    }

    internal static Stage02PreviewRequest RequestWith(
      Stage02MatchedElement element,
      string activeProfileId = ProfileId,
      string rulePackageId = null,
      string rulePackageVersion = null,
      string ruleSha = null,
      string nonce = "fixed-nonce")
    {
      HbrRulePackage package = HbrRuleDatabase.Current.Package;
      return new Stage02PreviewRequest(
        "file-guid",
        "doc-fingerprint",
        "测试文档",
        "context-hash",
        activeProfileId,
        rulePackageId ?? package.PackageId,
        rulePackageVersion ?? package.PackageVersion,
        ruleSha ?? package.RulePackageSha256,
        nonce,
        new[] { element });
    }

    internal static Stage02MatchedElement Matched(
      string uniqueId,
      int elementId,
      string roleId,
      params Stage02WriteOperation[] operations)
    {
      return MatchedForDatabase(
        HbrRuleDatabase.Current,
        uniqueId,
        elementId,
        roleId,
        operations);
    }

    internal static Stage02MatchedElement MatchedForDatabase(
      HbrRuleDatabase database,
      string uniqueId,
      int elementId,
      string roleId,
      params Stage02WriteOperation[] operations)
    {
      string category = roleId == "ROOF" ? "OST_Roofs" : "OST_Walls";
      string elementKind = roleId == "ROOF" ? "Roof" : "Wall";
      if (roleId == "PROJECT" || roleId == "SITE" || roleId == "BUILDING")
      {
        category = "OST_ProjectInformation";
        elementKind = "ProjectInformation";
      }
      var completeOperations = new List<Stage02WriteOperation>(operations);
      var suppliedIds = new HashSet<string>(
        completeOperations.Select(x => x.PropertyId),
        StringComparer.Ordinal);
      completeOperations.AddRange(OperationsFor(roleId).Where(x =>
        !suppliedIds.Contains(x.PropertyId)));
      Stage02ElementReference element = Element(
        uniqueId,
        elementId,
        category,
        elementKind);
      if (!database.CarrierRolesById.ContainsKey(roleId))
      {
        return new Stage02MatchedElement(
          element,
          roleId,
          Stage02MatchSources.Category,
          completeOperations);
      }
      var engine = new Stage02MatchEngine(database, ProfileId);
      Stage02MatchResult match = roleId == "PROJECT"
        || roleId == "SITE"
        || roleId == "BUILDING"
          ? engine.Match(element, roleHint: roleId)
          : engine.Match(element);
      return new Stage02MatchedElement(element, match, completeOperations);
    }

    internal static Stage02ElementReference Element(
      string uniqueId,
      int elementId,
      string category,
      string elementKind = "Wall")
    {
      return new Stage02ElementReference(
        "doc-fingerprint",
        "测试文档",
        elementId,
        uniqueId,
        category,
        elementKind,
        "族-" + uniqueId,
        "类型-" + uniqueId,
        "元素-" + uniqueId);
    }

    internal static Stage02WriteOperation Operation(
      HbrRuleProperty property,
      string oldValue,
      string suggestedValue)
    {
      return new Stage02WriteOperation(
        property.PropertyId,
        property.Revit.ParameterGuid,
        property.Revit.ParameterName,
        new Stage02ObservedParameterState(
          string.Empty,
          true,
          true,
          "GUID",
          property.Revit.BindingScope,
          property.Revit.StorageType,
          new[] { CategoryForRole(property.CarrierRoleIds[0]) },
          false,
          property.Revit.StorageType,
          oldValue,
          oldValue,
          "canonical-guid",
          oldValue),
        suggestedValue,
        "RULE_PACKAGE",
        "EXACT",
        "NO_CHANGE",
        "SET");
    }

    internal static Stage02WriteOperation CopyOperation(
      Stage02WriteOperation source,
      string targetUniqueId = null,
      bool? bindingExists = null,
      bool? parameterExists = null,
      string parameterMatchSource = null,
      string observedBindingScope = null,
      string observedStorageType = null,
      IEnumerable<string> boundCategories = null,
      bool? isReadOnly = null,
      string rawValueKind = null,
      string rawValue = null,
      string displayValue = null,
      string sourceParameterIdentity = null,
      string sourceValue = null,
      string suggestedValue = null,
      string valueSource = null,
      string suggestionConfidence = null,
      string bindingAction = null,
      string valueAction = null,
      string applicability = null,
      IEnumerable<Stage02Blocker> blockers = null,
      string propertyId = null,
      Guid? parameterGuid = null,
      string parameterName = null,
      string bindingScope = null,
      string storageType = null,
      string parameterType = null,
      string requirementLevel = null,
      string conditionId = null)
    {
      var observed = new Stage02ObservedParameterState(
        targetUniqueId ?? source.TargetUniqueId,
        bindingExists ?? source.BindingExists,
        parameterExists ?? source.ParameterExists,
        parameterMatchSource ?? source.ParameterMatchSource,
        observedBindingScope ?? source.ObservedBindingScope,
        observedStorageType ?? source.ObservedStorageType,
        boundCategories ?? source.BoundCategories,
        isReadOnly ?? source.IsReadOnly,
        rawValueKind ?? source.OldValueKind,
        rawValue ?? source.OldValue,
        displayValue ?? source.OldDisplayValue,
        sourceParameterIdentity ?? source.SourceParameterIdentity,
        sourceValue ?? source.SourceValue);
      var operation = new Stage02WriteOperation(
        propertyId ?? source.PropertyId,
        parameterGuid ?? source.ParameterGuid,
        parameterName ?? source.ParameterName,
        observed,
        suggestedValue ?? source.SuggestedValue,
        valueSource ?? source.ValueSource,
        suggestionConfidence ?? source.SuggestionConfidence,
        bindingAction ?? source.BindingAction,
        valueAction ?? source.ValueAction,
        applicability ?? source.Applicability,
        blockers ?? source.Blockers);
      return operation.WithRuleMetadata(
        observed,
        bindingScope ?? source.BindingScope,
        storageType ?? source.StorageType,
        parameterType ?? source.ParameterType,
        requirementLevel ?? source.RequirementLevel,
        conditionId ?? source.ConditionId);
    }

    private static string CategoryForRole(string roleId)
    {
      switch (roleId)
      {
        case "WALL": return "OST_Walls";
        case "ROOF": return "OST_Roofs";
        case "PROJECT":
        case "SITE":
        case "BUILDING": return "OST_ProjectInformation";
        default: return "OST_Unknown";
      }
    }

    internal static IEnumerable<HbrRuleProperty> PropertiesFor(string roleId)
    {
      return HbrRuleDatabase.Current.Package.Properties.Where(x =>
        x.CarrierRoleIds.Contains(roleId));
    }

    internal static IEnumerable<Stage02WriteOperation> OperationsFor(
      string roleId)
    {
      return PropertiesFor(roleId).Select(property => Operation(
        property,
        "old-" + property.PropertyId,
        "suggested-" + property.PropertyId));
    }

    internal static HBRFileContext FileContext(
      string schemaVersion = HBRContextVersions.FileContextSchema,
      IDictionary<string, bool> projectConditions = null,
      bool officialProtocolCompatible = true)
    {
      HbrRulePackage package = HbrRuleDatabase.Current.Package;
      var provisional = new HBRFileContext(
        schemaVersion,
        "0.9.0",
        "file-guid",
        "doc-fingerprint",
        "测试文档",
        "P-001",
        "测试项目",
        "S-001",
        "测试子项",
        ProfileId,
        "测试范围",
        null,
        new Dictionary<string, PlanningTargetValue>(),
        projectConditions ?? new Dictionary<string, bool>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        true,
        officialProtocolCompatible,
        package.PackageVersion,
        package.PackageId,
        package.PackageVersion,
        package.RulePackageSha256,
        "source-hash",
        string.Empty);
      return provisional.WithHash(
        HBRFileContextCanonicalizer.ComputeHash(provisional));
    }

    private static string FlipSha(string sha)
    {
      return (sha[0] == 'a' ? "b" : "a") + sha.Substring(1);
    }

    private static HbrRuleDatabase CloneDatabaseWithRuleSha(string sha)
    {
      MethodInfo cloneMethod = typeof(object).GetMethod(
        "MemberwiseClone",
        BindingFlags.Instance | BindingFlags.NonPublic);
      var package = (HbrRulePackage)cloneMethod.Invoke(
        HbrRuleDatabase.Current.Package,
        null);
      typeof(HbrRulePackage).GetField(
        "<RulePackageSha256>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic).SetValue(package, sha);

      var database = (HbrRuleDatabase)cloneMethod.Invoke(
        HbrRuleDatabase.Current,
        null);
      typeof(HbrRuleDatabase).GetField(
        "<Package>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic).SetValue(
          database,
          package);
      return database;
    }

    private static HbrRuleDatabase CloneDatabaseWithRequirement(
      HbrRuleProperty source,
      string requirementLevel,
      string conditionId)
    {
      MethodInfo cloneMethod = typeof(object).GetMethod(
        "MemberwiseClone",
        BindingFlags.Instance | BindingFlags.NonPublic);
      var requirement = (HbrRequirement)cloneMethod.Invoke(
        source.Requirement,
        null);
      typeof(HbrRequirement).GetField(
        "<Level>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic).SetValue(
          requirement,
          requirementLevel);
      typeof(HbrRequirement).GetField(
        "<ConditionId>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic).SetValue(
          requirement,
          conditionId);

      var property = (HbrRuleProperty)cloneMethod.Invoke(source, null);
      typeof(HbrRuleProperty).GetField(
        "<Requirement>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic).SetValue(
          property,
          requirement);

      var properties = HbrRuleDatabase.Current.PropertiesById.ToDictionary(
        pair => pair.Key,
        pair => pair.Value,
        StringComparer.Ordinal);
      properties[source.PropertyId] = property;
      var database = (HbrRuleDatabase)cloneMethod.Invoke(
        HbrRuleDatabase.Current,
        null);
      typeof(HbrRuleDatabase).GetField(
        "<PropertiesById>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic).SetValue(
          database,
          new ReadOnlyDictionary<string, HbrRuleProperty>(properties));
      return database;
    }

    private static void AssertCode(string code, Action action)
    {
      Stage02ContractException exception = Assert.Throws<Stage02ContractException>(
        action);
      Assert.Equal(code, exception.Code);
      Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }
  }
}
