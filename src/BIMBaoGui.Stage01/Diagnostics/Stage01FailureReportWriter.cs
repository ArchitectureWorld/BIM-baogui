using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace BIMBaoGui.Stage01.Diagnostics
{
  public sealed class Stage01FailureReportContext
  {
    public string AssemblyPath { get; set; }
    public string PluginName { get; set; }
    public string PluginVersion { get; set; }
    public string RevitVersionNumber { get; set; }
    public string RevitVersionName { get; set; }
    public string RevitBuild { get; set; }
    public string ProcessArchitecture { get; set; }
    public string DocumentTitle { get; set; }
    public string DocumentPath { get; set; }
    public bool DocumentIsReadOnly { get; set; }
    public bool DocumentIsFamilyDocument { get; set; }
    public bool DocumentIsWorkshared { get; set; }
    public string OperationStage { get; set; }
    public bool TransactionRolledBack { get; set; }
    public Exception Exception { get; set; }
    public DateTimeOffset OccurredUtc { get; set; }
    public DateTimeOffset OccurredLocal { get; set; }
  }

  public sealed class Stage01FailureReportWriteResult
  {
    public bool Success { get; internal set; }
    public string ReportPath { get; internal set; }
    public string ErrorCode { get; internal set; }
    public string OriginalExceptionSummary { get; internal set; }
    public string ReportWriteErrorSummary { get; internal set; }
  }

  public static class Stage01FailureReportWriter
  {
    private const string DiagnosticCode = "DIAG_STAGE01_COMMIT_FAILED";
    private const string ReportWriteFailedCode = "REPORT_WRITE_FAILED";
    private const string ReportFilePrefix = "BIMBaoGui.Stage01.failure-";

    public static Stage01FailureReportWriteResult TryWrite(Stage01FailureReportContext context)
    {
      string temporaryPath = null;
      string originalExceptionSummary = SummarizeBestEffort(context?.Exception);

      try
      {
        if (context == null)
          throw new ArgumentNullException(nameof(context));

        string directory = Path.GetDirectoryName(context.AssemblyPath);
        if (string.IsNullOrWhiteSpace(directory))
          throw new InvalidOperationException("The plugin assembly directory is unavailable.");

        temporaryPath = Path.Combine(
          directory,
          ReportFilePrefix + Guid.NewGuid().ToString("N") + ".tmp");

        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        string compactJson = serializer.Serialize(BuildReport(context));
        string formattedJson = FormatJson(compactJson);
        File.WriteAllText(temporaryPath, formattedJson, new UTF8Encoding(false));
        string finalPath = MoveToUniqueReportPath(
          temporaryPath,
          directory,
          context.OccurredLocal);
        temporaryPath = null;

        return new Stage01FailureReportWriteResult
        {
          Success = true,
          ReportPath = finalPath,
          OriginalExceptionSummary = originalExceptionSummary
        };
      }
      catch (Exception reportWriteException)
      {
        DeleteBestEffort(temporaryPath);
        return new Stage01FailureReportWriteResult
        {
          Success = false,
          ErrorCode = ReportWriteFailedCode,
          OriginalExceptionSummary = originalExceptionSummary,
          ReportWriteErrorSummary = SummarizeBestEffort(reportWriteException)
        };
      }
    }

    private static Dictionary<string, object> BuildReport(Stage01FailureReportContext context)
    {
      return new Dictionary<string, object>
      {
        ["schemaVersion"] = "1.0",
        ["reportId"] = Guid.NewGuid().ToString("D"),
        ["occurredUtc"] = context.OccurredUtc.ToString("O", CultureInfo.InvariantCulture),
        ["occurredLocal"] = context.OccurredLocal.ToString("O", CultureInfo.InvariantCulture),
        ["diagnosticCode"] = DiagnosticCode,
        ["operationStage"] = context.OperationStage,
        ["transactionRolledBack"] = context.TransactionRolledBack,
        ["plugin"] = new Dictionary<string, object>
        {
          ["name"] = context.PluginName,
          ["version"] = context.PluginVersion,
          ["path"] = context.AssemblyPath,
          ["sha256"] = TryComputeSha256(context.AssemblyPath)
        },
        ["host"] = new Dictionary<string, object>
        {
          ["revitVersionNumber"] = context.RevitVersionNumber,
          ["revitVersionName"] = context.RevitVersionName,
          ["revitBuild"] = context.RevitBuild,
          ["processArchitecture"] = context.ProcessArchitecture
        },
        ["document"] = new Dictionary<string, object>
        {
          ["title"] = context.DocumentTitle,
          ["path"] = context.DocumentPath,
          ["isReadOnly"] = context.DocumentIsReadOnly,
          ["isFamilyDocument"] = context.DocumentIsFamilyDocument,
          ["isWorkshared"] = context.DocumentIsWorkshared
        },
        ["exceptionChain"] = BuildExceptionChain(context.Exception)
      };
    }

    private static object[] BuildExceptionChain(Exception exception)
    {
      var entries = new List<object>();
      int depth = 0;

      for (Exception current = exception; current != null; current = current.InnerException)
      {
        entries.Add(new Dictionary<string, object>
        {
          ["depth"] = depth,
          ["type"] = current.GetType().FullName,
          ["message"] = current.Message,
          ["source"] = current.Source,
          ["targetSite"] = current.TargetSite?.ToString(),
          ["hResult"] = current.HResult,
          ["stackTrace"] = current.StackTrace ?? string.Empty
        });
        depth++;
      }

      return entries.ToArray();
    }

    private static string TryComputeSha256(string path)
    {
      try
      {
        using (FileStream stream = File.OpenRead(path))
        using (SHA256 algorithm = SHA256.Create())
        {
          byte[] hash = algorithm.ComputeHash(stream);
          var builder = new StringBuilder(hash.Length * 2);
          foreach (byte value in hash)
            builder.Append(value.ToString("X2", CultureInfo.InvariantCulture));
          return builder.ToString();
        }
      }
      catch
      {
        return null;
      }
    }

    private static string MoveToUniqueReportPath(
      string temporaryPath,
      string directory,
      DateTimeOffset occurredLocal)
    {
      DateTimeOffset timestamp = occurredLocal;
      for (int attempt = 0; attempt < 1000; ++attempt)
      {
        string fileName = ReportFilePrefix
          + timestamp.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
          + ".json";
        string path = Path.Combine(directory, fileName);
        try
        {
          File.Move(temporaryPath, path);
          return path;
        }
        catch (IOException exception) when (
          AtomicJsonReportWriter.IsCreateNewCollision(exception))
        {
        }
        timestamp = timestamp.AddMilliseconds(1);
      }

      throw new IOException("A unique Stage01 failure report path could not be allocated.");
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
          if (escaped)
          {
            escaped = false;
          }
          else if (character == '\\')
          {
            escaped = true;
          }
          else if (character == '"')
          {
            insideString = false;
          }
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
            if (!char.IsWhiteSpace(character))
              builder.Append(character);
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

    private static string SummarizeBestEffort(Exception exception)
    {
      if (exception == null)
        return string.Empty;

      string typeName;
      try
      {
        typeName = exception.GetType().FullName;
      }
      catch
      {
        typeName = "System.Exception";
      }

      string message;
      try
      {
        message = exception.Message;
      }
      catch
      {
        message = "<exception message unavailable>";
      }

      return (typeName ?? "System.Exception") + ": " + (message ?? string.Empty);
    }

    private static void DeleteBestEffort(string path)
    {
      if (string.IsNullOrWhiteSpace(path))
        return;

      try
      {
        if (File.Exists(path))
          File.Delete(path);
      }
      catch
      {
      }
    }
  }
}
