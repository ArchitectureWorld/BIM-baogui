using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal sealed class NativeStage02WriteRequest
  {
    internal NativeStage02Preview Preview { get; set; }
    internal NativeStage02PreviewRequest ResolvedRequest { get; set; }

    internal NativeStage02WriteRequest Clone()
    {
      return new NativeStage02WriteRequest
      {
        Preview = Preview,
        ResolvedRequest = ResolvedRequest?.Clone()
      };
    }
  }

  internal sealed class NativeStage02WriteResult
  {
    internal bool Success { get; set; }
    internal bool PartialSuccess { get; set; }
    internal bool RequiresNewPreview { get; set; }
    internal string Status { get; set; } = string.Empty;
    internal int PreparedParameterCount { get; set; }
    internal int WrittenElementCount { get; set; }
    internal int FailedParameterCount { get; set; }
    internal int FailedElementCount { get; set; }
    internal int AssignedElementCount { get; set; }
    internal int RemovedAssignmentCount { get; set; }
    internal int FailedAssignmentCount { get; set; }
    internal IReadOnlyList<string> Messages { get; set; } = Array.Empty<string>();
    internal NativeStage02Preview RefreshedPreview { get; set; }
    internal NativeStage02PreviewRequest ResolvedRequest { get; set; }
    internal IReadOnlyList<NativeStage02ElementWriteOutcome> ElementOutcomes
    {
      get;
      set;
    } = Array.Empty<NativeStage02ElementWriteOutcome>();
    internal NativeWorkflowResultEnvelope WorkflowResult { get; set; }
    internal bool ScopeComplete { get; set; }
  }

  internal sealed class NativeStage02ElementWriteOutcome
  {
    internal int ElementId { get; set; }
    internal string ElementUniqueId { get; set; } = string.Empty;
    internal string RoleId { get; set; } = string.Empty;
    internal bool Succeeded { get; set; }
    internal string GeometryEvidenceHash { get; set; } = string.Empty;
    internal IReadOnlyList<NativeWorkflowItemEvidence> GeometryOutcomes
    {
      get;
      set;
    } = Array.Empty<NativeWorkflowItemEvidence>();
    internal IReadOnlyList<NativeWorkflowItemEvidence> FieldOutcomes
    {
      get;
      set;
    } = Array.Empty<NativeWorkflowItemEvidence>();
    internal string ErrorCode { get; set; } = string.Empty;
  }

  internal static class NativeStage02RevitWriteService
  {
    internal static NativeStage02WriteResult Execute(
      UIApplication uiApplication,
      NativeStage02WriteRequest request)
    {
      if (uiApplication == null) throw new ArgumentNullException(nameof(uiApplication));
      if (request?.Preview == null || request.ResolvedRequest == null)
        return Failure(true, "写入请求缺少已确认的 Stage02 预览。" );
      Document document = uiApplication.ActiveUIDocument?.Document;
      if (document == null) return Failure(true, "当前没有活动 Revit 项目文档。" );

      NativeStage02RevitPreviewResult rebuilt = RebuildPreview(
        uiApplication,
        request.ResolvedRequest);
      if (!rebuilt.Success || rebuilt.Preview == null)
      {
        return new NativeStage02WriteResult
        {
          Success = false,
          RequiresNewPreview = true,
          Status = "Stage02 写入前回读失败",
          Messages = rebuilt.Messages,
          ResolvedRequest = rebuilt.ResolvedRequest
        };
      }
      if (!string.Equals(
        request.Preview.PreviewHash,
        rebuilt.Preview.PreviewHash,
        StringComparison.OrdinalIgnoreCase))
      {
        return new NativeStage02WriteResult
        {
          Success = false,
          RequiresNewPreview = true,
          Status = "Stage02 预览已过期",
          Messages = new[]
          {
            "当前模型、参数或选择已发生变化；必须重新生成预览后再确认写入。",
            "原预览=" + request.Preview.PreviewHash,
            "当前预览=" + rebuilt.Preview.PreviewHash
          },
          RefreshedPreview = rebuilt.Preview,
          ResolvedRequest = rebuilt.ResolvedRequest
        };
      }

      NativeStage02SemanticAssignmentStorageSnapshot storedSnapshot =
        NativeStage02SemanticAssignmentStorage.Read(document);
      string[] existingUniqueIds = new FilteredElementCollector(document)
        .WhereElementIsNotElementType()
        .Select(value => value.UniqueId)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToArray();
      NativeStage02SemanticAssignmentStorageDecision storageDecision =
        NativeStage02SemanticAssignmentStoragePolicy.Evaluate(
          storedSnapshot,
          existingUniqueIds);
      if (storageDecision.State == NativeStage02SemanticAssignmentStorageState.Corrupt
        || storageDecision.State == NativeStage02SemanticAssignmentStorageState.UnsupportedFuture)
      {
        return Failure(
          true,
          "Stage02 语义角色存储不可写：" + storageDecision.State,
          storageDecision.Message);
      }
      NativeStage02SemanticAssignmentPayload assignmentPayload =
        storageDecision.Payload ?? new NativeStage02SemanticAssignmentPayload
        {
          SchemaVersion = NativeStage02SemanticAssignmentSchema.Version,
          RulePackageId = NativeStage02RuleCatalog.Current.Identity.PackageId,
          RulePackageVersion = NativeStage02RuleCatalog.Current.Identity.PackageVersion,
          Assignments = Array.Empty<NativeStage02SemanticAssignmentRecord>()
        };
      assignmentPayload.RulePackageId = NativeStage02RuleCatalog.Current.Identity.PackageId;
      assignmentPayload.RulePackageVersion = NativeStage02RuleCatalog.Current.Identity.PackageVersion;
      assignmentPayload.SchemaVersion = NativeStage02SemanticAssignmentSchema.Version;

      var messages = new List<string>();
      var failedPropertyIds = new HashSet<string>(StringComparer.Ordinal);
      string workflowUpdatedUtc = DateTime.UtcNow.ToString(
        "O",
        CultureInfo.InvariantCulture);
      var outcomes = new Dictionary<string, NativeStage02ElementWriteOutcome>(
        StringComparer.Ordinal);
      foreach (NativeStage02ElementPlan plan in rebuilt.Preview.Elements)
        outcomes.Add(plan.Element.UniqueId, CreateOutcome(plan, workflowUpdatedUtc));
      int preparedParameters = 0;
      int failedParameters = 0;
      foreach (BindingWork work in CollectBindingWork(rebuilt.Preview))
      {
        using (Transaction transaction = new Transaction(
          document,
          "HBR Stage02 参数 " + work.Property.IfcProperty))
        {
          bool started = false;
          try
          {
            if (transaction.Start() != TransactionStatus.Started)
              throw new InvalidOperationException("无法启动参数绑定事务。" );
            started = true;
            NativeStage02ParameterBindingService.Ensure(
              document,
              work.Property,
              work.CategoryKeys);
            document.Regenerate();
            if (transaction.Commit() != TransactionStatus.Committed)
              throw new InvalidOperationException("参数绑定事务未提交。" );
            started = false;
            preparedParameters++;
          }
          catch (Exception exception)
          {
            if (started) transaction.RollBack();
            failedParameters++;
            failedPropertyIds.Add(work.Property.PropertyId);
            messages.Add(
              "参数准备失败｜" + work.Property.IfcEntity + " / "
              + work.Property.IfcPropertySet + " / " + work.Property.IfcProperty
              + "｜" + exception.Message);
          }
        }
      }

      int writtenElements = 0;
      int failedElements = 0;
      int assignedElements = 0;
      int removedAssignments = 0;
      int failedAssignments = 0;
      foreach (NativeStage02ElementPlan elementPlan in rebuilt.Preview.Elements
        .OrderBy(value => value.Element.UniqueId, StringComparer.Ordinal))
      {
        NativeStage02ElementWriteOutcome outcome =
          outcomes[elementPlan.Element.UniqueId];
        if (elementPlan.IsBlocked)
        {
          outcome.Succeeded = false;
          outcome.ErrorCode = elementPlan.RoleConfirmation != null
            && !elementPlan.RoleConfirmation.Confirmed
              ? elementPlan.RoleConfirmation.Code
              : "STAGE02A_ELEMENT_BLOCKED";
          continue;
        }
        outcome.Succeeded = true;
        bool saveManualAssignment = string.Equals(
          elementPlan.AssignmentAction,
          NativeStage02AssignmentActions.SaveManualAssignment,
          StringComparison.Ordinal);
        bool removeManualAssignment = string.Equals(
          elementPlan.AssignmentAction,
          NativeStage02AssignmentActions.RemoveManualAssignment,
          StringComparison.Ordinal);
        bool changeAssignment = saveManualAssignment || removeManualAssignment;
        NativeStage02FieldPlan[] writes = elementPlan.Fields
          .Where(value => value.ValueAction == NativeStage02ValueAction.Set)
          .ToArray();
        NativeStage02FieldPlan[] requiredBindings = elementPlan.Fields
          .Where(value => value.BindingAction == NativeStage02BindingAction.Create
            || value.BindingAction == NativeStage02BindingAction.MergeCategories)
          .ToArray();
        if (!changeAssignment && writes.Length == 0)
        {
          outcome.FieldOutcomes = BuildFieldOutcomes(
            elementPlan,
            true,
            workflowUpdatedUtc,
            string.Empty);
          continue;
        }
        if (requiredBindings.Any(value => failedPropertyIds.Contains(
          value.Property.PropertyId)))
        {
          failedElements++;
          outcome.Succeeded = false;
          outcome.ErrorCode = "PARAMETER_BINDING_FAILED";
          if (changeAssignment) failedAssignments++;
          messages.Add(
            "构件跳过｜Id="
            + elementPlan.Element.ElementId.ToString(CultureInfo.InvariantCulture)
            + "｜依赖的参数绑定失败。" );
          continue;
        }

        Element live = document.GetElement(elementPlan.Element.UniqueId);
        if (live == null)
        {
          failedElements++;
          outcome.Succeeded = false;
          outcome.ErrorCode = "ELEMENT_NOT_FOUND";
          if (changeAssignment) failedAssignments++;
          messages.Add("构件跳过｜UniqueId=" + elementPlan.Element.UniqueId
            + "｜确认写入时元素已不存在。" );
          continue;
        }

        using (Transaction transaction = new Transaction(
          document,
          "HBR Stage02 构件 " + elementPlan.Element.ElementId))
        {
          bool started = false;
          try
          {
            NativeStage02SemanticAssignmentPayload candidatePayload =
              NativeStage02SemanticAssignmentWritePolicy.Apply(
                assignmentPayload,
                elementPlan);
            if (saveManualAssignment
              && elementPlan.RoleConfirmation?.Confirmation != null)
            {
              NativeStage02RoleConfirmation confirmation =
                elementPlan.RoleConfirmation.Confirmation;
              candidatePayload =
                NativeStage02SemanticAssignmentCanonicalizer.Upsert(
                  candidatePayload,
                  new NativeStage02SemanticAssignmentRecord
                  {
                    ElementUniqueId = elementPlan.Element.UniqueId,
                    RoleId = elementPlan.EffectiveRoleId,
                    AssignmentMode = NativeStage02AssignmentMode.Manual,
                    CarrierCategory = elementPlan.Element.Category,
                    CarrierElementKind = elementPlan.Element.ElementKind,
                    RulePackageSha256 = confirmation.RulePackageSha256,
                    ElementSnapshotHash = confirmation.ElementSnapshotHash,
                    ConfirmedUtc = confirmation.ConfirmedUtc
                  });
            }
            if (transaction.Start() != TransactionStatus.Started)
              throw new InvalidOperationException("无法启动构件写入事务。" );
            started = true;
            foreach (NativeStage02FieldPlan field in writes)
            {
              Element target = NativeStage02RevitService.ResolveTarget(
                document,
                live,
                field.Property.BindingScope);
              Parameter parameter = target.get_Parameter(field.Property.ParameterGuid)
                ?? throw new InvalidOperationException(
                  "绑定后仍无法取得固定 GUID 参数：" + field.Property.ParameterName);
              NativeStage02ValueCodec.WriteAndVerify(
                parameter,
                field.Property,
                field.ProposedCanonicalValue);
            }

            if (changeAssignment)
            {
              NativeStage02SemanticAssignmentStorageSnapshot candidateSnapshot =
                NativeStage02SemanticAssignmentStoragePolicy.CreateSnapshot(
                  candidatePayload,
                  DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
              NativeStage02SemanticAssignmentStorage.Write(
                document,
                candidateSnapshot);
            }

            document.Regenerate();
            foreach (NativeStage02FieldPlan field in writes)
            {
              Element target = NativeStage02RevitService.ResolveTarget(
                document,
                live,
                field.Property.BindingScope);
              Parameter parameter = target.get_Parameter(
                field.Property.ParameterGuid)
                ?? throw new InvalidOperationException(
                  "PARAMETER_READBACK_FAILED：无法按固定 GUID 回读参数："
                  + field.Property.ParameterName);
              string actual = NativeStage02ValueCodec.Read(
                parameter,
                field.Property);
              if (!string.Equals(
                actual,
                field.ProposedCanonicalValue,
                StringComparison.Ordinal))
              {
                throw new InvalidOperationException(
                  "PARAMETER_READBACK_FAILED：参数写入后回读不一致："
                  + field.Property.ParameterName);
              }
            }

            if (changeAssignment)
            {
              NativeStage02SemanticAssignmentStorageSnapshot roundTrip =
                NativeStage02SemanticAssignmentStorage.Read(document);
              NativeStage02SemanticAssignmentStorageDecision roundTripDecision =
                NativeStage02SemanticAssignmentStoragePolicy.Evaluate(
                  roundTrip,
                  existingUniqueIds);
              if (roundTripDecision.State
                != NativeStage02SemanticAssignmentStorageState.Current)
              {
                throw new InvalidOperationException(
                  NativeStage02SemanticAssignmentWritePolicy.ReadbackFailed
                  + "：" + roundTripDecision.Message);
              }
              NativeStage02SemanticAssignmentReadbackDecision readback =
                NativeStage02SemanticAssignmentWritePolicy.Verify(
                  roundTripDecision.Payload,
                  elementPlan);
              if (!readback.Success)
              {
                throw new InvalidOperationException(
                  readback.ErrorCode + "：" + readback.Message);
              }
            }

            if (transaction.Commit() != TransactionStatus.Committed)
              throw new InvalidOperationException("构件写入事务未提交。" );
            started = false;
            assignmentPayload = candidatePayload;
            if (saveManualAssignment) assignedElements++;
            if (removeManualAssignment) removedAssignments++;
            writtenElements++;
            outcome.Succeeded = true;
            outcome.ErrorCode = string.Empty;
            outcome.FieldOutcomes = BuildFieldOutcomes(
              elementPlan,
              true,
              workflowUpdatedUtc,
              string.Empty);
          }
          catch (Exception exception)
          {
            if (started) transaction.RollBack();
            failedElements++;
            outcome.Succeeded = false;
            outcome.ErrorCode = "ELEMENT_TRANSACTION_FAILED";
            outcome.FieldOutcomes = BuildFieldOutcomes(
              elementPlan,
              false,
              workflowUpdatedUtc,
              outcome.ErrorCode);
            if (changeAssignment) failedAssignments++;
            messages.Add(
              "构件写入失败｜Id="
              + elementPlan.Element.ElementId.ToString(CultureInfo.InvariantCulture)
              + "｜" + exception.Message);
          }
        }
      }

      bool scopeComplete = rebuilt.Preview.ScopeMode
        == NativeStage02ScopeMode.FullModel;
      string inputSnapshotHash =
        NativeStage02SemanticAssignmentCanonicalizer.Sha256(string.Join(
          "\u001f",
          rebuilt.Preview.Elements
            .OrderBy(value => value.Element.UniqueId, StringComparer.Ordinal)
            .Select(value => string.IsNullOrWhiteSpace(value.ElementSnapshotHash)
              ? NativeStage02ElementSnapshotCanonicalizer.Sha256(value.Element)
              : value.ElementSnapshotHash)));
      var workflowItems = outcomes.Values
        .OrderBy(value => value.ElementUniqueId, StringComparer.Ordinal)
        .SelectMany(value => value.GeometryOutcomes
          .Concat(value.FieldOutcomes))
        .ToList();
      foreach (NativeStage02ElementPlan plan in rebuilt.Preview.Elements)
      {
        NativeStage02ElementWriteOutcome outcome = outcomes[plan.Element.UniqueId];
        workflowItems.Add(BuildRoleOutcome(
          plan,
          outcome.Succeeded,
          workflowUpdatedUtc,
          outcome.ErrorCode));
      }
      workflowItems.Add(new NativeWorkflowItemEvidence
      {
        Identity = "SCOPE_COMPLETE",
        CurrentValue = scopeComplete ? "true" : "false",
        Source = "STAGE02A_SCOPE",
        WriteSucceeded = true,
        ReadbackSucceeded = true,
        InputHash = inputSnapshotHash,
        UpdatedUtc = workflowUpdatedUtc,
        ErrorCode = scopeComplete ? string.Empty : "PARTIAL_SCOPE"
      });
      NativeWorkflowResultEnvelope workflowResult =
        NativeWorkflowResultCanonicalizer.Build(
          string.IsNullOrWhiteSpace(rebuilt.Preview.RunId)
            ? Guid.NewGuid().ToString("D")
            : rebuilt.Preview.RunId,
          "STAGE02A",
          "ELEMENT_PREPARATION",
          new NativeWorkflowIdentity
          {
            DocumentFingerprint = rebuilt.Preview.DocumentFingerprint,
            ModelFileType = rebuilt.Preview.ModelProfile,
            RulePackageId = rebuilt.Preview.RulePackageId,
            RulePackageVersion = rebuilt.Preview.RulePackageVersion,
            RulePackageSha256 = rebuilt.Preview.RulePackageSha256
          },
          inputSnapshotHash,
          workflowItems,
          workflowUpdatedUtc);
      try
      {
        using (Transaction workflowTransaction = new Transaction(
          document,
          "HBR Stage02A workflow result"))
        {
          if (workflowTransaction.Start() != TransactionStatus.Started)
            throw new InvalidOperationException("无法启动 workflow result 事务。");
          NativeWorkflowResultStorage.Write(document, workflowResult);
          if (workflowTransaction.Commit() != TransactionStatus.Committed)
            throw new InvalidOperationException("workflow result 事务未提交。");
        }
      }
      catch (Exception exception)
      {
        messages.Add("WORKFLOW_RESULT_PERSISTENCE_FAILED：" + exception.Message);
        return new NativeStage02WriteResult
        {
          Success = false,
          PartialSuccess = preparedParameters > 0 || writtenElements > 0,
          RequiresNewPreview = true,
          Status = "Stage02A 技术失败：workflow result 未持久化",
          PreparedParameterCount = preparedParameters,
          WrittenElementCount = writtenElements,
          FailedParameterCount = failedParameters,
          FailedElementCount = failedElements,
          AssignedElementCount = assignedElements,
          RemovedAssignmentCount = removedAssignments,
          FailedAssignmentCount = failedAssignments,
          Messages = new ReadOnlyCollection<string>(messages),
          ElementOutcomes = outcomes.Values
            .OrderBy(value => value.ElementUniqueId, StringComparer.Ordinal)
            .ToArray(),
          ScopeComplete = scopeComplete,
          WorkflowResult = null,
          ResolvedRequest = rebuilt.ResolvedRequest
        };
      }

      NativeStage02RevitPreviewResult refreshed = RebuildPreview(
        uiApplication,
        rebuilt.ResolvedRequest);
      bool hasFailures = failedParameters > 0 || failedElements > 0;
      bool hasSuccess = preparedParameters > 0 || writtenElements > 0;
      if (messages.Count == 0)
      {
        messages.Add(
          "参数准备=" + preparedParameters.ToString(CultureInfo.InvariantCulture)
          + "，构件写入/角色保存="
          + writtenElements.ToString(CultureInfo.InvariantCulture) + "。" );
      }
      return new NativeStage02WriteResult
      {
        Success = !hasFailures,
        PartialSuccess = hasFailures && hasSuccess,
        RequiresNewPreview = !refreshed.Success,
        Status = !hasFailures
          ? "Stage02 写入完成"
          : hasSuccess ? "Stage02 部分成功" : "Stage02 写入失败",
        PreparedParameterCount = preparedParameters,
        WrittenElementCount = writtenElements,
        FailedParameterCount = failedParameters,
        FailedElementCount = failedElements,
        AssignedElementCount = assignedElements,
        RemovedAssignmentCount = removedAssignments,
        FailedAssignmentCount = failedAssignments,
        Messages = new ReadOnlyCollection<string>(messages),
        ElementOutcomes = outcomes.Values
          .OrderBy(value => value.ElementUniqueId, StringComparer.Ordinal)
          .ToArray(),
        WorkflowResult = workflowResult,
        ScopeComplete = scopeComplete,
        RefreshedPreview = refreshed.Preview,
        ResolvedRequest = refreshed.ResolvedRequest
      };
    }

    private static NativeStage02RevitPreviewResult RebuildPreview(
      UIApplication uiApplication,
      NativeStage02PreviewRequest request)
    {
      return NativeStage02RevitService.CreatePreview(
        uiApplication,
        request?.Clone() ?? new NativeStage02PreviewRequest());
    }

    private static IReadOnlyList<BindingWork> CollectBindingWork(
      NativeStage02Preview preview)
    {
      var byProperty = new Dictionary<string, BindingWork>(StringComparer.Ordinal);
      foreach (NativeStage02ElementPlan element in preview.Elements)
      {
        if (element.IsBlocked) continue;
        foreach (NativeStage02FieldPlan field in element.Fields)
        {
          if (field.BindingAction != NativeStage02BindingAction.Create
            && field.BindingAction != NativeStage02BindingAction.MergeCategories)
            continue;
          BindingWork work;
          if (!byProperty.TryGetValue(field.Property.PropertyId, out work))
          {
            work = new BindingWork(field.Property);
            byProperty.Add(field.Property.PropertyId, work);
          }
          work.AddCategory(element.Element.Category);
        }
      }
      return byProperty.Values
        .OrderBy(value => value.Property.PropertyId, StringComparer.Ordinal)
        .ToArray();
    }

    private static NativeStage02ElementWriteOutcome CreateOutcome(
      NativeStage02ElementPlan plan,
      string updatedUtc)
    {
      string snapshotHash = SnapshotHash(plan);
      string geometryHash = plan.Element.Geometry?.EvidenceHash ?? string.Empty;
      NativeWorkflowItemEvidence[] geometryOutcomes =
        (plan.TaskGeometry?.Checks
          ?? Array.Empty<NativeStage02GeometryCheckEvidence>())
        .Select(check =>
        {
          bool passed = check.State == NativeStage02GeometryCheckState.Passed
            || check.State == NativeStage02GeometryCheckState.ManualReviewApproved;
          return new NativeWorkflowItemEvidence
          {
            Identity = plan.Element.UniqueId + "|" + check.CheckId,
            CurrentValue = "GeometryEvidenceHash=" + geometryHash
              + ";ManualReviewRecordHash="
              + (check.ManualReviewRecordHash ?? string.Empty)
              + ";State=" + check.State + ";Code=" + check.Code,
            Source = "STAGE02A_GEOMETRY",
            WriteSucceeded = passed,
            ReadbackSucceeded = passed,
            InputHash = HashOrFallback(geometryHash, snapshotHash),
            UpdatedUtc = updatedUtc,
            ErrorCode = passed ? string.Empty : check.Code
          };
        })
        .ToArray();
      return new NativeStage02ElementWriteOutcome
      {
        ElementId = plan.Element.ElementId,
        ElementUniqueId = plan.Element.UniqueId,
        RoleId = plan.EffectiveRoleId,
        GeometryEvidenceHash = geometryHash,
        GeometryOutcomes = geometryOutcomes,
        FieldOutcomes = BuildFieldOutcomes(
          plan,
          false,
          updatedUtc,
          "NOT_WRITTEN")
      };
    }

    private static IReadOnlyList<NativeWorkflowItemEvidence> BuildFieldOutcomes(
      NativeStage02ElementPlan plan,
      bool transactionSucceeded,
      string updatedUtc,
      string transactionError)
    {
      string snapshotHash = SnapshotHash(plan);
      return (plan.Fields ?? Array.Empty<NativeStage02FieldPlan>())
        .Select(field =>
        {
          bool ready = field.Status == NativeStage02FieldStatus.Correct
            || field.Status == NativeStage02FieldStatus.PendingBinding
            || field.Status == NativeStage02FieldStatus.PendingWrite
            || field.Status == NativeStage02FieldStatus.NotApplicable;
          bool succeeded = transactionSucceeded && ready;
          string error = succeeded
            ? string.Empty
            : !string.IsNullOrWhiteSpace(transactionError)
              ? transactionError
              : "FIELD_" + field.Status.ToString().ToUpperInvariant();
          string input = NativeStage02SemanticAssignmentCanonicalizer.Sha256(
            snapshotHash + "\u001f" + field.Property.PropertyId + "\u001f"
              + field.CurrentCanonicalValue + "\u001f"
              + field.ProposedCanonicalValue);
          return new NativeWorkflowItemEvidence
          {
            Identity = plan.Element.UniqueId + "|"
              + field.Property.ParameterGuid.ToString("D"),
            CurrentValue = field.ValueAction == NativeStage02ValueAction.Set
              ? field.ProposedCanonicalValue
              : field.CurrentCanonicalValue,
            Unit = field.Property.CanonicalUnit,
            Source = "STAGE02A_FIELD",
            WriteSucceeded = succeeded,
            ReadbackSucceeded = succeeded,
            InputHash = input,
            UpdatedUtc = updatedUtc,
            ErrorCode = error
          };
        })
        .ToArray();
    }

    private static NativeWorkflowItemEvidence BuildRoleOutcome(
      NativeStage02ElementPlan plan,
      bool elementSucceeded,
      string updatedUtc,
      string errorCode)
    {
      bool confirmed = plan.RoleConfirmation?.Confirmed == true;
      return new NativeWorkflowItemEvidence
      {
        Identity = plan.Element.UniqueId + "|ROLE_CONFIRMATION",
        CurrentValue = plan.EffectiveRoleId,
        Source = "STAGE02A_ROLE",
        WriteSucceeded = confirmed && elementSucceeded,
        ReadbackSucceeded = confirmed && elementSucceeded,
        InputHash = SnapshotHash(plan),
        UpdatedUtc = updatedUtc,
        ErrorCode = confirmed && elementSucceeded
          ? string.Empty
          : !string.IsNullOrWhiteSpace(errorCode)
            ? errorCode
            : plan.RoleConfirmation?.Code ?? "ROLE_CONFIRMATION_REQUIRED"
      };
    }

    private static string SnapshotHash(NativeStage02ElementPlan plan)
    {
      return string.IsNullOrWhiteSpace(plan.ElementSnapshotHash)
        ? NativeStage02ElementSnapshotCanonicalizer.Sha256(plan.Element)
        : plan.ElementSnapshotHash;
    }

    private static string HashOrFallback(string value, string fallback)
    {
      return !string.IsNullOrWhiteSpace(value) && value.Length == 64
        ? value
        : fallback;
    }

    private static NativeStage02WriteResult Failure(
      bool requiresNewPreview,
      params string[] messages)
    {
      return new NativeStage02WriteResult
      {
        Success = false,
        RequiresNewPreview = requiresNewPreview,
        Status = "Stage02 写入失败",
        Messages = messages ?? Array.Empty<string>()
      };
    }

    private sealed class BindingWork
    {
      private readonly HashSet<string> _categoryKeys = new HashSet<string>(
        StringComparer.Ordinal);

      internal BindingWork(NativeStage02PropertyDefinition property)
      {
        Property = property ?? throw new ArgumentNullException(nameof(property));
      }

      internal NativeStage02PropertyDefinition Property { get; }
      internal IReadOnlyList<string> CategoryKeys => _categoryKeys
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

      internal void AddCategory(string categoryKey)
      {
        if (!string.IsNullOrWhiteSpace(categoryKey)) _categoryKeys.Add(categoryKey);
      }
    }
  }
}
