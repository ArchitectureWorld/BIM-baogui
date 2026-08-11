using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Diagnostics;
using BIMBaoGui.Stage01.Revit;
using BIMBaoGui.Stage01.Stage02;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage02RevitOperationResultsTests
  {
    private const BindingFlags InternalStatic =
      BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags InternalInstance =
      BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void SelectionTechnicalFailurePreservesExceptionAndWritesTypedReport()
    {
      Type resultType = RequiredType(
        "BIMBaoGui.Stage01.Revit.Stage02RevitSelectionResult");
      var expected = new InvalidOperationException(
        "host read failed",
        new ApplicationException("revit api failed"));
      object result = InvokeFactory(
        resultType,
        "TechnicalFailure",
        "CURRENT_SELECTION",
        "Revit read failed.",
        expected);

      Assert.Equal(
        "TechnicalFailure",
        ReadProperty(result, "Disposition").ToString());
      Assert.Same(expected, ReadProperty(result, "Exception"));
      Assert.False((bool)ReadProperty(result, "Success"));

      object decision = InvokePolicy("ForSelection", result);
      Assert.True((bool)ReadProperty(decision, "ShouldWrite"));
      Assert.Equal(
        "STAGE02_SELECTION_SERVICE_EXCEPTION",
        ReadProperty(decision, "ErrorCode"));
      Assert.Equal("PREVIEW_SELECTION", ReadProperty(decision, "OperationStage"));
      Assert.Same(expected, ReadProperty(decision, "Exception"));

      Stage02FailureReportWriteResult report = WriteReport(decision);
      try
      {
        Assert.True(report.Success, report.ReportWriteErrorSummary);
        Assert.True(File.Exists(report.ReportPath));
        var serializer = new JavaScriptSerializer();
        var root = Assert.IsType<Dictionary<string, object>>(
          serializer.DeserializeObject(
            File.ReadAllText(report.ReportPath, Encoding.UTF8)));
        Assert.Equal(
          "STAGE02_SELECTION_SERVICE_EXCEPTION",
          root["errorCode"]);
        object[] chain = Assert.IsType<object[]>(root["exceptionChain"]);
        Assert.NotEmpty(chain);
        var first = Assert.IsType<Dictionary<string, object>>(chain[0]);
        Assert.Equal(expected.GetType().FullName, first["type"]);
      }
      finally
      {
        Delete(report.ReportPath);
      }
    }

    [Fact]
    public void SelectionNoResultUsesDistinctTypedReportWithDiagnosticException()
    {
      Stage02RevitFailureReportDecision decision =
        Stage02RevitFailureReportPolicy.ForSelection(null);

      Assert.True(decision.ShouldWrite);
      Assert.Equal("PREVIEW_SELECTION", decision.OperationStage);
      Assert.Equal("STAGE02_SELECTION_NO_RESULT", decision.ErrorCode);
      Assert.Equal(
        "Stage02 元素选择服务未返回结果。",
        decision.DiagnosticMessage);
      Assert.NotNull(decision.Exception);
      AssertReportHasExceptionChain(
        decision,
        decision.Exception.GetType());
    }

    [Theory]
    [InlineData("BusinessBlocked")]
    [InlineData("CancelledResult")]
    public void NonTechnicalSelectionOutcomesDoNotRequestReports(
      string factoryName)
    {
      Type resultType = RequiredType(
        "BIMBaoGui.Stage01.Revit.Stage02RevitSelectionResult");
      object result = string.Equals(
        factoryName,
        "BusinessBlocked",
        StringComparison.Ordinal)
        ? InvokeFactory(
          resultType,
          factoryName,
          "CURRENT_SELECTION",
          new[] { "No elements selected." })
        : InvokeFactory(resultType, factoryName, "EXPLICIT_PICK");

      object decision = InvokePolicy("ForSelection", result);

      Assert.False((bool)ReadProperty(decision, "ShouldWrite"));
      Assert.Null(ReadProperty(decision, "Exception"));
    }

    [Fact]
    public void PreviewContractExceptionIsBusinessBlockedWithoutReport()
    {
      Type resultType = RequiredType(
        "BIMBaoGui.Stage01.Revit.Stage02RevitPreviewResult");
      var expected = new Stage02ContractException(
        "BUSINESS_BLOCKER",
        "preview input is blocked");
      object result = InvokeFactory(resultType, "FromException", expected);

      Assert.Equal(
        "BusinessBlocked",
        ReadProperty(result, "Disposition").ToString());
      Assert.Null(ReadProperty(result, "Exception"));
      object decision = InvokePolicy("ForPreview", result);
      Assert.False((bool)ReadProperty(decision, "ShouldWrite"));
      Assert.Null(ReadProperty(decision, "Exception"));
    }

    [Fact]
    public void PreviewWithBusinessBlockersRetainsPreviewWithoutReportingSuccess()
    {
      Stage02Preview preview = Preview();
      var blocker = new Stage02Blocker(
        "READ_ONLY_PARAMETER",
        "Parameter cannot be written.");
      var blockers = new SingleUseEnumerable<Stage02Blocker>(blocker);

      var result = new Stage02RevitPreviewResult(preview, blockers);

      Assert.Equal(
        Stage02RevitPreviewDisposition.BusinessBlocked,
        result.Disposition);
      Assert.False(result.Success);
      Assert.Same(preview, result.Preview);
      Assert.Same(blocker, Assert.Single(result.Blockers));
      Assert.Equal(1, blockers.EnumerationCount);
      Stage02RevitFailureReportDecision decision =
        Stage02RevitFailureReportPolicy.ForPreview(result);
      Assert.False(decision.ShouldWrite);
      Assert.Null(decision.Exception);
    }

    [Theory]
    [InlineData(Stage02SelectionModes.CurrentSelection)]
    [InlineData(Stage02SelectionModes.ExplicitPick)]
    [InlineData(Stage02SelectionModes.ExplicitIds)]
    [InlineData(Stage02SelectionModes.ProjectInformation)]
    public void HostUnavailableAndActualExceptionMapConsistentlyAcrossReadPaths(
      string selectionMode)
    {
      const string message = "same host read failure text";
      Stage02RevitSelectionResult unavailableSelection =
        Stage02RevitHostFailurePolicy.ForSelection(
          selectionMode,
          message,
          null);
      Stage02RevitPreviewResult unavailablePreview =
        Stage02RevitHostFailurePolicy.ForPreview(message, null);

      Assert.Equal(
        Stage02RevitSelectionDisposition.BusinessBlocked,
        unavailableSelection.Disposition);
      Assert.Equal(
        Stage02RevitPreviewDisposition.BusinessBlocked,
        unavailablePreview.Disposition);
      Assert.Equal(selectionMode, unavailableSelection.SelectionMode);
      Assert.Null(unavailableSelection.Exception);
      Assert.Null(unavailablePreview.Exception);
      Assert.False(
        Stage02RevitFailureReportPolicy.ForSelection(unavailableSelection)
          .ShouldWrite);
      Assert.False(
        Stage02RevitFailureReportPolicy.ForPreview(unavailablePreview)
          .ShouldWrite);

      var actualException = new InvalidOperationException(message);
      Stage02RevitSelectionResult failedSelection =
        Stage02RevitHostFailurePolicy.ForSelection(
          selectionMode,
          message,
          actualException);
      Stage02RevitPreviewResult failedPreview =
        Stage02RevitHostFailurePolicy.ForPreview(message, actualException);

      Assert.Equal(
        Stage02RevitSelectionDisposition.TechnicalFailure,
        failedSelection.Disposition);
      Assert.Equal(
        Stage02RevitPreviewDisposition.TechnicalFailure,
        failedPreview.Disposition);
      Assert.Equal(selectionMode, failedSelection.SelectionMode);
      Assert.Same(actualException, failedSelection.Exception);
      Assert.Same(actualException, failedPreview.Exception);
      Assert.True(
        Stage02RevitFailureReportPolicy.ForSelection(failedSelection)
          .ShouldWrite);
      Assert.True(
        Stage02RevitFailureReportPolicy.ForPreview(failedPreview)
          .ShouldWrite);
    }

    [Fact]
    public void WriteEnqueueFailurePreservesThrowAndTypesFalseResult()
    {
      var thrown = new InvalidOperationException("enqueue callback failed");
      Stage02RevitWriteEnqueueFailureDecision thrownDecision =
        Stage02RevitWriteEnqueueFailurePolicy.ForFailure(
          "callback failed",
          thrown);

      Assert.Equal(
        "STAGE02_WRITE_ENQUEUE_EXCEPTION",
        thrownDecision.ErrorCode);
      Assert.Same(thrown, thrownDecision.Exception);
      Assert.Equal("WRITE_ENQUEUE", thrownDecision.OperationStage);

      Stage02RevitWriteEnqueueFailureDecision rejectedDecision =
        Stage02RevitWriteEnqueueFailurePolicy.ForFailure(
          "host queue rejected",
          null);

      Assert.Equal(
        "STAGE02_WRITE_ENQUEUE_REJECTED",
        rejectedDecision.ErrorCode);
      Assert.NotNull(rejectedDecision.Exception);
      Assert.IsType<InvalidOperationException>(rejectedDecision.Exception);
      Assert.Equal("WRITE_ENQUEUE", rejectedDecision.OperationStage);
      Assert.Equal(
        "Stage02 写入请求提交发生技术失败。",
        rejectedDecision.DiagnosticMessage);
    }

    [Fact]
    public void WriteHostCallbackFailureIsTypedAndPreservesOriginalException()
    {
      var expected = new InvalidOperationException(
        "host callback failed",
        new ApplicationException("revit callback infrastructure failed"));

      Stage02RevitWriteHostCallbackFailureDecision decision =
        Stage02RevitWriteHostCallbackFailurePolicy.ForFailure(expected);

      Assert.Equal("WRITE_HOST_CALLBACK", decision.OperationStage);
      Assert.Equal("WRITE_HOST_CALLBACK", decision.ErrorCode);
      Assert.Equal(
        "Stage02 写入宿主回调发生技术失败。",
        decision.DiagnosticMessage);
      Assert.Equal("Stage02 写入宿主回调失败。", decision.UserMessage);
      Assert.Same(expected, decision.Exception);
    }

    [Fact]
    public void PreviewInternalExceptionPreservesExceptionAndWritesTypedReport()
    {
      Type resultType = RequiredType(
        "BIMBaoGui.Stage01.Revit.Stage02RevitPreviewResult");
      var expected = new InvalidOperationException(
        "preview failed",
        new ApplicationException("revit api preview failed"));
      object result = InvokeFactory(resultType, "FromException", expected);

      Assert.Equal(
        "TechnicalFailure",
        ReadProperty(result, "Disposition").ToString());
      Assert.Same(expected, ReadProperty(result, "Exception"));
      object decision = InvokePolicy("ForPreview", result);
      Assert.True((bool)ReadProperty(decision, "ShouldWrite"));
      Assert.Equal(
        "STAGE02_PREVIEW_SERVICE_EXCEPTION",
        ReadProperty(decision, "ErrorCode"));
      Assert.Same(expected, ReadProperty(decision, "Exception"));

      AssertReportHasExceptionChain(decision, expected.GetType());
    }

    [Fact]
    public void PreviewNoResultUsesDistinctReportWithDiagnosticException()
    {
      Type resultType = RequiredType(
        "BIMBaoGui.Stage01.Revit.Stage02RevitPreviewResult");
      object result = InvokeFactory(resultType, "NoResult");
      Assert.Equal(
        "NoResult",
        ReadProperty(result, "Disposition").ToString());

      object resultDecision = InvokePolicy("ForPreview", result);
      Assert.True((bool)ReadProperty(resultDecision, "ShouldWrite"));
      Assert.Equal(
        "STAGE02_PREVIEW_NO_RESULT",
        ReadProperty(resultDecision, "ErrorCode"));
      Assert.NotNull(ReadProperty(resultDecision, "Exception"));

      object nullDecision = InvokePolicy("ForPreview", null);
      Assert.True((bool)ReadProperty(nullDecision, "ShouldWrite"));
      Assert.Equal(
        "STAGE02_PREVIEW_NO_RESULT",
        ReadProperty(nullDecision, "ErrorCode"));
      Exception diagnostic = Assert.IsAssignableFrom<Exception>(
        ReadProperty(nullDecision, "Exception"));
      AssertReportHasExceptionChain(nullDecision, diagnostic.GetType());
    }

    private static Type RequiredType(string fullName)
    {
      Type type = typeof(Stage02ContractException).Assembly.GetType(
        fullName,
        false);
      Assert.True(type != null, "Expected Core seam type: " + fullName);
      return type;
    }

    private static object InvokeFactory(
      Type resultType,
      string methodName,
      params object[] arguments)
    {
      MethodInfo method = resultType.GetMethod(
        methodName,
        InternalStatic);
      Assert.True(
        method != null,
        "Expected internal factory " + resultType.FullName + "." + methodName);
      return method.Invoke(null, arguments);
    }

    private static object InvokePolicy(string methodName, object result)
    {
      Type policyType = RequiredType(
        "BIMBaoGui.Stage01.Revit.Stage02RevitFailureReportPolicy");
      MethodInfo method = policyType.GetMethod(methodName, InternalStatic);
      Assert.True(method != null, "Expected report policy method " + methodName);
      return method.Invoke(null, new[] { result });
    }

    private static object ReadProperty(object instance, string propertyName)
    {
      Assert.NotNull(instance);
      PropertyInfo property = instance.GetType().GetProperty(
        propertyName,
        InternalInstance);
      Assert.True(
        property != null,
        "Expected internal property "
        + instance.GetType().FullName
        + "."
        + propertyName);
      return property.GetValue(instance, null);
    }

    private static Stage02FailureReportWriteResult WriteReport(object decision)
    {
      DateTimeOffset occurredUtc = DateTimeOffset.UtcNow;
      return Stage02FailureReportWriter.TryWrite(
        new Stage02FailureReportContext
        {
          DiagnosticCode = "DIAG_STAGE02_PREVIEW_FAILED",
          ErrorCode = (string)ReadProperty(decision, "ErrorCode"),
          DiagnosticMessage =
            (string)ReadProperty(decision, "DiagnosticMessage"),
          InputSignature = "typed-selection-input",
          FileGuid = "typed-selection-file",
          DocumentFingerprint = "typed-selection-host",
          DocumentTitle = "typed-selection.rvt",
          RulePackageId = "hbr-rulepack",
          RulePackageVersion = "1.0.0",
          RulePackageSha256 = "package-sha",
          PreviewHash = string.Empty,
          UniqueIds = Array.Empty<string>(),
          PropertyIds = Array.Empty<string>(),
          OperationStage = (string)ReadProperty(decision, "OperationStage"),
          RootCauseStage = (string)ReadProperty(decision, "OperationStage"),
          CleanupStage = string.Empty,
          TransactionStatus = "NOT_STARTED",
          TransactionGroupStatus = "NOT_STARTED",
          Exception = (Exception)ReadProperty(decision, "Exception"),
          OccurredUtc = occurredUtc,
          OccurredLocal = occurredUtc.ToLocalTime()
        });
    }

    private static void AssertReportHasExceptionChain(
      object decision,
      Type expectedExceptionType)
    {
      Stage02FailureReportWriteResult report = WriteReport(decision);
      try
      {
        Assert.True(report.Success, report.ReportWriteErrorSummary);
        Assert.True(File.Exists(report.ReportPath));
        var serializer = new JavaScriptSerializer();
        var root = Assert.IsType<Dictionary<string, object>>(
          serializer.DeserializeObject(
            File.ReadAllText(report.ReportPath, Encoding.UTF8)));
        object[] chain = Assert.IsType<object[]>(root["exceptionChain"]);
        Assert.NotEmpty(chain);
        var first = Assert.IsType<Dictionary<string, object>>(chain[0]);
        Assert.Equal(expectedExceptionType.FullName, first["type"]);
      }
      finally
      {
        Delete(report.ReportPath);
      }
    }

    private static Stage02Preview Preview()
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
        Array.Empty<Stage02MatchedElement>());
      return new Stage02Preview(
        request,
        Array.Empty<Stage02MatchedElement>(),
        "canonical-payload",
        "preview-hash");
    }

    private sealed class SingleUseEnumerable<T> : IEnumerable<T>
    {
      private readonly T _item;

      internal SingleUseEnumerable(T item)
      {
        _item = item;
      }

      internal int EnumerationCount { get; private set; }

      public IEnumerator<T> GetEnumerator()
      {
        EnumerationCount++;
        if (EnumerationCount > 1)
          throw new InvalidOperationException(
            "The enumerable was consumed more than once.");
        yield return _item;
      }

      IEnumerator IEnumerable.GetEnumerator()
      {
        return GetEnumerator();
      }
    }

    private static void Delete(string path)
    {
      if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        File.Delete(path);
    }
  }
}
