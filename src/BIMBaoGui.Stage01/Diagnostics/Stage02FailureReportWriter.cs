using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using System.Web.Script.Serialization;

namespace BIMBaoGui.Stage01.Diagnostics
{
  public sealed class Stage02FailureReportContext
  {
    public string DiagnosticCode { get; set; }
    public string ErrorCode { get; set; }
    public string DiagnosticMessage { get; set; }
    public string InputSignature { get; set; }
    public Guid AttemptToken { get; set; }
    public string PublicationDisposition { get; set; }
    public string FileGuid { get; set; }
    public string DocumentFingerprint { get; set; }
    public string DocumentTitle { get; set; }
    public string RulePackageId { get; set; }
    public string RulePackageVersion { get; set; }
    public string RulePackageSha256 { get; set; }
    public string PreviewHash { get; set; }
    public IReadOnlyList<string> UniqueIds { get; set; }
    public IReadOnlyList<string> PropertyIds { get; set; }
    public string OperationStage { get; set; }
    public string RootCauseStage { get; set; }
    public string CleanupStage { get; set; }
    public bool TransactionRolledBack { get; set; }
    public bool GroupRolledBack { get; set; }
    public bool RollbackConfirmed { get; set; }
    public string TransactionStatus { get; set; }
    public string TransactionGroupStatus { get; set; }
    public string HandoffFinalizerTerminalStatus { get; set; }
    public string HandoffEndCallTerminalStatus { get; set; }
    public Exception Exception { get; set; }
    public DateTimeOffset OccurredUtc { get; set; }
    public DateTimeOffset OccurredLocal { get; set; }
  }

  public sealed class Stage02FailureReportWriteResult
  {
    public bool Success { get; internal set; }
    public string ReportPath { get; internal set; }
    public string ErrorCode { get; internal set; }
    public string OriginalExceptionSummary { get; internal set; }
    public string ReportWriteErrorSummary { get; internal set; }
  }

  public enum Stage02FailureReportPublicationDisposition
  {
    PublishedCurrent,
    DiscardedStale,
    IgnoredDuplicate
  }

  public sealed class Stage02FailureReportDraft
  {
    private readonly Stage02FailureReportContext _snapshot;

    private Stage02FailureReportDraft(Stage02FailureReportContext snapshot)
    {
      _snapshot = snapshot;
    }

    public static Stage02FailureReportDraft Capture(
      Stage02FailureReportContext context)
    {
      if (context == null) throw new ArgumentNullException(nameof(context));
      if (string.IsNullOrWhiteSpace(context.InputSignature))
        throw new ArgumentException(
          "Stage02 写失败报告缺少输入签名。",
          nameof(context));
      if (context.AttemptToken == Guid.Empty)
        throw new ArgumentException(
          "Stage02 写失败报告缺少尝试标识。",
          nameof(context));
      return new Stage02FailureReportDraft(Clone(context, string.Empty));
    }

    internal Stage02FailureReportContext WithPublicationDisposition(
      string publicationDisposition)
    {
      return Clone(_snapshot, publicationDisposition);
    }

    private static Stage02FailureReportContext Clone(
      Stage02FailureReportContext source,
      string publicationDisposition)
    {
      return new Stage02FailureReportContext
      {
        DiagnosticCode = source.DiagnosticCode,
        ErrorCode = source.ErrorCode,
        DiagnosticMessage = source.DiagnosticMessage,
        InputSignature = source.InputSignature,
        AttemptToken = source.AttemptToken,
        PublicationDisposition = publicationDisposition,
        FileGuid = source.FileGuid,
        DocumentFingerprint = source.DocumentFingerprint,
        DocumentTitle = source.DocumentTitle,
        RulePackageId = source.RulePackageId,
        RulePackageVersion = source.RulePackageVersion,
        RulePackageSha256 = source.RulePackageSha256,
        PreviewHash = source.PreviewHash,
        UniqueIds = (source.UniqueIds ?? Array.Empty<string>()).ToArray(),
        PropertyIds = (source.PropertyIds ?? Array.Empty<string>()).ToArray(),
        OperationStage = source.OperationStage,
        RootCauseStage = source.RootCauseStage,
        CleanupStage = source.CleanupStage,
        TransactionRolledBack = source.TransactionRolledBack,
        GroupRolledBack = source.GroupRolledBack,
        RollbackConfirmed = source.RollbackConfirmed,
        TransactionStatus = source.TransactionStatus,
        TransactionGroupStatus = source.TransactionGroupStatus,
        HandoffFinalizerTerminalStatus =
          source.HandoffFinalizerTerminalStatus,
        HandoffEndCallTerminalStatus =
          source.HandoffEndCallTerminalStatus,
        Exception = source.Exception,
        OccurredUtc = source.OccurredUtc,
        OccurredLocal = source.OccurredLocal
      };
    }
  }

