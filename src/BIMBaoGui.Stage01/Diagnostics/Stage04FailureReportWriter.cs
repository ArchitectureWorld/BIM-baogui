using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace BIMBaoGui.Stage01.Diagnostics
{
  internal sealed class Stage04FailureReportContext
  {
    public string AssemblyPath { get; set; }
    public string SourcePath { get; set; }
    public string DestinationPath { get; set; }
    public string OperationStage { get; set; }
    public Exception Exception { get; set; }
    public DateTimeOffset OccurredUtc { get; set; }
    public DateTimeOffset OccurredLocal { get; set; }
  }

  internal sealed class Stage04FailureReportWriteResult
  {
    public bool Success { get; set; }
    public string ReportPath { get; set; }
    public string ErrorCode { get; set; }
    public string OriginalExceptionSummary { get; set; }
    public string ReportWriteErrorSummary { get; set; }
  }

  internal static class Stage04FailureReportWriter
  {
    private const string DiagnosticCode =
      "DIAG_STAGE04_MVD_NORMALIZATION_FAILED";
    private const string ReportWriteFailedCode = "REPORT_WRITE_FAILED";
    private const string ReportFilePrefix = "BIMBaoGui.Stage04.failure-";

    public static Stage04FailureReportWriteResult TryWrite(
      Stage04FailureReportContext context)
    {
      string temporaryPath = null;
      string originalSummary = Summarize(context?.Exception);
      try
      {
        if (context == null) throw new ArgumentNullException(nameof(context));
        string directory = Path.GetDirectoryName(context.AssemblyPath);
        if (string.IsNullOrWhiteSpace(directory))
          throw new InvalidOperationException("插件程序集目录不可用。");
        temporaryPath = Path.Combine(
          directory,
          ReportFilePrefix + Guid.NewGuid().ToString("N") + ".tmp");

        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        string json = FormatJson(serializer.Serialize(BuildReport(context)));
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
        string finalPath = MoveToUniquePath(
          temporaryPath,
          directory,
          context.OccurredLocal);
        temporaryPath = null;
        return new Stage04FailureReportWriteResult
        {
          Success = true,
          ReportPath = finalPath,
          OriginalExceptionSummary = originalSummary
        };
      }
      catch (Exception reportException)
      {
        DeleteBestEffort(temporaryPath);
        return new Stage04FailureReportWriteResult
        {
          Success = false,
          ErrorCode = ReportWriteFailedCode,
          OriginalExceptionSummary = originalSummary,
          ReportWriteErrorSummary = Summarize(reportException)
        };
      }
    }

    private static Dictionary<string, object> BuildReport(
      Stage04FailureReportContext context)
    {
      return new Dictionary<string, object>
      {
        ["schemaVersion"] = "1.0",
        ["reportId"] = Guid.NewGuid().ToString("D"),
        ["occurredUtc"] = context.OccurredUtc.ToString(
          "O",
          CultureInfo.InvariantCulture),
        ["occurredLocal"] = context.OccurredLocal.ToString(
          "O",
          CultureInfo.InvariantCulture),
        ["diagnosticCode"] = DiagnosticCode,
        ["operationStage"] = context.OperationStage,
        ["sourcePath"] = context.SourcePath,
        ["destinationPath"] = context.DestinationPath,
        ["pluginPath"] = context.AssemblyPath,
        ["exceptionChain"] = BuildExceptionChain(context.Exception)
      };
    }

    private static object[] BuildExceptionChain(Exception exception)
    {
      var result = new List<object>();
      int depth = 0;
      for (Exception current = exception;
        current != null;
        current = current.InnerException)
      {
        result.Add(new Dictionary<string, object>
        {
          ["depth"] = depth++,
          ["type"] = current.GetType().FullName,
          ["message"] = current.Message,
          ["hResult"] = current.HResult,
          ["stackTrace"] = current.StackTrace ?? string.Empty
        });
      }
      return result.ToArray();
    }

    private static string MoveToUniquePath(
      string temporaryPath,
      string directory,
      DateTimeOffset occurredLocal)
    {
      DateTimeOffset timestamp = occurredLocal;
      for (int attempt = 0; attempt < 1000; attempt++)
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
        catch (IOException) when (File.Exists(path))
        {
          timestamp = timestamp.AddMilliseconds(1);
        }
      }
      throw new IOException("无法分配唯一 Stage04 失败报告路径。");
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
            indentation++;
            AppendNewLine(builder, indentation);
            break;
          case '}':
          case ']':
            indentation--;
            AppendNewLine(builder, indentation);
            builder.Append(character);
            break;
          case ',':
            builder.Append(character);
            AppendNewLine(builder, indentation);
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

    private static void AppendNewLine(StringBuilder builder, int indentation)
    {
      builder.Append(Environment.NewLine);
      builder.Append(' ', indentation * 2);
    }

    private static string Summarize(Exception exception)
    {
      if (exception == null) return string.Empty;
      return exception.GetType().FullName + ": " + exception.Message;
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
