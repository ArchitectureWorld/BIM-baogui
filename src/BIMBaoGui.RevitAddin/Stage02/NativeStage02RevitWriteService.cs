using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.RevitAddin.Rules;

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

      var messages = new List<string>();
      var failedPropertyIds = new HashSet<string>(StringComparer.Ordinal);
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
        if (elementPlan.IsBlocked) continue;
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
        if (!changeAssignment && writes.Length == 0) continue;
        if (requiredBindings.Any(value => failedPropertyIds.Contains(
          value.Property.PropertyId)))
        {
          failedElements++;
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
          }
          catch (Exception exception)
          {
            if (started) transaction.RollBack();
            failedElements++;
            if (changeAssignment) failedAssignments++;
            messages.Add(
              "构件写入失败｜Id="
              + elementPlan.Element.ElementId.ToString(CultureInfo.InvariantCulture)
              + "｜" + exception.Message);
          }
        }
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
