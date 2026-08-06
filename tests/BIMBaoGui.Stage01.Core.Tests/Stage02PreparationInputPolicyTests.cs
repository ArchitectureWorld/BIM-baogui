using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Revit.Parameters;
using BIMBaoGui.Stage01.Stage02;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage02PreparationInputPolicyTests
  {
    [Fact]
    public void Evaluate_BlocksProjectInformationConflicts()
    {
      Stage02PreparationInputDecision idsConflict = Evaluate(
        new[] { 42 },
        explicitPick: false,
        projectInformation: true);
      Stage02PreparationInputDecision pickConflict = Evaluate(
        Array.Empty<int>(),
        explicitPick: true,
        projectInformation: true);

      Assert.False(idsConflict.Success);
      Assert.False(pickConflict.Success);
      Assert.Contains(idsConflict.Blockers, value => value.Contains("冲突"));
      Assert.Contains(pickConflict.Blockers, value => value.Contains("冲突"));
    }

    [Fact]
    public void Evaluate_BlocksExplicitIdsAndPickConflict()
    {
      Stage02PreparationInputDecision decision = Evaluate(
        new[] { 42 },
        explicitPick: true,
        projectInformation: false);

      Assert.False(decision.Success);
      Assert.Contains(decision.Blockers, value => value.Contains("冲突"));
    }

    [Theory]
    [InlineData(true, false, false, Stage02PreparationSelectionMode.ProjectInformation)]
    [InlineData(false, true, false, Stage02PreparationSelectionMode.ExplicitIds)]
    [InlineData(false, false, true, Stage02PreparationSelectionMode.ExplicitPick)]
    [InlineData(false, false, false, Stage02PreparationSelectionMode.CurrentSelection)]
    public void Evaluate_ResolvesExactlyOneSelectionMode(
      bool projectInformation,
      bool hasElementIds,
      bool explicitPick,
      Stage02PreparationSelectionMode expected)
    {
      Stage02PreparationInputDecision decision = Evaluate(
        hasElementIds ? new[] { 42 } : Array.Empty<int>(),
        explicitPick,
        projectInformation);

      Assert.True(decision.Success);
      Assert.Equal(expected, decision.SelectionMode);
    }

    [Fact]
    public void Evaluate_ProjectInformationDefaultsBlankRoleToProject()
    {
      Stage02PreparationInputDecision decision = Stage02PreparationInputPolicy
        .Evaluate(
          "context-a",
          Array.Empty<int>(),
          "  ",
          explicitPick: false,
          projectInformation: true);

      Assert.True(decision.Success);
      Assert.Equal("PROJECT", decision.RoleHint);
    }

    [Theory]
    [InlineData("PROJECT")]
    [InlineData("SITE")]
    [InlineData("BUILDING")]
    public void Evaluate_ProjectInformationAcceptsOnlyDeclaredRoles(string role)
    {
      Stage02PreparationInputDecision accepted = Stage02PreparationInputPolicy
        .Evaluate(
          "context-a",
          Array.Empty<int>(),
          role,
          explicitPick: false,
          projectInformation: true);
      Stage02PreparationInputDecision rejected = Stage02PreparationInputPolicy
        .Evaluate(
          "context-a",
          Array.Empty<int>(),
          "SPACE",
          explicitPick: false,
          projectInformation: true);

      Assert.True(accepted.Success);
      Assert.False(rejected.Success);
      Assert.Contains(rejected.Blockers, value => value.Contains("PROJECT"));
    }

    [Fact]
    public void Evaluate_DeduplicatesAndSortsElementIdsForStableSignature()
    {
      Stage02PreparationInputDecision first = Evaluate(
        new[] { 9, 2, 9, 5 },
        explicitPick: false,
        projectInformation: false);
      Stage02PreparationInputDecision second = Evaluate(
        new[] { 5, 9, 2 },
        explicitPick: false,
        projectInformation: false);

      Assert.Equal(new[] { 2, 5, 9 }, first.ElementIds);
      Assert.Equal(first.ElementIds, second.ElementIds);
      Assert.Equal(first.InputSignature, second.InputSignature);
    }

    [Fact]
    public void Evaluate_ContextHashChangeChangesInputSignature()
    {
      Stage02PreparationInputDecision first = Evaluate(
        new[] { 42 },
        explicitPick: false,
        projectInformation: false,
        contextHash: "context-a");
      Stage02PreparationInputDecision second = Evaluate(
        new[] { 42 },
        explicitPick: false,
        projectInformation: false,
        contextHash: "context-b");

      Assert.NotEqual(first.InputSignature, second.InputSignature);
    }

    [Fact]
    public void Evaluate_SelectionModeChangeChangesInputSignature()
    {
      Stage02PreparationInputDecision current = Evaluate(
        Array.Empty<int>(),
        explicitPick: false,
        projectInformation: false);
      Stage02PreparationInputDecision pick = Evaluate(
        Array.Empty<int>(),
        explicitPick: true,
        projectInformation: false);

      Assert.NotEqual(current.InputSignature, pick.InputSignature);
    }

    [Fact]
    public void Evaluate_ElementIdsChangeChangesInputSignature()
    {
      Stage02PreparationInputDecision first = Evaluate(
        new[] { 42 },
        explicitPick: false,
        projectInformation: false);
      Stage02PreparationInputDecision second = Evaluate(
        new[] { 43 },
        explicitPick: false,
        projectInformation: false);

      Assert.NotEqual(first.InputSignature, second.InputSignature);
    }

    [Fact]
    public void Evaluate_RoleHintChangeChangesInputSignature()
    {
      Stage02PreparationInputDecision first = Stage02PreparationInputPolicy
        .Evaluate(
          "context-a",
          new[] { 42 },
          "PROJECT",
          explicitPick: false,
          projectInformation: false);
      Stage02PreparationInputDecision second = Stage02PreparationInputPolicy
        .Evaluate(
          "context-a",
          new[] { 42 },
          "SITE",
          explicitPick: false,
          projectInformation: false);

      Assert.NotEqual(first.InputSignature, second.InputSignature);
    }

    [Fact]
    public void Evaluate_NormalizesEquivalentRoleHintsBeforeSigning()
    {
      Stage02PreparationInputDecision first = Stage02PreparationInputPolicy
        .Evaluate(
          "context-a",
          new[] { 42 },
          " project ",
          explicitPick: false,
          projectInformation: false);
      Stage02PreparationInputDecision second = Stage02PreparationInputPolicy
        .Evaluate(
          "context-a",
          new[] { 42 },
          "PROJECT",
          explicitPick: false,
          projectInformation: false);

      Assert.Equal("PROJECT", first.RoleHint);
      Assert.Equal(first.RoleHint, second.RoleHint);
      Assert.Equal(first.InputSignature, second.InputSignature);
    }

    [Fact]
    public void ExecutionPolicy_DefersConfirmationWhenBothEdgesRiseTogether()
    {
      Stage02PreparationEdgeDecision decision =
        Stage02PreparationExecutionPolicy.Evaluate(
          previewEdge: true,
          confirmEdge: true);

      Assert.True(decision.ShouldGeneratePreview);
      Assert.False(decision.ShouldConfirmWrite);
      Assert.True(decision.ConfirmationDeferred);
    }

    [Fact]
    public void ExecutionPolicy_AllowsAnIndependentConfirmationEdge()
    {
      Stage02PreparationEdgeDecision decision =
        Stage02PreparationExecutionPolicy.Evaluate(
          previewEdge: false,
          confirmEdge: true);

      Assert.False(decision.ShouldGeneratePreview);
      Assert.True(decision.ShouldConfirmWrite);
      Assert.False(decision.ConfirmationDeferred);
    }

    [Fact]
    public void WriteAttemptState_AssignsOneUniqueTokenPerAttempt()
    {
      var state = new Stage02PreparationWriteAttemptState();

      Guid first = state.BeginAttempt();
      Assert.True(state.IsPending);
      Assert.True(state.IsActive(first));
      Assert.Equal(
        Stage02PreparationWriteCompletionDisposition.Publish,
        state.CompleteAttempt(first, string.Empty));

      Guid second = state.BeginAttempt();
      Assert.NotEqual(Guid.Empty, first);
      Assert.NotEqual(Guid.Empty, second);
      Assert.NotEqual(first, second);
      Assert.True(state.IsActive(second));
    }

    [Fact]
    public void WriteAttemptState_OldDuplicateCannotConsumeRetryAttempt()
    {
      var state = new Stage02PreparationWriteAttemptState();
      Guid first = state.BeginAttempt();
      Assert.Equal(
        Stage02PreparationWriteCompletionDisposition.Publish,
        state.CompleteAttempt(first, "attempt-1.json"));

      Guid second = state.BeginAttempt();
      Assert.Equal(
        Stage02PreparationWriteCompletionDisposition.Ignored,
        state.CompleteAttempt(first, "stale-duplicate.json"));
      Assert.True(state.IsPending);
      Assert.True(state.IsActive(second));
      Assert.Equal("attempt-1.json", state.LastFailureReportPath);

      Assert.Equal(
        Stage02PreparationWriteCompletionDisposition.Publish,
        state.CompleteAttempt(second, string.Empty));
      Assert.False(state.IsPending);
      Assert.Equal("attempt-1.json", state.LastFailureReportPath);
    }

    [Fact]
    public void WriteAttemptState_InputChangeKeepsAttemptPendingUntilCallback()
    {
      var state = new Stage02PreparationWriteAttemptState();
      Assert.Equal(Stage02PreparationWriteAttemptPhase.Idle, state.Phase);

      Guid previous = state.BeginAttempt();
      Assert.Equal(Stage02PreparationWriteAttemptPhase.Pending, state.Phase);
      Assert.Equal(
        Stage02PreparationWriteCompletionDisposition.Publish,
        state.CompleteAttempt(previous, "previous-failure.json"));
      Assert.Equal(Stage02PreparationWriteAttemptPhase.Idle, state.Phase);

      Guid staleWithoutReport = state.BeginAttempt();
      Assert.Equal(Stage02PreparationWriteAttemptPhase.Pending, state.Phase);
      state.MarkActiveAttemptStale();

      Assert.True(state.IsPending);
      Assert.True(state.IsActive(staleWithoutReport));
      Assert.Equal(
        Stage02PreparationWriteAttemptPhase.StalePending,
        state.Phase);
      Assert.Throws<InvalidOperationException>(() =>
      {
        state.BeginAttempt();
      });

      Assert.Equal(
        Stage02PreparationWriteCompletionDisposition.Discarded,
        state.CompleteAttempt(staleWithoutReport, string.Empty));
      Assert.False(state.IsPending);
      Assert.Equal(Stage02PreparationWriteAttemptPhase.Idle, state.Phase);
      Assert.Equal("previous-failure.json", state.LastFailureReportPath);

      Guid staleWithReport = state.BeginAttempt();
      state.MarkActiveAttemptStale();
      Assert.Equal(
        Stage02PreparationWriteAttemptPhase.StalePending,
        state.Phase);
      Assert.Equal(
        Stage02PreparationWriteCompletionDisposition.Discarded,
        state.CompleteAttempt(staleWithReport, "stale-failure.json"));
      Assert.Equal(Stage02PreparationWriteAttemptPhase.Idle, state.Phase);
      Assert.Equal("stale-failure.json", state.LastFailureReportPath);

      Guid current = state.BeginAttempt();
      Assert.Equal(Stage02PreparationWriteAttemptPhase.Pending, state.Phase);
      Assert.Equal(
        Stage02PreparationWriteCompletionDisposition.Ignored,
        state.CompleteAttempt(staleWithReport, "stale-duplicate.json"));
      Assert.True(state.IsActive(current));
      Assert.Equal(Stage02PreparationWriteAttemptPhase.Pending, state.Phase);
      Assert.Equal("stale-failure.json", state.LastFailureReportPath);
      Assert.Equal(
        Stage02PreparationWriteCompletionDisposition.Publish,
        state.CompleteAttempt(current, string.Empty));
      Assert.Equal(Stage02PreparationWriteAttemptPhase.Idle, state.Phase);
      Assert.Equal("stale-failure.json", state.LastFailureReportPath);
    }

    [Fact]
    public void WriteAttemptState_SynchronousCallbackWinsOverEnqueueFailure()
    {
      var state = new Stage02PreparationWriteAttemptState();
      Guid attempt = state.BeginAttempt();

      Stage02PreparationWriteCompletionDisposition callbackDisposition =
        state.CompleteAttempt(attempt, string.Empty);
      Stage02PreparationWriteCompletionDisposition enqueueFailureDisposition =
        state.CompleteAttempt(
          attempt,
          "enqueue-failure-must-not-win.json");

      Assert.Equal(
        Stage02PreparationWriteCompletionDisposition.Publish,
        callbackDisposition);
      Assert.Equal(
        Stage02PreparationWriteCompletionDisposition.Ignored,
        enqueueFailureDisposition);
      Assert.False(state.IsPending);
      Assert.Equal(string.Empty, state.LastFailureReportPath);
    }

    [Fact]
    public void WriteAttemptState_PreservesLastFailureReportUntilNonEmptyReplacement()
    {
      var state = new Stage02PreparationWriteAttemptState();
      Guid failed = state.BeginAttempt();
      Assert.Equal(
        Stage02PreparationWriteCompletionDisposition.Publish,
        state.CompleteAttempt(failed, "first-failure.json"));

      Guid succeeded = state.BeginAttempt();
      Assert.Equal(
        Stage02PreparationWriteCompletionDisposition.Publish,
        state.CompleteAttempt(succeeded, string.Empty));
      Assert.Equal("first-failure.json", state.LastFailureReportPath);

      Guid emptyFailure = state.BeginAttempt();
      Assert.Equal(
        Stage02PreparationWriteCompletionDisposition.Publish,
        state.CompleteAttempt(emptyFailure, "  "));
      Assert.Equal("first-failure.json", state.LastFailureReportPath);

      Guid newerFailure = state.BeginAttempt();
      Assert.Equal(
        Stage02PreparationWriteCompletionDisposition.Publish,
        state.CompleteAttempt(newerFailure, "newer-failure.json"));
      Assert.Equal("newer-failure.json", state.LastFailureReportPath);
    }

    [Fact]
    public void WriteAttemptState_RejectsOverlappingBeginUntilCurrentAttemptEnds()
    {
      var state = new Stage02PreparationWriteAttemptState();
      state.BeginAttempt();

      Assert.Throws<InvalidOperationException>(() =>
      {
        state.BeginAttempt();
      });
      Assert.True(state.IsPending);
    }

    [Fact]
    public void CompletionGate_ConsumerFailureUsesIndependentTerminalPathExactlyOnce()
    {
      Type openGateType = typeof(Stage02PreparationInputPolicy)
        .Assembly
        .GetType(
          "BIMBaoGui.Stage01.Stage02.Stage02PreparationCompletionGate`1");
      Assert.NotNull(openGateType);
      Type gateType = openGateType.MakeGenericType(typeof(object));
      ConstructorInfo constructor = gateType.GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic,
        null,
        new[]
        {
          typeof(Action<object>),
          typeof(Action<Exception>),
          typeof(Action<Exception>),
          typeof(Action)
        },
        null);
      Assert.True(
        constructor != null,
        "Completion gate must expose an independent technical-failure path.");

      var state = new Stage02PreparationWriteAttemptState();
      Guid attemptToken = state.BeginAttempt();
      var expected = new InvalidOperationException(
        "completion consumer sentinel");
      int businessCompletionCount = 0;
      int terminalCount = 0;
      int recordCount = 0;
      int refreshCount = 0;
      object gate = constructor.Invoke(new object[]
      {
        new Action<object>(_ =>
        {
          businessCompletionCount++;
          throw expected;
        }),
        new Action<Exception>(exception =>
        {
          terminalCount++;
          Assert.Same(expected, exception);
          Assert.Equal(
            Stage02PreparationWriteCompletionDisposition.Publish,
            state.CompleteAttempt(attemptToken, string.Empty));
        }),
        new Action<Exception>(exception =>
        {
          recordCount++;
          Assert.Same(expected, exception);
          throw new InvalidOperationException("report writer sentinel");
        }),
        new Action(() => refreshCount++)
      });
      MethodInfo tryComplete = gateType.GetMethod(
        "TryComplete",
        BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.NotNull(tryComplete);

      Assert.True((bool) tryComplete.Invoke(gate, new[] { new object() }));
      Assert.False((bool) tryComplete.Invoke(gate, new[] { new object() }));

      Assert.Equal(1, businessCompletionCount);
      Assert.Equal(1, terminalCount);
      Assert.Equal(1, recordCount);
      Assert.Equal(1, refreshCount);
      Assert.Equal(Stage02PreparationWriteAttemptPhase.Idle, state.Phase);
    }

    [Fact]
    public void FailureReportState_clears_for_new_preview_and_rejects_late_identity()
    {
      Type stateType = typeof(Stage02PreparationInputPolicy)
        .Assembly
        .GetType(
          "BIMBaoGui.Stage01.Stage02.Stage02PreparationFailureReportState");
      Assert.NotNull(stateType);
      object state = Activator.CreateInstance(stateType);
      var observeCurrent = stateType.GetMethod("ObserveCurrent");
      var beginPreview = stateType.GetMethod("BeginPreview");
      var tryPublish = stateType.GetMethod("TryPublish");
      var reportPath = stateType.GetProperty("ReportPath");
      Assert.NotNull(observeCurrent);
      Assert.NotNull(beginPreview);
      Assert.NotNull(tryPublish);
      Assert.NotNull(reportPath);

      observeCurrent.Invoke(state, new object[] { "input-a", "host-a" });
      Assert.True((bool) tryPublish.Invoke(
        state,
        new object[] { "input-a", "host-a", "write-a.json" }));
      Assert.Equal("write-a.json", reportPath.GetValue(state));

      beginPreview.Invoke(state, new object[] { "input-a", "host-a" });
      Assert.Equal(string.Empty, reportPath.GetValue(state));
      Assert.True((bool) tryPublish.Invoke(
        state,
        new object[] { "input-a", "host-a", "preview-a.json" }));

      observeCurrent.Invoke(state, new object[] { "input-b", "host-b" });
      Assert.Equal(string.Empty, reportPath.GetValue(state));
      Assert.False((bool) tryPublish.Invoke(
        state,
        new object[] { "input-a", "host-a", "late-a.json" }));
      Assert.Equal(string.Empty, reportPath.GetValue(state));
      Assert.True((bool) tryPublish.Invoke(
        state,
        new object[] { "input-b", "HOST-B", "preview-b.json" }));
      Assert.Equal("preview-b.json", reportPath.GetValue(state));
    }

    [Fact]
    public void PreviewCounts_DeduplicateGuidsAcrossElementsAndCountSetOperations()
    {
      Stage02PreparationPreviewCounts counts =
        Stage02PreparationPreviewCounts.Calculate(CountedPreview());

      Assert.Equal(3, counts.PendingInstallCount);
      Assert.Equal(1, counts.AlreadyInstalledCount);
      Assert.Equal(4, counts.TotalParameterCount);
      Assert.Equal(3, counts.PendingWriteCount);
    }

    [Fact]
    public void PreviewCounts_NullAndEmptyPreviewReturnZero()
    {
      Stage02PreparationPreviewCounts nullCounts =
        Stage02PreparationPreviewCounts.Calculate(null);
      Stage02PreparationPreviewCounts emptyCounts =
        Stage02PreparationPreviewCounts.Calculate(Preview());

      AssertCountsAreZero(nullCounts);
      AssertCountsAreZero(emptyCounts);
    }

    [Fact]
    public void PreviewCountCache_PublishClearAndRepublishReplaceSnapshots()
    {
      var cache = new Stage02PreparationPreviewCountCache();
      AssertCountsAreZero(cache.Current);

      cache.Publish(CountedPreview());
      Stage02PreparationPreviewCounts first = cache.Current;
      Assert.Equal(4, first.TotalParameterCount);
      Assert.Equal(3, first.PendingWriteCount);

      cache.Clear();
      AssertCountsAreZero(cache.Current);
      Assert.Equal(4, first.TotalParameterCount);
      Assert.Equal(3, first.PendingWriteCount);

      Stage02Preview replacement = Preview(Element(
        "replacement",
        103,
        Operation(
          "replacement",
          "P-ONLY",
          Guid.Parse("55555555-5555-5555-5555-555555555555"),
          HbrBindingActions.Reuse,
          "SET")));
      cache.Publish(replacement);
      Assert.Equal(0, cache.Current.PendingInstallCount);
      Assert.Equal(1, cache.Current.AlreadyInstalledCount);
      Assert.Equal(1, cache.Current.TotalParameterCount);
      Assert.Equal(1, cache.Current.PendingWriteCount);
    }

    [Fact]
    public void WritePublicationPolicy_SuccessPublishesUniqueTotalsAndSetCount()
    {
      Stage02PreparationWritePublicationDecision decision =
        Stage02PreparationWritePublicationPolicy.Evaluate(
          Stage02PreparationPreviewCounts.Calculate(CountedPreview()),
          currentInstalledCount: 1,
          success: true,
          requiresNewPreview: false);

      Assert.True(decision.ClearPreview);
      Assert.Equal(4, decision.InstalledCount);
      Assert.Equal(3, decision.WrittenCount);
    }

    [Fact]
    public void WritePublicationPolicy_RetryKeepsPreviewAndCurrentInstalledCount()
    {
      Stage02PreparationWritePublicationDecision decision =
        Stage02PreparationWritePublicationPolicy.Evaluate(
          Stage02PreparationPreviewCounts.Calculate(CountedPreview()),
          currentInstalledCount: 2,
          success: false,
          requiresNewPreview: false);

      Assert.False(decision.ClearPreview);
      Assert.Equal(2, decision.InstalledCount);
      Assert.Equal(0, decision.WrittenCount);
    }

    [Fact]
    public void WritePublicationPolicy_NewPreviewFailureClearsPreviewAndCounts()
    {
      Stage02PreparationWritePublicationDecision decision =
        Stage02PreparationWritePublicationPolicy.Evaluate(
          Stage02PreparationPreviewCounts.Calculate(CountedPreview()),
          currentInstalledCount: 2,
          success: false,
          requiresNewPreview: true);

      Assert.True(decision.ClearPreview);
      Assert.Equal(0, decision.InstalledCount);
      Assert.Equal(0, decision.WrittenCount);
    }

    [Fact]
    public void FieldDetailFormatter_RoundTripsEscapedValuesWithStableBytes()
    {
      string tricky = "甲｜来源=伪造;:\\"
        + "\n"
        + "\\n"
        + "｜\"引号\"\t\u0001";
      Guid parameterGuid = Guid.Parse(
        "66666666-6666-6666-6666-666666666666");
      var observed = new Stage02ObservedParameterState(
        "element-json",
        true,
        true,
        "GUID",
        "INSTANCE",
        "STRING",
        new[] { "OST_Walls" },
        false,
        "STRING",
        tricky,
        tricky,
        "canonical-guid",
        tricky);
      Stage02WriteOperation operation = new Stage02WriteOperation(
          "PROPERTY｜:" + tricky,
          parameterGuid,
          "参数名" + tricky,
          observed,
          "建议值" + tricky,
          "来源" + tricky,
          "EXACT",
          HbrBindingActions.MergeCategories,
          "SET",
          "APPLICABLE" + tricky,
          new[]
          {
            new Stage02Blocker("Z_CODE", "Z" + tricky),
            new Stage02Blocker("A_CODE", "A" + tricky)
          })
        .WithRuleMetadata(
          observed,
          "INSTANCE",
          "STRING",
          "TEXT",
          "REQUIRED" + tricky,
          "CONDITION");
      var reference = new Stage02ElementReference(
        "document-fingerprint",
        "测试文档",
        201,
        "element-json",
        "类别" + tricky,
        "Wall",
        "测试族",
        "测试类型",
        "名称" + tricky);
      var element = new Stage02MatchedElement(
        reference,
        "角色" + tricky,
        Stage02MatchSources.RoleHint,
        new[] { operation });

      string first = Stage02PreparationFieldDetailFormatter.Format(
        element,
        operation);
      string second = Stage02PreparationFieldDetailFormatter.Format(
        element,
        operation);

      Assert.Equal(
        Encoding.UTF8.GetBytes(first),
        Encoding.UTF8.GetBytes(second));
      Assert.DoesNotContain("\n", first);
      Assert.DoesNotContain("\r", first);
      Assert.DoesNotContain("\t", first);
      Assert.DoesNotContain("\u0001", first);
      AssertTopLevelKeyOrder(first);

      var serializer = new JavaScriptSerializer();
      var root = Assert.IsType<Dictionary<string, object>>(
        serializer.DeserializeObject(first));
      Assert.Equal(19, root.Count);
      Assert.Equal("document-fingerprint", root["documentFingerprint"]);
      Assert.Equal("测试文档", root["documentTitle"]);
      Assert.Equal(201, Convert.ToInt32(root["elementId"]));
      Assert.Equal("element-json", root["uniqueId"]);
      Assert.Equal("名称" + tricky, root["elementName"]);
      Assert.Equal("类别" + tricky, root["category"]);
      Assert.Equal("角色" + tricky, root["role"]);
      Assert.Equal("INSTANCE", root["scope"]);
      Assert.Equal("PROPERTY｜:" + tricky, root["propertyId"]);
      Assert.Equal(parameterGuid.ToString("D"), root["parameterGuid"]);
      Assert.Equal("参数名" + tricky, root["parameterName"]);
      Assert.Equal(tricky, root["oldValue"]);
      Assert.Equal("建议值" + tricky, root["suggestedValue"]);
      Assert.Equal("来源" + tricky, root["source"]);
      Assert.Equal("REQUIRED" + tricky, root["requirementLevel"]);
      Assert.Equal("APPLICABLE" + tricky, root["applicability"]);
      Assert.Equal(HbrBindingActions.MergeCategories, root["bindingAction"]);
      Assert.Equal("SET", root["valueAction"]);
      object[] blockers = Assert.IsType<object[]>(root["blockers"]);
      Assert.Equal(2, blockers.Length);
      var firstBlocker = Assert.IsType<Dictionary<string, object>>(
        blockers[0]);
      var secondBlocker = Assert.IsType<Dictionary<string, object>>(
        blockers[1]);
      Assert.Equal("A_CODE", firstBlocker["code"]);
      Assert.Equal("A" + tricky, firstBlocker["message"]);
      Assert.Equal("Z_CODE", secondBlocker["code"]);
      Assert.Equal("Z" + tricky, secondBlocker["message"]);
    }

    private static Stage02PreparationInputDecision Evaluate(
      int[] elementIds,
      bool explicitPick,
      bool projectInformation,
      string contextHash = "context-a")
    {
      return Stage02PreparationInputPolicy.Evaluate(
        contextHash,
        elementIds,
        string.Empty,
        explicitPick,
        projectInformation);
    }

    private static Stage02Preview CountedPreview()
    {
      Guid first = Guid.Parse("11111111-1111-1111-1111-111111111111");
      Guid second = Guid.Parse("22222222-2222-2222-2222-222222222222");
      Guid third = Guid.Parse("33333333-3333-3333-3333-333333333333");
      Guid fourth = Guid.Parse("44444444-4444-4444-4444-444444444444");
      Stage02MatchedElement firstElement = Element(
        "element-a",
        101,
        Operation(
          "element-a",
          "P-A-1",
          first,
          HbrBindingActions.Reuse,
          "SET"),
        Operation(
          "element-a",
          "P-B-1",
          second,
          HbrBindingActions.Reuse,
          "NO_CHANGE"),
        Operation(
          "element-a",
          "P-C-1",
          third,
          HbrBindingActions.MergeCategories,
          "SET"));
      Stage02MatchedElement secondElement = Element(
        "element-b",
        102,
        Operation(
          "element-b",
          "P-A-2",
          first,
          HbrBindingActions.CreateAndBind,
          "NO_CHANGE"),
        Operation(
          "element-b",
          "P-B-2",
          second,
          HbrBindingActions.Reuse,
          "SET"),
        Operation(
          "element-b",
          "P-D-1",
          fourth,
          HbrBindingActions.BindExisting,
          "NO_CHANGE"));
      return Preview(firstElement, secondElement);
    }

    private static Stage02Preview Preview(
      params Stage02MatchedElement[] elements)
    {
      var request = new Stage02PreviewRequest(
        "file-guid",
        "document-fingerprint",
        "context-hash",
        "profile",
        "rule-package",
        "1.0.0",
        "rule-sha",
        "nonce",
        elements);
      return new Stage02Preview(
        request,
        elements,
        "canonical-payload",
        "preview-hash");
    }

    private static Stage02MatchedElement Element(
      string uniqueId,
      int elementId,
      params Stage02WriteOperation[] operations)
    {
      var element = new Stage02ElementReference(
        "document-fingerprint",
        "测试文档",
        elementId,
        uniqueId,
        "OST_Walls",
        "Wall",
        "测试族",
        "测试类型",
        "测试元素");
      return new Stage02MatchedElement(
        element,
        "WALL",
        Stage02MatchSources.RoleHint,
        operations);
    }

    private static Stage02WriteOperation Operation(
      string targetUniqueId,
      string propertyId,
      Guid parameterGuid,
      string bindingAction,
      string valueAction)
    {
      var observed = new Stage02ObservedParameterState(
        targetUniqueId,
        true,
        true,
        "GUID",
        "INSTANCE",
        "STRING",
        new[] { "OST_Walls" },
        false,
        "STRING",
        "旧值",
        "旧值",
        "canonical-guid",
        "旧值");
      return new Stage02WriteOperation(
        propertyId,
        parameterGuid,
        "测试参数",
        observed,
        "建议值",
        "TEST",
        "EXACT",
        bindingAction,
        valueAction);
    }

    private static void AssertCountsAreZero(
      Stage02PreparationPreviewCounts counts)
    {
      Assert.Equal(0, counts.PendingInstallCount);
      Assert.Equal(0, counts.AlreadyInstalledCount);
      Assert.Equal(0, counts.TotalParameterCount);
      Assert.Equal(0, counts.PendingWriteCount);
    }

    private static void AssertTopLevelKeyOrder(string json)
    {
      string[] orderedKeys =
      {
        "\"documentFingerprint\":",
        "\"documentTitle\":",
        "\"elementId\":",
        "\"uniqueId\":",
        "\"elementName\":",
        "\"category\":",
        "\"role\":",
        "\"scope\":",
        "\"propertyId\":",
        "\"parameterGuid\":",
        "\"parameterName\":",
        "\"oldValue\":",
        "\"suggestedValue\":",
        "\"source\":",
        "\"requirementLevel\":",
        "\"applicability\":",
        "\"bindingAction\":",
        "\"valueAction\":",
        "\"blockers\":"
      };
      int previous = -1;
      foreach (string key in orderedKeys)
      {
        int current = json.IndexOf(key, StringComparison.Ordinal);
        Assert.True(current > previous);
        previous = current;
      }
    }
  }
}
