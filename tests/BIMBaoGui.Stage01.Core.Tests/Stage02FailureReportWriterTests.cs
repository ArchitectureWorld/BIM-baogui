using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Diagnostics;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage02FailureReportWriterTests
  {
    [Fact]
    public void WritesUtf8WithoutBomBesideCurrentAssemblyAndNoBusinessValues()
    {
      Stage02FailureReportWriteResult result =
        Stage02FailureReportWriter.TryWrite(Context());

      try
      {
        Assert.True(result.Success, result.ReportWriteErrorSummary);
        Assert.Equal(
          Path.GetDirectoryName(typeof(Stage02FailureReportWriter).Assembly.Location),
          Path.GetDirectoryName(result.ReportPath));
        Assert.StartsWith(
          "BIMBaoGui.Stage02.failure-",
          Path.GetFileName(result.ReportPath),
          StringComparison.Ordinal);
        byte[] bytes = File.ReadAllBytes(result.ReportPath);
        Assert.False(bytes.Length >= 3
          && bytes[0] == 0xEF
          && bytes[1] == 0xBB
          && bytes[2] == 0xBF);
        string json = new UTF8Encoding(false).GetString(bytes);
        Assert.Contains("preview-hash", json, StringComparison.Ordinal);
        Assert.Contains("unique-id", json, StringComparison.Ordinal);
        Assert.Contains("property-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-business-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("canonical-payload-secret", json, StringComparison.Ordinal);
      }
      finally
      {
        Delete(result.ReportPath);
      }
    }

    [Fact]
    public async Task ConcurrentReportsAllocateUniqueAtomicJsonPaths()
    {
      Stage02FailureReportWriteResult[] results = await Task.WhenAll(
        Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
          Stage02FailureReportWriter.TryWrite(Context()))));
      try
      {
        Assert.All(results, result => Assert.True(
          result.Success,
          result.ReportWriteErrorSummary));
        Assert.Equal(
          results.Length,
          results.Select(result => result.ReportPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count());
        Assert.All(results, result => Assert.Equal(
          ".json",
          Path.GetExtension(result.ReportPath)));
      }
      finally
      {
        foreach (Stage02FailureReportWriteResult result in results)
          Delete(result.ReportPath);
      }
    }

    [Fact]
    public void Directory_name_collision_retries_without_FileExists_probe()
    {
      uint offset = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
      DateTimeOffset occurredLocal = new DateTimeOffset(
        2100,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero).AddMilliseconds(offset);
      string directory = Path.GetDirectoryName(
        typeof(Stage02FailureReportWriter).Assembly.Location);
      string occupiedPath = Path.Combine(
        directory,
        "BIMBaoGui.Stage02.failure-"
          + occurredLocal.ToString(
            "yyyyMMdd-HHmmss-fff",
            System.Globalization.CultureInfo.InvariantCulture)
          + ".json");
      Stage02FailureReportWriteResult result = null;

      try
      {
        Directory.CreateDirectory(occupiedPath);
        Stage02FailureReportContext context = Context();
        context.OccurredLocal = occurredLocal;

        result = Stage02FailureReportWriter.TryWrite(context);

        Assert.True(result.Success, result.ReportWriteErrorSummary);
        Assert.True(Directory.Exists(occupiedPath));
        Assert.NotEqual(occupiedPath, result.ReportPath);
        Assert.True(File.Exists(result.ReportPath));
      }
      finally
      {
        Delete(result?.ReportPath);
        if (Directory.Exists(occupiedPath)) Directory.Delete(occupiedPath);
      }
    }

    [Fact]
    public void AggregateException_reports_every_flattened_inner_type_without_messages()
    {
      Stage02FailureReportContext context = Context();
      context.Exception = new AggregateException(
        new InvalidOperationException("primary-secret-marker"),
        new AggregateException(
          new IOException("delete-secret-marker"),
          new UnauthorizedAccessException("restore-secret-marker")));
      Stage02FailureReportWriteResult result =
        Stage02FailureReportWriter.TryWrite(context);

      try
      {
        Assert.True(result.Success, result.ReportWriteErrorSummary);
        string json = File.ReadAllText(result.ReportPath, Encoding.UTF8);
        Assert.Contains(
          typeof(InvalidOperationException).FullName,
          json,
          StringComparison.Ordinal);
        Assert.Contains(
          typeof(IOException).FullName,
          json,
          StringComparison.Ordinal);
        Assert.Contains(
          typeof(UnauthorizedAccessException).FullName,
          json,
          StringComparison.Ordinal);
        foreach (string marker in new[]
        {
          "primary-secret-marker",
          "delete-secret-marker",
          "restore-secret-marker"
        })
        {
          Assert.DoesNotContain(marker, json, StringComparison.Ordinal);
          Assert.DoesNotContain(
            marker,
            result.OriginalExceptionSummary,
            StringComparison.Ordinal);
        }
      }
      finally
      {
        Delete(result.ReportPath);
      }
    }

    [Fact]
    public void AggregateException_sibling_inners_share_parent_plus_one_depth()
    {
      Stage02FailureReportContext context = Context();
      context.Exception = new AggregateException(
        new IOException("business-marker-a"),
        new UnauthorizedAccessException("business-marker-b"));
      Stage02FailureReportWriteResult result =
        Stage02FailureReportWriter.TryWrite(context);

      try
      {
        Assert.True(result.Success, result.ReportWriteErrorSummary);
        var serializer = new JavaScriptSerializer();
        var root = Assert.IsType<Dictionary<string, object>>(
          serializer.DeserializeObject(
            File.ReadAllText(result.ReportPath, Encoding.UTF8)));
        Dictionary<string, int> depths =
          Assert.IsType<object[]>(root["exceptionChain"])
            .Cast<Dictionary<string, object>>()
            .ToDictionary(
              entry => Assert.IsType<string>(entry["type"]),
              entry => Assert.IsType<int>(entry["depth"]));

        Assert.Equal(0, depths[typeof(AggregateException).FullName]);
        Assert.Equal(1, depths[typeof(IOException).FullName]);
        Assert.Equal(
          1,
          depths[typeof(UnauthorizedAccessException).FullName]);
      }
      finally
      {
        Delete(result.ReportPath);
      }
    }

    [Fact]
    public void Nested_aggregate_reports_every_sibling_inner_type()
    {
      Stage02FailureReportContext context = Context();
      context.Exception = new InvalidOperationException(
        "wrapper-secret-marker",
        new AggregateException(
          new IOException("first-secret-marker"),
          new UnauthorizedAccessException("second-secret-marker")));
      Stage02FailureReportWriteResult result =
        Stage02FailureReportWriter.TryWrite(context);

      try
      {
        Assert.True(result.Success, result.ReportWriteErrorSummary);
        string json = File.ReadAllText(result.ReportPath, Encoding.UTF8);
        Assert.Contains(typeof(IOException).FullName, json);
        Assert.Contains(typeof(UnauthorizedAccessException).FullName, json);
        Assert.DoesNotContain("wrapper-secret-marker", json);
        Assert.DoesNotContain("first-secret-marker", json);
        Assert.DoesNotContain("second-secret-marker", json);
      }
      finally
      {
        Delete(result.ReportPath);
      }
    }

    [Fact]
    public void Fatal_unknown_status_records_unconfirmed_rollback_and_statuses()
    {
      Stage02FailureReportContext context = Context();
      context.RollbackConfirmed = false;
      context.TransactionStatus = "Error";
      context.TransactionGroupStatus = "Started";
      Stage02FailureReportWriteResult result =
        Stage02FailureReportWriter.TryWrite(context);

      try
      {
        Assert.True(result.Success, result.ReportWriteErrorSummary);
        string json = File.ReadAllText(result.ReportPath, Encoding.UTF8);
        Assert.Contains("\"rollbackConfirmed\": false", json);
        Assert.Contains("\"transactionStatus\": \"Error\"", json);
        Assert.Contains(
          "\"transactionGroupStatus\": \"Started\"",
          json);
      }
      finally
      {
        Delete(result.ReportPath);
      }
    }

    [Fact]
    public void Terminal_handoff_conflict_records_both_observed_statuses()
    {
      Stage02FailureReportContext context = Context();
      context.TransactionRolledBack = false;
      context.TransactionStatus = "CONFLICT";
      context.HandoffFinalizerTerminalStatus = "Committed";
      context.HandoffEndCallTerminalStatus = "RolledBack";
      Stage02FailureReportWriteResult result =
        Stage02FailureReportWriter.TryWrite(context);

      try
      {
        Assert.True(result.Success, result.ReportWriteErrorSummary);
        string json = File.ReadAllText(result.ReportPath, Encoding.UTF8);
        Assert.Contains(
          "\"handoffFinalizerTerminalStatus\": \"Committed\"",
          json);
        Assert.Contains(
          "\"handoffEndCallTerminalStatus\": \"RolledBack\"",
          json);
        Assert.Contains("\"transactionStatus\": \"CONFLICT\"", json);
        Assert.Contains("\"transactionRolledBack\": false", json);
      }
      finally
      {
        Delete(result.ReportPath);
      }
    }

    [Fact]
    public void Group_only_rollback_does_not_claim_transaction_rollback()
    {
      Stage02FailureReportContext context = Context();
      context.TransactionRolledBack = false;
      context.GroupRolledBack = true;
      context.RollbackConfirmed = true;
      Stage02FailureReportWriteResult result =
        Stage02FailureReportWriter.TryWrite(context);

      try
      {
        Assert.True(result.Success, result.ReportWriteErrorSummary);
        string json = File.ReadAllText(result.ReportPath, Encoding.UTF8);
        Assert.Contains("\"transactionRolledBack\": false", json);
        Assert.Contains("\"groupRolledBack\": true", json);
      }
      finally
      {
        Delete(result.ReportPath);
      }
    }

    [Fact]
    public void Root_cause_and_cleanup_stages_are_reported_without_overwrite()
    {
      Stage02FailureReportContext context = Context();
      context.OperationStage = "TRANSACTION_GROUP_ROLLBACK";
      var rootCauseProperty = typeof(Stage02FailureReportContext)
        .GetProperty("RootCauseStage");
      var cleanupProperty = typeof(Stage02FailureReportContext)
        .GetProperty("CleanupStage");

      Assert.NotNull(rootCauseProperty);
      Assert.NotNull(cleanupProperty);
      rootCauseProperty.SetValue(context, "WRITE_VALUES");
      cleanupProperty.SetValue(context, "TRANSACTION_GROUP_ROLLBACK");
      Stage02FailureReportWriteResult result =
        Stage02FailureReportWriter.TryWrite(context);

      try
      {
        Assert.True(result.Success, result.ReportWriteErrorSummary);
        string json = File.ReadAllText(result.ReportPath, Encoding.UTF8);
        Assert.Contains("\"operationStage\": \"WRITE_VALUES\"", json);
        Assert.Contains("\"rootCauseStage\": \"WRITE_VALUES\"", json);
        Assert.Contains(
          "\"cleanupStage\": \"TRANSACTION_GROUP_ROLLBACK\"",
          json);
      }
      finally
      {
        Delete(result.ReportPath);
      }
    }

    [Fact]
    public void Preview_failure_records_current_input_identity_and_controlled_diagnostics()
    {
      Stage02FailureReportContext context = Context();
      SetRequiredProperty(
        context,
        "DiagnosticCode",
        "DIAG_STAGE02_PREVIEW_FAILED");
      SetRequiredProperty(
        context,
        "ErrorCode",
        "STAGE02_PREVIEW_SERVICE_EXCEPTION");
      SetRequiredProperty(
        context,
        "DiagnosticMessage",
        "Stage02 预览构建发生技术失败。");
      SetRequiredProperty(context, "InputSignature", "input-signature");
      context.OperationStage = "PREVIEW_BUILD";
      context.RootCauseStage = "PREVIEW_BUILD";
      context.Exception = new InvalidOperationException(
        "hidden-business-value-must-not-leak");
      Stage02FailureReportWriteResult result =
        Stage02FailureReportWriter.TryWrite(context);

      try
      {
        Assert.True(result.Success, result.ReportWriteErrorSummary);
        var serializer = new JavaScriptSerializer();
        var root = Assert.IsType<Dictionary<string, object>>(
          serializer.DeserializeObject(
            File.ReadAllText(result.ReportPath, Encoding.UTF8)));

        Assert.Equal("STAGE02", root["stage"]);
        Assert.Equal(
          "DIAG_STAGE02_PREVIEW_FAILED",
          root["diagnosticCode"]);
        Assert.Equal(
          "STAGE02_PREVIEW_SERVICE_EXCEPTION",
          root["errorCode"]);
        Assert.Equal(
          "Stage02 预览构建发生技术失败。",
          root["message"]);
        Assert.Equal("input-signature", root["inputSignature"]);
        Assert.Equal("PREVIEW_BUILD", root["operationStage"]);
        string json = File.ReadAllText(result.ReportPath, Encoding.UTF8);
        Assert.DoesNotContain(
          "hidden-business-value-must-not-leak",
          json,
          StringComparison.Ordinal);
      }
      finally
      {
        Delete(result.ReportPath);
      }
    }

    [Fact]
    public void Write_failure_records_attempt_and_publication_identity()
    {
      Guid attemptToken = Guid.Parse("8b5e13c4-bf2d-4d17-9944-c1a7e55f90d2");
      Stage02FailureReportContext context = Context();
      SetRequiredProperty(context, "InputSignature", "write-input-signature");
      SetRequiredProperty(context, "AttemptToken", attemptToken);
      SetRequiredProperty(
        context,
        "PublicationDisposition",
        "PUBLISHED_CURRENT");
      Stage02FailureReportWriteResult result =
        Stage02FailureReportWriter.TryWrite(context);

      try
      {
        Assert.True(result.Success, result.ReportWriteErrorSummary);
        var serializer = new JavaScriptSerializer();
        var root = Assert.IsType<Dictionary<string, object>>(
          serializer.DeserializeObject(
            File.ReadAllText(result.ReportPath, Encoding.UTF8)));

        Assert.Equal("write-input-signature", root["inputSignature"]);
        Assert.Equal(attemptToken.ToString("D"), root["attemptToken"]);
        Assert.Equal("preview-hash", root["previewHash"]);
        Assert.Equal(
          "PUBLISHED_CURRENT",
          root["publicationDisposition"]);
      }
      finally
      {
        Delete(result.ReportPath);
      }
    }

    [Fact]
    public void Host_callback_failure_code_and_message_are_publishable()
    {
      Stage02FailureReportContext context = Context();
      var expected = new InvalidOperationException("host callback failed");
      context.DiagnosticCode = "DIAG_STAGE02_WRITE_FAILED";
      context.ErrorCode = "WRITE_HOST_CALLBACK";
      context.DiagnosticMessage =
        "Stage02 写入宿主回调发生技术失败。";
      context.InputSignature = "host-callback-input";
      context.AttemptToken = Guid.Parse(
        "68fa5b36-9ee3-40e5-902f-9c0f5243cb5f");
      context.OperationStage = "WRITE_HOST_CALLBACK";
      context.RootCauseStage = "WRITE_HOST_CALLBACK";
      context.TransactionRolledBack = false;
      context.GroupRolledBack = false;
      context.RollbackConfirmed = false;
      context.TransactionStatus = "NOT_STARTED";
      context.TransactionGroupStatus = "NOT_STARTED";
      context.Exception = expected;
      Stage02FailureReportWriteResult result =
        Stage02FailureReportWriter.TryWrite(context);

      try
      {
        Assert.True(result.Success, result.ReportWriteErrorSummary);
        Dictionary<string, object> root = ReadRoot(result.ReportPath);
        Assert.Equal("WRITE_HOST_CALLBACK", root["errorCode"]);
        Assert.Equal("WRITE_HOST_CALLBACK", root["operationStage"]);
        Assert.Equal(
          "Stage02 写入宿主回调发生技术失败。",
          root["message"]);
        object[] chain = Assert.IsType<object[]>(root["exceptionChain"]);
        var first = Assert.IsType<Dictionary<string, object>>(chain[0]);
        Assert.Equal(expected.GetType().FullName, first["type"]);
      }
      finally
      {
        Delete(result.ReportPath);
      }
    }

    [Fact]
    public void Completion_consumer_failure_identity_is_publishable_without_allowing_unknown_variants()
    {
      const string diagnosticCode = "DIAG_STAGE02_WRITE_FAILED";
      const string errorCode =
        "STAGE02_WRITE_COMPLETION_CONSUMER_FAILED";
      const string message =
        "Stage02 写入结果完成消费者发生技术失败；业务完成未重试。";
      Stage02FailureReportWriteResult published = null;
      Stage02FailureReportWriteResult unknownCode = null;
      Stage02FailureReportWriteResult unknownMessage = null;

      try
      {
        Stage02FailureReportContext context = Context();
        context.DiagnosticCode = diagnosticCode;
        context.ErrorCode = errorCode;
        context.DiagnosticMessage = message;
        context.InputSignature = "completion-consumer-input";
        context.AttemptToken = Guid.Parse(
          "fb591a70-e93b-46ca-8a71-5818a892fd87");
        context.OperationStage = "WRITE_COMPLETION_CONSUMER";
        context.RootCauseStage = "WRITE_COMPLETION_CONSUMER";

        published = Stage02FailureReportWriter.TryWrite(context);

        Assert.True(published.Success, published.ReportWriteErrorSummary);
        Assert.True(File.Exists(published.ReportPath));
        Assert.Equal(".json", Path.GetExtension(published.ReportPath));
        Dictionary<string, object> root = ReadRoot(published.ReportPath);
        Assert.Equal(diagnosticCode, root["diagnosticCode"]);
        Assert.Equal(errorCode, root["errorCode"]);
        Assert.Equal(message, root["message"]);
        Assert.Equal(
          "WRITE_COMPLETION_CONSUMER",
          root["operationStage"]);

        Stage02FailureReportContext unknownCodeContext = Context();
        unknownCodeContext.DiagnosticCode = diagnosticCode;
        unknownCodeContext.ErrorCode = errorCode + "_UNKNOWN";
        unknownCodeContext.DiagnosticMessage = message;
        unknownCode = Stage02FailureReportWriter.TryWrite(
          unknownCodeContext);

        Assert.False(unknownCode.Success);
        Assert.Equal("REPORT_WRITE_FAILED", unknownCode.ErrorCode);
        Assert.True(string.IsNullOrEmpty(unknownCode.ReportPath));

        Stage02FailureReportContext unknownMessageContext = Context();
        unknownMessageContext.DiagnosticCode = diagnosticCode;
        unknownMessageContext.ErrorCode = errorCode;
        unknownMessageContext.DiagnosticMessage = message + "未知";
        unknownMessage = Stage02FailureReportWriter.TryWrite(
          unknownMessageContext);

        Assert.False(unknownMessage.Success);
        Assert.Equal("REPORT_WRITE_FAILED", unknownMessage.ErrorCode);
        Assert.True(string.IsNullOrEmpty(unknownMessage.ReportPath));
      }
      finally
      {
        Delete(published?.ReportPath);
        Delete(unknownCode?.ReportPath);
        Delete(unknownMessage?.ReportPath);
      }
    }

    [Fact]
    public void Failure_draft_finalization_marks_current_and_stale_and_ignores_duplicates()
    {
      Stage02FailureReportPublicationResult current = null;
      Stage02FailureReportPublicationResult stale = null;
      try
      {
        string[] sourceUniqueIds = { "frozen-current-id" };
        Stage02FailureReportContext currentContext = Context();
        currentContext.InputSignature = "current-input-signature";
        currentContext.AttemptToken = Guid.Parse(
          "4910156d-373d-4bc8-a452-5d67d0b37c92");
        currentContext.UniqueIds = sourceUniqueIds;
        Stage02FailureReportDraft currentDraft =
          Stage02FailureReportDraft.Capture(currentContext);

        currentContext.InputSignature = "mutated-signature";
        sourceUniqueIds[0] = "mutated-id";
        current = Stage02FailureReportFinalizer.TryPublish(
          currentDraft,
          Stage02FailureReportPublicationDisposition.PublishedCurrent);

        Stage02FailureReportContext staleContext = Context();
        staleContext.InputSignature = "stale-input-signature";
        staleContext.AttemptToken = Guid.Parse(
          "8d8acc18-0790-4ee0-a066-911e66308e14");
        Stage02FailureReportDraft staleDraft =
          Stage02FailureReportDraft.Capture(staleContext);
        stale = Stage02FailureReportFinalizer.TryPublish(
          staleDraft,
          Stage02FailureReportPublicationDisposition.DiscardedStale);
        Stage02FailureReportPublicationResult ignored =
          Stage02FailureReportFinalizer.TryPublish(
            staleDraft,
            Stage02FailureReportPublicationDisposition.IgnoredDuplicate);

        Assert.True(current.WasWritten);
        Assert.True(current.ShouldPublishCurrent);
        Assert.True(current.WriteResult.Success);
        Assert.True(stale.WasWritten);
        Assert.False(stale.ShouldPublishCurrent);
        Assert.True(stale.WriteResult.Success);
        Assert.False(ignored.WasWritten);
        Assert.False(ignored.ShouldPublishCurrent);
        Assert.Equal(string.Empty, ignored.ReportPath);

        Dictionary<string, object> currentRoot = ReadRoot(current.ReportPath);
        Assert.Equal(
          "current-input-signature",
          currentRoot["inputSignature"]);
        Assert.Equal(
          "4910156d-373d-4bc8-a452-5d67d0b37c92",
          currentRoot["attemptToken"]);
        Assert.Equal(
          "PUBLISHED_CURRENT",
          currentRoot["publicationDisposition"]);
        Assert.Contains(
          "frozen-current-id",
          Assert.IsType<object[]>(currentRoot["uniqueIds"]));
        Assert.DoesNotContain(
          "mutated-id",
          Assert.IsType<object[]>(currentRoot["uniqueIds"]));

        Dictionary<string, object> staleRoot = ReadRoot(stale.ReportPath);
        Assert.Equal(
          "stale-input-signature",
          staleRoot["inputSignature"]);
        Assert.Equal(
          "8d8acc18-0790-4ee0-a066-911e66308e14",
          staleRoot["attemptToken"]);
        Assert.Equal(
          "DISCARDED_STALE",
          staleRoot["publicationDisposition"]);
        Assert.NotEqual(current.ReportPath, stale.ReportPath);
        Assert.True(File.Exists(current.ReportPath));
        Assert.True(File.Exists(stale.ReportPath));
        Assert.Equal(".json", Path.GetExtension(current.ReportPath));
        Assert.Equal(".json", Path.GetExtension(stale.ReportPath));
      }
      finally
      {
        Delete(current?.ReportPath);
        Delete(stale?.ReportPath);
      }
    }

    [Fact]
    public void Existing_write_failure_context_keeps_write_diagnostic_defaults()
    {
      Stage02FailureReportWriteResult result =
        Stage02FailureReportWriter.TryWrite(Context());

      try
      {
        Assert.True(result.Success, result.ReportWriteErrorSummary);
        string json = File.ReadAllText(result.ReportPath, Encoding.UTF8);
        Assert.Contains(
          "\"diagnosticCode\": \"DIAG_STAGE02_WRITE_FAILED\"",
          json,
          StringComparison.Ordinal);
        Assert.Contains(
          "\"operationStage\": \"WRITE_VALUES\"",
          json,
          StringComparison.Ordinal);
      }
      finally
      {
        Delete(result.ReportPath);
      }
    }

    private static void SetRequiredProperty(
      Stage02FailureReportContext context,
      string propertyName,
      object value)
    {
      var property = typeof(Stage02FailureReportContext)
        .GetProperty(propertyName);
      Assert.NotNull(property);
      property.SetValue(context, value);
    }

    private static Stage02FailureReportContext Context()
    {
      return new Stage02FailureReportContext
      {
        FileGuid = "file-guid",
        DocumentFingerprint = "document-fingerprint",
        DocumentTitle = "test-model.rvt",
        RulePackageId = "hbr-rulepack",
        RulePackageVersion = "1.0.0",
        RulePackageSha256 = "package-sha",
        PreviewHash = "preview-hash",
        UniqueIds = new[] { "unique-id" },
        PropertyIds = new[] { "property-id" },
        OperationStage = "WRITE_VALUES",
        TransactionRolledBack = true,
        GroupRolledBack = true,
        RollbackConfirmed = true,
        TransactionStatus = "RolledBack",
        TransactionGroupStatus = "RolledBack",
        Exception = new InvalidOperationException(
          "secret-business-value canonical-payload-secret"),
        OccurredUtc = DateTimeOffset.UtcNow,
        OccurredLocal = DateTimeOffset.Now
      };
    }

    private static Dictionary<string, object> ReadRoot(string path)
    {
      var serializer = new JavaScriptSerializer();
      return Assert.IsType<Dictionary<string, object>>(
        serializer.DeserializeObject(File.ReadAllText(path, Encoding.UTF8)));
    }

    private static void Delete(string path)
    {
      if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        File.Delete(path);
    }
  }
}
