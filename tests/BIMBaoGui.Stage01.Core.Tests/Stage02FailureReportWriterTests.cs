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

    private static void Delete(string path)
    {
      if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        File.Delete(path);
    }
  }
}
