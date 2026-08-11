using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal sealed class NativeStage01FailureReportContext
  {
    internal string ProductVersion { get; set; } = string.Empty;
    internal string RevitVersion { get; set; } = string.Empty;
    internal string DocumentTitle { get; set; } = string.Empty;
    internal string DocumentPath { get; set; } = string.Empty;
    internal string FileGuid { get; set; } = string.Empty;
    internal string PayloadHash { get; set; } = string.Empty;
    internal string RulePackageId { get; set; } = string.Empty;
    internal string RulePackageVersion { get; set; } = string.Empty;
    internal string RulePackageSha256 { get; set; } = string.Empty;
    internal string OperationStage { get; set; } = string.Empty;
    internal bool TransactionRolledBack { get; set; }
    internal Exception Exception { get; set; }
    internal DateTimeOffset OccurredUtc { get; set; }
  }

  internal sealed class NativeStage01FailureReportResult
  {
    internal bool Success { get; set; }
    internal string ReportPath { get; set; } = string.Empty;
    internal string Error { get; set; } = string.Empty;
  }

  internal static class NativeStage01FailureReportWriter
  {
    internal static NativeStage01FailureReportResult TryWrite(
      NativeStage01FailureReportContext context)
    {
      if (context == null) throw new ArgumentNullException(nameof(context));
      string temporaryPath = string.Empty;
      try
      {
        string directory = Path.Combine(
          Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
          "BIMBaoGui",
          "RevitAddin",
          "Diagnostics");
        Directory.CreateDirectory(directory);
        string stamp = context.OccurredUtc.ToString(
          "yyyyMMdd-HHmmss-fff",
          CultureInfo.InvariantCulture);
        string suffix = Guid.NewGuid().ToString("N");
        string finalPath = Path.Combine(
          directory,
          "BIMBaoGui.RevitAddin.Stage01.failure-"
            + stamp + "-" + suffix + ".json");
        temporaryPath = finalPath + ".tmp";
        string json = Serialize(context);
        byte[] bytes = new UTF8Encoding(false).GetBytes(json);
        using (var stream = new FileStream(
          temporaryPath,
          FileMode.CreateNew,
          FileAccess.Write,
          FileShare.None))
        {
          stream.Write(bytes, 0, bytes.Length);
          stream.Flush(true);
        }
        File.Move(temporaryPath, finalPath);
        temporaryPath = string.Empty;
        return new NativeStage01FailureReportResult
        {
          Success = true,
          ReportPath = finalPath
        };
      }
      catch (Exception exception)
      {
        return new NativeStage01FailureReportResult
        {
          Success = false,
          Error = exception.GetType().FullName + "：" + exception.Message
        };
      }
      finally
      {
        if (!string.IsNullOrWhiteSpace(temporaryPath))
        {
          try
          {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
          }
          catch
          {
          }
        }
      }
    }

    private static string Serialize(NativeStage01FailureReportContext context)
    {
      Exception exception = context.Exception;
      var payload = new Dictionary<string, object>(StringComparer.Ordinal)
      {
        { "schemaVersion", "1.0.0" },
        { "diagnosticCode", "DIAG_NATIVE_STAGE01_COMMIT_FAILED" },
        { "occurredUtc", context.OccurredUtc.ToString("O", CultureInfo.InvariantCulture) },
        { "productVersion", context.ProductVersion },
        { "revitVersion", context.RevitVersion },
        { "documentTitle", context.DocumentTitle },
        { "documentPath", context.DocumentPath },
        { "fileGuid", context.FileGuid },
        { "payloadHash", context.PayloadHash },
        { "rulePackageId", context.RulePackageId },
        { "rulePackageVersion", context.RulePackageVersion },
        { "rulePackageSha256", context.RulePackageSha256 },
        { "operationStage", context.OperationStage },
        { "transactionRolledBack", context.TransactionRolledBack },
        { "exceptionType", exception == null ? string.Empty : exception.GetType().FullName },
        { "exceptionMessage", exception == null ? string.Empty : exception.Message }
      };
      var serializer = new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 128
      };
      return serializer.Serialize(payload);
    }
  }
}
