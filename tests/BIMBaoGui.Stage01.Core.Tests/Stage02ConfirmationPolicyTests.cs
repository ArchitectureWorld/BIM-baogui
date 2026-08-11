using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.Stage02;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage02ConfirmationPolicyTests
  {
    [Fact]
    public void Confirmation_snapshot_public_constructor_is_context_derived()
    {
      ConstructorInfo constructor = Assert.Single(
        typeof(Stage02ConfirmationSnapshot).GetConstructors(
          BindingFlags.Public | BindingFlags.Instance));
      ParameterInfo[] parameters = constructor.GetParameters();

      Assert.NotEmpty(parameters);
      Assert.Equal(typeof(HBRFileContext), parameters[0].ParameterType);
      Assert.DoesNotContain(parameters, parameter =>
        string.Equals(parameter.Name, "fileGuid", StringComparison.Ordinal)
        || string.Equals(
          parameter.Name,
          "documentFingerprint",
          StringComparison.Ordinal)
        || string.Equals(
          parameter.Name,
          "fileContextHash",
          StringComparison.Ordinal)
        || string.Equals(
          parameter.Name,
          "activeProfileId",
          StringComparison.Ordinal)
        || string.Equals(
          parameter.Name,
          "rulePackageId",
          StringComparison.Ordinal)
        || string.Equals(
          parameter.Name,
          "rulePackageVersion",
          StringComparison.Ordinal)
        || string.Equals(
          parameter.Name,
          "rulePackageSha256",
          StringComparison.Ordinal));
    }

    [Fact]
    public void Confirmation_snapshot_validates_context_and_freezes_conditions()
    {
      Stage02Preview preview = Preview();
      IReadOnlyList<Stage02CurrentElementSnapshot> elements =
        Snapshot(preview).Elements;
      HBRFileContext context = Stage02PreviewCompilerTests.FileContext(
        projectConditions: new Dictionary<string, bool>
        {
          ["building.roof"] = false
        });

      Stage02ContractException exception = Assert.Throws<
        Stage02ContractException>(() => new Stage02ConfirmationSnapshot(
          context.WithHash("tampered-context-hash"),
          preview.PreviewHash,
          preview.Nonce,
          elements));
      var snapshot = new Stage02ConfirmationSnapshot(
        context,
        preview.PreviewHash,
        preview.Nonce,
        elements);
      ((IDictionary<string, bool>)context.ProjectConditions)[
        "building.roof"] = true;

      Assert.Equal(Stage02Codes.InvalidFileContext, exception.Code);
      Assert.Equal(context.FileGuid, snapshot.FileGuid);
      Assert.Equal(
        context.RevitDocumentFingerprint,
        snapshot.DocumentFingerprint);
      Assert.Equal(context.FileContextHash, snapshot.FileContextHash);
      Assert.Equal(context.ModelFileType, snapshot.ActiveProfileId);
      Assert.False(snapshot.ProjectConditions["building.roof"]);
      Assert.Throws<NotSupportedException>(() =>
        ((IDictionary<string, bool>)snapshot.ProjectConditions)[
          "building.roof"] = true);
    }

    [Fact]
    public void Confirmation_rejects_project_conditions_change_with_reused_hash()
    {
      HBRFileContext context = Stage02PreviewCompilerTests.FileContext(
        projectConditions: new Dictionary<string, bool>
        {
          ["building.roof"] = false
        });
      HbrRuleProperty property =
        Stage02PreviewCompilerTests.PropertiesFor("WALL").First();
      Stage02MatchedElement element = Stage02PreviewCompilerTests.Matched(
        "uid-1",
        42,
        "WALL",
        Stage02PreviewCompilerTests.Operation(
          property,
          "old",
          "suggested"));
      Stage02Preview preview = Stage02PreviewCompilerTests.Compile(
        new Stage02PreviewRequest(
          context,
          "conditions-changed",
          new[] { element }));
      var current = new Stage02ConfirmationSnapshot(
        preview.PreviewHash,
        preview.Nonce,
        preview.FileGuid,
        preview.DocumentFingerprint,
        preview.FileContextHash,
        preview.ActiveProfileId,
        preview.RulePackageId,
        preview.RulePackageVersion,
        preview.RulePackageSha256,
        new Dictionary<string, bool>
        {
          ["building.roof"] = true
        },
        Snapshot(preview).Elements);

      Stage02ConfirmationResult result = Policy()
        .ValidateAndConsumeForExecution(preview, current);

      AssertRejected(result, Stage02Codes.FileContextChanged);
    }

    [Fact]
    public void Confirmation_rejects_document_or_old_value_change()
    {
      Stage02Preview preview = Preview();

      Stage02ConfirmationResult documentChanged =
        Policy().ValidateAndConsumeForExecution(
          preview,
          Snapshot(preview, documentFingerprint: "other-document"));
      Stage02ConfirmationResult oldValueChanged =
        Policy().ValidateAndConsumeForExecution(
          preview,
          Snapshot(preview, oldValue: "changed-old"));

      AssertRejected(documentChanged, Stage02Codes.DocumentFingerprintChanged);
      AssertRejected(oldValueChanged, Stage02Codes.OldValueChanged);
    }

    [Fact]
    public void Confirmation_rejects_old_value_hash_change_when_text_is_same()
    {
      Stage02Preview preview = Preview();

      Stage02ConfirmationResult result =
        Policy().ValidateAndConsumeForExecution(
          preview,
          Snapshot(preview, oldValueHash: new string('f', 64)));

      AssertRejected(result, Stage02Codes.OldValueChanged);
    }

    [Fact]
    public void Confirmation_rejects_preview_with_operation_blockers()
    {
      HbrRuleProperty property =
        Stage02PreviewCompilerTests.PropertiesFor("WALL").First();
      var operation = new Stage02WriteOperation(
        property.PropertyId,
        property.Revit.ParameterGuid,
        property.Revit.ParameterName,
        "old",
        "suggested",
        "RULE_PACKAGE",
        "NO_CHANGE",
        "SET",
        "APPLICABLE",
        new[]
        {
          new Stage02Blocker("VALUE_BLOCKED", "建议值需要人工处理。")
        });
      Stage02Preview preview = Stage02PreviewCompilerTests.Compile(
        Stage02PreviewCompilerTests.Request(
          Stage02PreviewCompilerTests.Matched(
            "uid-1", 42, "WALL", operation)));

      Stage02ConfirmationResult result =
        Policy().ValidateAndConsumeForExecution(preview, Snapshot(preview));

      AssertRejected(result, Stage02Codes.PreviewHasBlockers);
    }

    [Fact]
    public void Confirmation_consumes_same_preview_hash_and_nonce_only_once()
    {
      Stage02Preview preview = Preview();
      Stage02ConfirmationSnapshot snapshot = Snapshot(preview);
      Stage02ConfirmationPolicy policy = Policy();

      Stage02ConfirmationResult first =
        policy.ValidateAndConsumeForExecution(preview, snapshot);
      Stage02ConfirmationResult second =
        policy.ValidateAndConsumeForExecution(preview, snapshot);

      Assert.True(first.Accepted);
      Assert.Empty(first.Blockers);
      Assert.Equal(
        Stage02HandoffStates.ConsumedForExecution,
        first.HandoffState);
      Assert.Equal(
        preview.PreviewHash + "|" + preview.Nonce,
        first.ConsumptionKey);
      Assert.True(first.RequiresNewPreviewAfterExecutionFailure);
      AssertRejected(second, Stage02Codes.PreviewAlreadyConsumed);
    }

    [Fact]
    public void Confirmation_does_not_burn_nonce_when_validation_fails()
    {
      Stage02Preview preview = Preview();
      Stage02ConfirmationPolicy policy = Policy();

      Stage02ConfirmationResult rejected =
        policy.ValidateAndConsumeForExecution(
        preview,
        Snapshot(preview, fileContextHash: "changed-context"));
      Stage02ConfirmationResult accepted =
        policy.ValidateAndConsumeForExecution(
        preview,
        Snapshot(preview));

      AssertRejected(rejected, Stage02Codes.FileContextChanged);
      Assert.True(accepted.Accepted);
    }

    [Fact]
    public void Confirmation_rejects_any_rule_context_unique_id_or_role_change()
    {
      Stage02Preview preview = Preview();
      var checks = new[]
      {
        new { Snapshot = Snapshot(preview, fileGuid: "other-file"), Code = Stage02Codes.FileGuidChanged },
        new { Snapshot = Snapshot(preview, activeProfileId: "单体建筑—地下"), Code = Stage02Codes.ActiveProfileChanged },
        new { Snapshot = Snapshot(preview, fileContextHash: "other-context"), Code = Stage02Codes.FileContextChanged },
        new { Snapshot = Snapshot(preview, rulePackageId: "other-package"), Code = Stage02Codes.RulePackageIdentityChanged },
        new { Snapshot = Snapshot(preview, rulePackageVersion: "other-version"), Code = Stage02Codes.RulePackageIdentityChanged },
        new { Snapshot = Snapshot(preview, rulePackageSha: new string('a', 64)), Code = Stage02Codes.RulePackageIdentityChanged },
        new { Snapshot = Snapshot(preview, uniqueId: "other-uid"), Code = Stage02Codes.ElementSetChanged },
        new { Snapshot = Snapshot(preview, roleId: "ROOF"), Code = Stage02Codes.RoleSnapshotChanged },
        new { Snapshot = Snapshot(preview, elementName: "changed-name"), Code = Stage02Codes.ElementSnapshotChanged },
        new { Snapshot = Snapshot(preview, targetUniqueId: "other-target"), Code = Stage02Codes.OldValueChanged },
        new { Snapshot = Snapshot(preview, previewHash: new string('b', 64)), Code = Stage02Codes.PreviewHashChanged },
        new { Snapshot = Snapshot(preview, nonce: "other-nonce"), Code = Stage02Codes.NonceChanged }
      };

      foreach (var check in checks)
      {
        Stage02ConfirmationResult result =
          Policy().ValidateAndConsumeForExecution(preview, check.Snapshot);
        AssertRejected(result, check.Code);
      }
    }

    [Fact]
    public void Confirmation_uses_document_fingerprint_and_unique_id_not_element_id()
    {
      Stage02Preview preview = Preview();

      Stage02ConfirmationResult result =
        Policy().ValidateAndConsumeForExecution(
          preview,
          Snapshot(preview, elementId: 987654));

      Assert.True(result.Accepted);
      Assert.Empty(result.Blockers);
    }

    [Fact]
    public async Task Confirmation_nonce_consumption_is_thread_safe()
    {
      Stage02Preview preview = Preview();
      Stage02ConfirmationSnapshot snapshot = Snapshot(preview);
      Stage02ConfirmationPolicy policy = Policy();
      Task<Stage02ConfirmationResult>[] tasks = Enumerable.Range(0, 16)
        .Select(_ => Task.Run(() =>
          policy.ValidateAndConsumeForExecution(preview, snapshot)))
        .ToArray();

      Stage02ConfirmationResult[] results = await Task.WhenAll(tasks);

      Assert.Single(results, x => x.Accepted);
      Assert.Equal(15, results.Count(x => x.Blockers.Any(
        blocker => blocker.Code == Stage02Codes.PreviewAlreadyConsumed)));
    }

    [Fact]
    public void Confirmation_snapshot_and_result_are_defensively_read_only()
    {
      Stage02Preview preview = Preview();
      var elements = new List<Stage02CurrentElementSnapshot>(
        Snapshot(preview).Elements);
      var snapshot = new Stage02ConfirmationSnapshot(
        preview.PreviewHash,
        preview.Nonce,
        preview.FileGuid,
        preview.DocumentFingerprint,
        preview.FileContextHash,
        preview.ActiveProfileId,
        preview.RulePackageId,
        preview.RulePackageVersion,
        preview.RulePackageSha256,
        elements);
      elements.Clear();

      Assert.Single(snapshot.Elements);
      Assert.Throws<NotSupportedException>(() =>
        ((IList<Stage02CurrentElementSnapshot>)snapshot.Elements).Clear());
      Stage02ConfirmationResult rejected =
        Policy().ValidateAndConsumeForExecution(
          preview,
          Snapshot(preview, nonce: "wrong"));
      Assert.Throws<NotSupportedException>(() =>
        ((IList<Stage02Blocker>)rejected.Blockers).Clear());
    }

    [Fact]
    public void Confirmation_store_is_atomic_across_policy_instances()
    {
      Stage02Preview preview = Preview();
      Stage02ConfirmationSnapshot snapshot = Snapshot(preview);
      var store = new Stage02ConfirmationConsumptionStore();
      var firstPolicy = new Stage02ConfirmationPolicy(store);
      var secondPolicy = new Stage02ConfirmationPolicy(store);

      Stage02ConfirmationResult first =
        firstPolicy.ValidateAndConsumeForExecution(preview, snapshot);
      Stage02ConfirmationResult second =
        secondPolicy.ValidateAndConsumeForExecution(preview, snapshot);

      Assert.True(first.Accepted);
      AssertRejected(second, Stage02Codes.PreviewAlreadyConsumed);
    }

    [Fact]
    public void Confirmation_default_policies_share_one_process_store()
    {
      Stage02Preview preview = Preview("default-shared-serial");
      Stage02ConfirmationSnapshot snapshot = Snapshot(preview);

      Stage02ConfirmationResult first = new Stage02ConfirmationPolicy()
        .ValidateAndConsumeForExecution(preview, snapshot);
      Stage02ConfirmationResult second = new Stage02ConfirmationPolicy()
        .ValidateAndConsumeForExecution(preview, snapshot);

      Assert.True(first.Accepted);
      AssertRejected(second, Stage02Codes.PreviewAlreadyConsumed);
    }

    [Fact]
    public async Task Confirmation_default_store_is_atomic_across_policy_instances()
    {
      Stage02Preview preview = Preview("default-shared-concurrent");
      Stage02ConfirmationSnapshot snapshot = Snapshot(preview);
      var first = new Stage02ConfirmationPolicy();
      var second = new Stage02ConfirmationPolicy();
      Task<Stage02ConfirmationResult>[] tasks = Enumerable.Range(0, 16)
        .Select(index => Task.Run(() => (index % 2 == 0 ? first : second)
          .ValidateAndConsumeForExecution(preview, snapshot)))
        .ToArray();

      Stage02ConfirmationResult[] results = await Task.WhenAll(tasks);

      Assert.Single(results, result => result.Accepted);
      Assert.Equal(15, results.Count(result => result.Blockers.Any(
        blocker => blocker.Code == Stage02Codes.PreviewAlreadyConsumed)));
    }

    [Fact]
    public void Confirmation_test_store_injection_is_not_public_api()
    {
      Assert.False(typeof(Stage02ConfirmationConsumptionStore).IsPublic);
      Assert.DoesNotContain(
        typeof(Stage02ConfirmationPolicy).GetConstructors(
          BindingFlags.Public | BindingFlags.Instance),
        constructor => constructor.GetParameters().Length != 0);
      Assert.DoesNotContain(
        typeof(Stage02ConfirmationPolicy).GetMethods(
          BindingFlags.Public | BindingFlags.Instance),
        method => method.Name.IndexOf(
          "Release",
          StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void Confirmation_rejects_tampered_canonical_payload()
    {
      Stage02Preview original = Preview();
      var tampered = new Stage02Preview(
        Stage02PreviewCompilerTests.Request(original.Elements.ToArray()),
        original.Elements,
        original.CanonicalPayload + " ",
        original.PreviewHash);

      Stage02ConfirmationResult result =
        Policy().ValidateAndConsumeForExecution(tampered, Snapshot(tampered));

      AssertRejected(result, Stage02Codes.PreviewHashChanged);
    }

    [Fact]
    public void Confirmation_rejects_object_graph_not_matching_canonical_payload()
    {
      Stage02Preview original = Preview();
      Stage02MatchedElement expected = original.Elements[0];
      Stage02WriteOperation changed =
        Stage02PreviewCompilerTests.CopyOperation(
          expected.Operations[0],
          suggestedValue: "tampered-suggestion");
      var operations = expected.Operations
        .Select((operation, index) => index == 0 ? changed : operation)
        .ToArray();
      Stage02MatchedElement changedElement = expected.WithOperations(operations);
      var tampered = new Stage02Preview(
        Stage02PreviewCompilerTests.Request(changedElement),
        new[] { changedElement },
        original.CanonicalPayload,
        original.PreviewHash);

      Stage02ConfirmationResult result =
        Policy().ValidateAndConsumeForExecution(tampered, Snapshot(tampered));

      AssertRejected(result, Stage02Codes.PreviewHashChanged);
    }

    [Fact]
    public void Confirmation_rejects_any_complete_operation_snapshot_change()
    {
      Stage02Preview preview = Preview();
      Stage02WriteOperation operation = preview.Elements[0].Operations[0];
      var mutations = new[]
      {
        Stage02PreviewCompilerTests.CopyOperation(
          operation, targetUniqueId: "other-target"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, parameterGuid: Guid.Parse(
            "11111111-1111-1111-1111-111111111111")),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, parameterName: "changed-name"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, bindingExists: !operation.BindingExists),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, parameterExists: !operation.ParameterExists),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, parameterMatchSource: "LEGACY_NAME"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, observedBindingScope: "TYPE"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, observedStorageType: "INTEGER"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, boundCategories: new[] { "OST_Floors" }),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, isReadOnly: !operation.IsReadOnly),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, rawValueKind: "INTEGER"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, displayValue: "changed-display"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, sourceParameterIdentity: "legacy-name"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, sourceValue: "changed-source"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, suggestedValue: "changed-suggestion"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, valueSource: "STAGE01"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, suggestionConfidence: "INFERRED"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, bindingAction: "MERGE_CATEGORIES"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, valueAction: "SKIP"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, applicability: "NOT_APPLICABLE"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation,
          blockers: new[] { new Stage02Blocker("TEST", "changed") }),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, bindingScope: operation.BindingScope + "_CHANGED"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, storageType: operation.StorageType + "_CHANGED"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, parameterType: operation.ParameterType + "_CHANGED"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation,
          requirementLevel: operation.RequirementLevel + "_CHANGED"),
        Stage02PreviewCompilerTests.CopyOperation(
          operation, conditionId: "changed-condition")
      };

      foreach (Stage02WriteOperation mutation in mutations)
      {
        Stage02ConfirmationResult result =
          Policy().ValidateAndConsumeForExecution(
            preview,
            Snapshot(preview, currentOperation: mutation));
        AssertRejected(result, Stage02Codes.OldValueChanged);
      }
    }

    [Fact]
    public void Confirmation_rejects_match_source_change()
    {
      Stage02Preview preview = Preview();

      Stage02ConfirmationResult result =
        Policy().ValidateAndConsumeForExecution(
          preview,
          Snapshot(preview, matchSource: Stage02MatchSources.SavedRole));

      AssertRejected(result, Stage02Codes.RoleSnapshotChanged);
    }

    [Fact]
    public void Confirmation_rejects_malformed_current_operation_without_throwing()
    {
      Stage02Preview preview = Preview();
      Stage02WriteOperation malformed =
        Stage02PreviewCompilerTests.CopyOperation(
          preview.Elements[0].Operations[0],
          blockers: new Stage02Blocker[] { null });

      Stage02ConfirmationResult result =
        Policy().ValidateAndConsumeForExecution(
          preview,
          Snapshot(preview, currentOperation: malformed));

      AssertRejected(result, Stage02Codes.OldValueChanged);
    }

    private static Stage02Preview Preview(string nonce = "fixed-nonce")
    {
      HbrRuleProperty property =
        Stage02PreviewCompilerTests.PropertiesFor("WALL").First();
      Stage02MatchedElement element = Stage02PreviewCompilerTests.Matched(
        "uid-1",
        42,
        "WALL",
        Stage02PreviewCompilerTests.Operation(
          property,
          "old",
          "suggested"));
      return Stage02PreviewCompilerTests.Compile(
        string.Equals(nonce, "fixed-nonce", StringComparison.Ordinal)
          ? Stage02PreviewCompilerTests.Request(element)
          : Stage02PreviewCompilerTests.RequestWith(element, nonce: nonce));
    }

    private static Stage02ConfirmationSnapshot Snapshot(
      Stage02Preview preview,
      string previewHash = null,
      string nonce = null,
      string fileGuid = null,
      string documentFingerprint = null,
      string fileContextHash = null,
      string activeProfileId = null,
      string rulePackageId = null,
      string rulePackageVersion = null,
      string rulePackageSha = null,
      string uniqueId = null,
      int? elementId = null,
      string roleId = null,
      string matchSource = null,
      string elementName = null,
      string oldValue = null,
      string oldValueHash = null,
      string targetUniqueId = null,
      Stage02WriteOperation currentOperation = null)
    {
      Stage02MatchedElement expected = preview.Elements[0];
      string currentDocument = documentFingerprint
        ?? preview.DocumentFingerprint;
      var reference = new Stage02ElementReference(
        currentDocument,
        expected.Element.DocumentTitle,
        elementId ?? expected.Element.ElementId,
        uniqueId ?? expected.Element.UniqueId,
        expected.Element.Category,
        expected.Element.ElementKind,
        expected.Element.FamilyName,
        expected.Element.TypeName,
        elementName ?? expected.Element.ElementName);
      var element = new Stage02CurrentElementSnapshot(
        reference,
        roleId ?? expected.RoleId,
        matchSource ?? expected.MatchSource,
        expected.Operations.Select((operation, index) =>
        {
          Stage02WriteOperation current = index == 0
            ? currentOperation ?? operation
            : operation;
          if (index == 0 && (targetUniqueId != null || oldValue != null))
          {
            current = Stage02PreviewCompilerTests.CopyOperation(
              current,
              targetUniqueId: targetUniqueId,
              rawValue: oldValue);
          }
          return new Stage02CurrentPropertySnapshot(
            current,
            index == 0 ? oldValueHash : null);
        }));
      return new Stage02ConfirmationSnapshot(
        previewHash ?? preview.PreviewHash,
        nonce ?? preview.Nonce,
        fileGuid ?? preview.FileGuid,
        currentDocument,
        fileContextHash ?? preview.FileContextHash,
        activeProfileId ?? preview.ActiveProfileId,
        rulePackageId ?? preview.RulePackageId,
        rulePackageVersion ?? preview.RulePackageVersion,
        rulePackageSha ?? preview.RulePackageSha256,
        new[] { element });
    }

    private static void AssertRejected(
      Stage02ConfirmationResult result,
      string expectedCode)
    {
      Assert.False(result.Accepted);
      Assert.Equal(Stage02HandoffStates.Rejected, result.HandoffState);
      Assert.False(result.RequiresNewPreviewAfterExecutionFailure);
      Assert.Contains(result.Blockers, x =>
        x.Code == expectedCode && !string.IsNullOrWhiteSpace(x.Message));
    }


    private static Stage02ConfirmationPolicy Policy()
    {
      return new Stage02ConfirmationPolicy(
        new Stage02ConfirmationConsumptionStore());
    }
  }
}