  public sealed class Stage02FailureReportPublicationResult
  {
    internal Stage02FailureReportPublicationResult(
      bool wasWritten,
      bool shouldPublishCurrent,
      Stage02FailureReportWriteResult writeResult)
    {
      WasWritten = wasWritten;
      ShouldPublishCurrent = shouldPublishCurrent;
      WriteResult = writeResult;
    }

    public bool WasWritten { get; }
    public bool ShouldPublishCurrent { get; }
    public Stage02FailureReportWriteResult WriteResult { get; }
    public string ReportPath => WriteResult?.ReportPath ?? string.Empty;
  }

  public static class Stage02FailureReportFinalizer
  {
    public static Stage02FailureReportPublicationResult TryPublish(
      Stage02FailureReportDraft draft,
      Stage02FailureReportPublicationDisposition disposition)
    {
      if (draft == null) throw new ArgumentNullException(nameof(draft));
      if (disposition ==
        Stage02FailureReportPublicationDisposition.IgnoredDuplicate)
      {
        return new Stage02FailureReportPublicationResult(
          false,
          false,
          null);
      }

      string publicationDisposition;
      bool publishCurrent;
      switch (disposition)
      {
        case Stage02FailureReportPublicationDisposition.PublishedCurrent:
          publicationDisposition = "PUBLISHED_CURRENT";
          publishCurrent = true;
          break;
        case Stage02FailureReportPublicationDisposition.DiscardedStale:
          publicationDisposition = "DISCARDED_STALE";
          publishCurrent = false;
          break;
        default:
          throw new ArgumentOutOfRangeException(
            nameof(disposition),
            disposition,
            "未知的 Stage02 失败报告发布归属。");
      }

      Stage02FailureReportWriteResult writeResult =
        Stage02FailureReportWriter.TryWrite(
          draft.WithPublicationDisposition(publicationDisposition));
      bool written = writeResult.Success;
      return new Stage02FailureReportPublicationResult(
        written,
        publishCurrent && written,
        writeResult);
    }
  }

  public static class Stage02FailureReportWriter
  {
    private const string ReportWriteFailedCode = "REPORT_WRITE_FAILED";
    private const string ReportFilePrefix = "BIMBaoGui.Stage02.failure-";
    private const string WriteFailureDiagnosticCode =
      "DIAG_STAGE02_WRITE_FAILED";
    private const string PreviewFailureDiagnosticCode =
      "DIAG_STAGE02_PREVIEW_FAILED";

    public static Stage02FailureReportWriteResult TryWrite(
      Stage02FailureReportContext context)
    {
      string temporaryPath = null;
      string originalSummary = Summarize(context == null
        ? null
        : context.Exception);
      try
      {
        if (context == null) throw new ArgumentNullException(nameof(context));
        string assemblyPath = typeof(Stage02FailureReportWriter)
          .Assembly.Location;
        string directory = Path.GetDirectoryName(assemblyPath);
        if (string.IsNullOrWhiteSpace(directory))
          throw new InvalidOperationException("生产程序集目录不可用。");

        temporaryPath = Path.Combine(
          directory,
          ReportFilePrefix + Guid.NewGuid().ToString("N") + ".tmp");
        var serializer = new JavaScriptSerializer
        {
          MaxJsonLength = int.MaxValue
        };
        string json = serializer.Serialize(BuildReport(
          context,
          assemblyPath));
        File.WriteAllText(
          temporaryPath,
          FormatJson(json),
          new UTF8Encoding(false));
        string finalPath = MoveToUniquePath(
          temporaryPath,
          directory,
          context.OccurredLocal);
        temporaryPath = null;
        return new Stage02FailureReportWriteResult
        {
          Success = true,
          ReportPath = finalPath,
          OriginalExceptionSummary = originalSummary
        };
      }
      catch (Exception reportException)
      {
        DeleteBestEffort(temporaryPath);
        return new Stage02FailureReportWriteResult
        {
          Success = false,
          ErrorCode = ReportWriteFailedCode,
          OriginalExceptionSummary = originalSummary,
          ReportWriteErrorSummary = Summarize(reportException)
        };
      }
    }

