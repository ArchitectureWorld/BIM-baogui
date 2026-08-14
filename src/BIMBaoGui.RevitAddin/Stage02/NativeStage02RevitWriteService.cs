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
    internal IReadOnlyList<string> Messages { get; set; } =
      Array.Empty<string>();
    internal NativeStage02Preview RefreshedPreview { get; set; }
    internal NativeStage02PreviewRequest ResolvedRequest { get; set; }
  }

  internal static class NativeStage02RevitWriteService
  {
    internal static NativeStage02WriteResult Execute(
      UIApplication uiApplication,
      NativeStage02WriteRequest request)
    {
      if (uiApplication == null)
        throw new ArgumentNullException(nameof(uiApplication));
      if (request?.Preview == null || request.ResolvedRequest == null)
        return Failure(true, "写入请求缺少已确认的 Stage02 预览。" );
      Document document = uiApplication.ActiveUIDocument?.Document;
      if (document == null)
        return Failure(true, "当前没有活动 Revit 项目文档。" );

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
              "参数准备失败｜"
              + work.Property.IfcEntity
              + " / "
              + work.Property.IfcPropertySet
              + " / "
              + work.Property.IfcProperty
              + "｜"
              + exception.Message);
            continue;
          }
        }
      }

      int writtenElements = 0;
      int failedElements = 0;
      foreach (NativeStage02ElementPlan elementPlan in rebuilt.Preview.Elements
        .OrderBy(value => value.Element.UniqueId, StringComparer.Ordinal))
      {
        if (elementPlan.IsBlocked) continue;
        NativeStage02FieldPlan[] writes = elementPlan.Fields
          .Where(value => value.ValueAction == NativeStage02ValueAction.Set)
          .ToArray();
        if (writes.Length == 0) continue;
        if (writes.Any(value => failedPropertyIds.Contains(
          value.Property.PropertyId)))
        {
          failedElements++;
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
          messages.Add(
            "构件跳过｜UniqueId="
            + elementPlan.Element.UniqueId
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
            if (transaction.Start() != TransactionStatus.Started)
              throw new InvalidOperationException("无法启动构件写入事务。" );
            started = true;
            foreach (NativeStage02FieldPlan field in writes)
            {
              Element target = NativeStage02RevitService.ResolveTarget(
                document,
                live,
                field.Property.BindingScope);
              Parameter parameter = target.get_Parameter(
                field.Property.ParameterGuid)
                ?? throw new InvalidOperationException(
                  "绑定后仍无法取得固定 GUID 参数："
                  + field.Property.ParameterName);
              NativeStage02ValueCodec.WriteAndVerify(
                parameter,
                field.Property,
                field.ProposedCanonicalValue);
            }
            document.Regenerate();
            if (transaction.Commit() != TransactionStatus.Committed)
              throw new InvalidOperationException("构件写入事务未提交。" );
            started = false;
            writtenElements++;
          }
          catch (Exception exception)
          {
            if (started) transaction.RollBack();
            failedElements++;
            messages.Add(
              "构件写入失败｜Id="
              + elementPlan.Element.ElementId.ToString(CultureInfo.InvariantCulture)
              + "｜"
              + exception.Message);
            continue;
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
          "参数准备="
          + preparedParameters.ToString(CultureInfo.InvariantCulture)
          + "，构件写入="
          + writtenElements.ToString(CultureInfo.InvariantCulture)
          + "。" );
      }
      return new NativeStage02WriteResult
      {
        Success = !hasFailures,
        PartialSuccess = hasFailures && hasSuccess,
        RequiresNewPreview = !refreshed.Success,
        Status = !hasFailures
          ? "Stage02 写入完成"
          : hasSuccess
            ? "Stage02 部分成功"
            : "Stage02 写入失败",
        PreparedParameterCount = preparedParameters,
        WrittenElementCount = writtenElements,
        FailedParameterCount = failedParameters,
        FailedElementCount = failedElements,
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
            && field.BindingAction
              != NativeStage02BindingAction.MergeCategories)
            continue;
          if (!byProperty.TryGetValue(
            field.Property.PropertyId,
            out BindingWork work))
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
      private readonly HashSet<string> _categoryKeys =
        new HashSet<string>(StringComparer.Ordinal);

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
        if (!string.IsNullOrWhiteSpace(categoryKey))
          _categoryKeys.Add(categoryKey);
      }
    }
  }
}