    private static Dictionary<string, object> BuildReport(
      Stage02FailureReportContext context,
      string assemblyPath)
    {
      string rootCauseStage = string.IsNullOrWhiteSpace(
          context.RootCauseStage)
        ? context.OperationStage ?? string.Empty
        : context.RootCauseStage;
      return new Dictionary<string, object>
      {
        ["schemaVersion"] = "1.0",
        ["stage"] = "STAGE02",
        ["reportId"] = Guid.NewGuid().ToString("D"),
        ["occurredUtc"] = context.OccurredUtc.ToString(
          "O",
          CultureInfo.InvariantCulture),
        ["occurredLocal"] = context.OccurredLocal.ToString(
          "O",
          CultureInfo.InvariantCulture),
        ["diagnosticCode"] = ResolveDiagnosticCode(context),
        ["errorCode"] = ResolveErrorCode(context),
        ["message"] = ResolveDiagnosticMessage(context),
        ["inputSignature"] = context.InputSignature ?? string.Empty,
        ["attemptToken"] = context.AttemptToken == Guid.Empty
          ? string.Empty
          : context.AttemptToken.ToString("D"),
        ["publicationDisposition"] =
          context.PublicationDisposition ?? string.Empty,
        ["operationStage"] = rootCauseStage,
        ["rootCauseStage"] = rootCauseStage,
        ["cleanupStage"] = context.CleanupStage ?? string.Empty,
        ["transactionRolledBack"] = context.TransactionRolledBack,
        ["groupRolledBack"] = context.GroupRolledBack,
        ["rollbackConfirmed"] = context.RollbackConfirmed,
        ["transactionStatus"] = context.TransactionStatus ?? string.Empty,
        ["transactionGroupStatus"] =
          context.TransactionGroupStatus ?? string.Empty,
        ["handoffFinalizerTerminalStatus"] =
          context.HandoffFinalizerTerminalStatus ?? string.Empty,
        ["handoffEndCallTerminalStatus"] =
          context.HandoffEndCallTerminalStatus ?? string.Empty,
        ["fileGuid"] = context.FileGuid ?? string.Empty,
        ["documentFingerprint"] =
          context.DocumentFingerprint ?? string.Empty,
        ["documentTitle"] = context.DocumentTitle ?? string.Empty,
        ["rulePackage"] = new Dictionary<string, object>
        {
          ["id"] = context.RulePackageId ?? string.Empty,
          ["version"] = context.RulePackageVersion ?? string.Empty,
          ["sha256"] = context.RulePackageSha256 ?? string.Empty
        },
        ["previewHash"] = context.PreviewHash ?? string.Empty,
        ["uniqueIds"] = Sort(context.UniqueIds),
        ["propertyIds"] = Sort(context.PropertyIds),
        ["plugin"] = new Dictionary<string, object>
        {
          ["path"] = assemblyPath,
          ["sha256"] = TrySha256(assemblyPath)
        },
        ["exceptionChain"] = BuildExceptionChain(context.Exception)
      };
    }

    private static string ResolveDiagnosticCode(
      Stage02FailureReportContext context)
    {
      string value = (context.DiagnosticCode ?? string.Empty).Trim();
      if (value.Length == 0) return WriteFailureDiagnosticCode;
      if (string.Equals(
          value,
          WriteFailureDiagnosticCode,
          StringComparison.Ordinal)
        || string.Equals(
          value,
          PreviewFailureDiagnosticCode,
          StringComparison.Ordinal))
      {
        return value;
      }
      throw new ArgumentException("不支持的 Stage02 诊断码。", nameof(context));
    }

    private static string ResolveErrorCode(Stage02FailureReportContext context)
    {
      string value = (context.ErrorCode ?? string.Empty).Trim();
      if (value.Length == 0) return string.Empty;
      switch (value)
      {
        case "STAGE02_SELECTION_SERVICE_EXCEPTION":
        case "STAGE02_SELECTION_NO_RESULT":
        case "STAGE02_PREVIEW_SERVICE_EXCEPTION":
        case "STAGE02_PREVIEW_NO_RESULT":
        case "STAGE02_WRITE_ENQUEUE_EXCEPTION":
        case "STAGE02_WRITE_ENQUEUE_REJECTED":
        case "STAGE02_WRITE_COMPLETION_CONSUMER_FAILED":
        case "WRITE_HOST_CALLBACK":
          return value;
        default:
          throw new ArgumentException(
            "不支持的 Stage02 失败错误码。",
            nameof(context));
      }
    }

    private static string ResolveDiagnosticMessage(
      Stage02FailureReportContext context)
    {
      string value = (context.DiagnosticMessage ?? string.Empty).Trim();
      if (value.Length == 0) return string.Empty;
      switch (value)
      {
        case "Stage02 元素选择发生技术失败。":
        case "Stage02 元素选择服务未返回结果。":
        case "Stage02 预览构建发生技术失败。":
        case "Stage02 预览服务未返回可发布结果。":
        case "Stage02 写入请求提交发生技术失败。":
        case "Stage02 写入宿主回调发生技术失败。":
        case "Stage02 写入结果完成消费者发生技术失败；业务完成未重试。":
          return value;
        default:
          throw new ArgumentException(
            "不支持的 Stage02 诊断消息。",
            nameof(context));
      }
    }

    private static string[] Sort(IEnumerable<string> values)
    {
      return (values ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
    }

    private static object[] BuildExceptionChain(Exception exception)
    {
      var entries = new List<object>();
      var visited = new HashSet<Exception>(ExceptionReferenceComparer.Instance);
      TraverseExceptionGraph(exception, entries, visited, 0);
      return entries.ToArray();
    }

    private static int TraverseExceptionGraph(
      Exception exception,
      ICollection<object> entries,
      ISet<Exception> visited,
      int depth)
    {
      if (exception == null || !visited.Add(exception)) return depth;
      entries.Add(new Dictionary<string, object>
      {
        ["depth"] = depth,
        ["type"] = exception.GetType().FullName,
        ["source"] = exception.Source,
        ["targetSite"] = exception.TargetSite == null
          ? string.Empty
          : exception.TargetSite.ToString(),
        ["hResult"] = exception.HResult,
        ["stackTrace"] = exception.StackTrace ?? string.Empty
      });
      depth++;
      AggregateException aggregate = exception as AggregateException;
      if (aggregate != null)
      {
        foreach (Exception inner in aggregate.Flatten().InnerExceptions)
          TraverseExceptionGraph(inner, entries, visited, depth);
        return depth;
      }
      return TraverseExceptionGraph(
        exception.InnerException,
        entries,
        visited,
        depth);
    }

    private sealed class ExceptionReferenceComparer
      : IEqualityComparer<Exception>
    {
      internal static readonly ExceptionReferenceComparer Instance =
        new ExceptionReferenceComparer();

      public bool Equals(Exception left, Exception right)
      {
        return ReferenceEquals(left, right);
      }

      public int GetHashCode(Exception exception)
      {
        return RuntimeHelpers.GetHashCode(exception);
      }
    }

    private static string MoveToUniquePath(
      string temporaryPath,
      string directory,
      DateTimeOffset occurredLocal)
    {
      DateTimeOffset timestamp = occurredLocal;
      for (int attempt = 0; attempt < 10000; attempt++)
      {
        string path = Path.Combine(
          directory,
          ReportFilePrefix
            + timestamp.ToString(
              "yyyyMMdd-HHmmss-fff",
              CultureInfo.InvariantCulture)
            + ".json");
        try
        {
          File.Move(temporaryPath, path);
          return path;
        }
        catch (IOException exception) when (
          AtomicJsonReportWriter.IsCreateNewCollision(exception))
        {
          timestamp = timestamp.AddMilliseconds(1);
        }
      }
      throw new IOException("无法分配唯一的 Stage02 失败报告路径。");
    }

    private static string TrySha256(string path)
    {
      try
      {
        using (FileStream stream = File.OpenRead(path))
        using (SHA256 algorithm = SHA256.Create())
        {
          return string.Concat(algorithm.ComputeHash(stream)
            .Select(value => value.ToString(
              "x2",
              CultureInfo.InvariantCulture)));
        }
      }
      catch
      {
        return string.Empty;
      }
    }

    private static string Summarize(Exception exception)
    {
      if (exception == null) return string.Empty;
      try
      {
        return exception.GetType().FullName
          + "; HResult=0x"
          + exception.HResult.ToString("X8", CultureInfo.InvariantCulture);
      }
      catch
      {
        return "System.Exception: <unavailable>";
      }
    }

    private static string FormatJson(string json)
    {
      var builder = new StringBuilder(json.Length + 256);
      int indentation = 0;
      bool insideString = false;
      bool escaped = false;
      foreach (char character in json)
      {
        if (insideString)
        {
          builder.Append(character);
          if (escaped) escaped = false;
          else if (character == '\\') escaped = true;
          else if (character == '"') insideString = false;
          continue;
        }
        switch (character)
        {
          case '"':
            insideString = true;
            builder.Append(character);
            break;
          case '{':
          case '[':
            builder.Append(character);
            AppendLine(builder, ++indentation);
            break;
          case '}':
          case ']':
            AppendLine(builder, --indentation);
            builder.Append(character);
            break;
          case ',':
            builder.Append(character);
            AppendLine(builder, indentation);
            break;
          case ':':
            builder.Append(": ");
            break;
          default:
            if (!char.IsWhiteSpace(character)) builder.Append(character);
            break;
        }
      }
      return builder.ToString();
    }

    private static void AppendLine(StringBuilder builder, int indentation)
    {
      builder.Append(Environment.NewLine);
      builder.Append(' ', Math.Max(0, indentation) * 2);
    }

    private static void DeleteBestEffort(string path)
    {
      if (string.IsNullOrWhiteSpace(path)) return;
      try
      {
        if (File.Exists(path)) File.Delete(path);
      }
      catch
      {
      }
    }
  }
}
