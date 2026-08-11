using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Diagnostics;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage01FailureReportWriterTests
  {
    [Fact]
    public void TryWrite_WritesCompleteIndentedReportBesideAssemblyWithoutPayloadContent()
    {
      string directory = CreateTemporaryDirectory();

      try
      {
        string assemblyPath = Path.Combine(directory, "BIMBaoGui.Stage01.gha");
        File.WriteAllText(assemblyPath, "binary-payload-secret", new UTF8Encoding(false));
        Exception originalException = CaptureNestedException();
        var context = CreateContext(assemblyPath, originalException);

        Stage01FailureReportWriteResult result = Stage01FailureReportWriter.TryWrite(context);

        Assert.True(result.Success, result.ReportWriteErrorSummary);
        Assert.Null(result.ErrorCode);
        Assert.Equal(Path.GetDirectoryName(context.AssemblyPath), Path.GetDirectoryName(result.ReportPath));
        Assert.Matches(
          new Regex(@"^BIMBaoGui\.Stage01\.failure-\d{8}-\d{6}-\d{3}\.json$"),
          Path.GetFileName(result.ReportPath));

        byte[] reportBytes = File.ReadAllBytes(result.ReportPath);
        Assert.False(
          reportBytes.Length >= 3 && reportBytes[0] == 0xEF && reportBytes[1] == 0xBB && reportBytes[2] == 0xBF,
          "The report must use UTF-8 without a BOM.");

        string reportJson = Encoding.UTF8.GetString(reportBytes);
        Assert.Contains(Environment.NewLine + "  \"schemaVersion\"", reportJson);
        Assert.DoesNotContain("payload-secret", reportJson);

        var serializer = new JavaScriptSerializer();
        var root = Assert.IsType<Dictionary<string, object>>(serializer.DeserializeObject(reportJson));
        Assert.Equal("1.0", root["schemaVersion"]);
        Assert.True(Guid.TryParse(Assert.IsType<string>(root["reportId"]), out _));
        Assert.Equal("2026-08-02T01:02:03.4560000+00:00", root["occurredUtc"]);
        Assert.Equal("2026-08-02T09:02:03.4560000+08:00", root["occurredLocal"]);
        Assert.Equal("DIAG_STAGE01_COMMIT_FAILED", root["diagnosticCode"]);
        Assert.Equal("OFFICIAL_PROJECTION", root["operationStage"]);
        Assert.True(Assert.IsType<bool>(root["transactionRolledBack"]));

        Dictionary<string, object> plugin = GetObject(root, "plugin");
        Assert.Equal("BIMBaoGui.Stage01", plugin["name"]);
        Assert.Equal("0.9.0.0", plugin["version"]);
        Assert.Equal(assemblyPath, plugin["path"]);
        Assert.Matches("^[0-9A-F]{64}$", Assert.IsType<string>(plugin["sha256"]));

        Dictionary<string, object> host = GetObject(root, "host");
        Assert.Equal("2020", host["revitVersionNumber"]);
        Assert.Equal("Autodesk Revit 2020", host["revitVersionName"]);
        Assert.Equal("20200220_1100(x64)", host["revitBuild"]);
        Assert.Equal("x64", host["processArchitecture"]);

        Dictionary<string, object> document = GetObject(root, "document");
        Assert.Equal("20260731test02-v090-validation", document["title"]);
        Assert.Equal(@"D:\18_建模项目\test.rvt", document["path"]);
        Assert.False(Assert.IsType<bool>(document["isReadOnly"]));
        Assert.False(Assert.IsType<bool>(document["isFamilyDocument"]));
        Assert.True(Assert.IsType<bool>(document["isWorkshared"]));

        object[] exceptionChain = Assert.IsType<object[]>(root["exceptionChain"]);
        Assert.Equal(2, exceptionChain.Length);
        Dictionary<string, object> outer = Assert.IsType<Dictionary<string, object>>(exceptionChain[0]);
        Dictionary<string, object> inner = Assert.IsType<Dictionary<string, object>>(exceptionChain[1]);
        Assert.Equal(0, outer["depth"]);
        Assert.Equal(typeof(InvalidOperationException).FullName, outer["type"]);
        Assert.Equal("outer failure", outer["message"]);
        Assert.Equal(1, inner["depth"]);
        Assert.Equal(typeof(ArgumentException).FullName, inner["type"]);
        Assert.Equal("inner failure", inner["message"]);

        foreach (Dictionary<string, object> entry in exceptionChain.Cast<Dictionary<string, object>>())
        {
          Assert.True(entry.ContainsKey("source"));
          Assert.True(entry.ContainsKey("targetSite"));
          Assert.True(entry.ContainsKey("hResult"));
          Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(entry["stackTrace"])));
        }

        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
      }
      finally
      {
        DeleteDirectoryBestEffort(directory);
      }
    }

    [Fact]
    public void TryWrite_WhenOutputDirectoryWasDeleted_ReturnsFailureWithoutHidingOriginalException()
    {
      string directory = CreateTemporaryDirectory();
      string assemblyPath = Path.Combine(directory, "BIMBaoGui.Stage01.gha");
      var originalException = new InvalidOperationException("original commit failure");
      var context = CreateContext(assemblyPath, originalException);
      Directory.Delete(directory, recursive: true);

      Stage01FailureReportWriteResult result = Stage01FailureReportWriter.TryWrite(context);

      Assert.False(result.Success);
      Assert.Equal("REPORT_WRITE_FAILED", result.ErrorCode);
      Assert.Null(result.ReportPath);
      Assert.Contains(typeof(InvalidOperationException).FullName, result.OriginalExceptionSummary);
      Assert.Contains("original commit failure", result.OriginalExceptionSummary);
      Assert.False(string.IsNullOrWhiteSpace(result.ReportWriteErrorSummary));
      Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task TryWrite_ConcurrentSameTimestamp_WritesOneUniqueReportPerFailure()
    {
      string directory = CreateTemporaryDirectory();

      try
      {
        string assemblyPath = Path.Combine(directory, "BIMBaoGui.Stage01.gha");
        File.WriteAllText(assemblyPath, "assembly", new UTF8Encoding(false));
        var context = CreateContext(
          assemblyPath,
          new InvalidOperationException(new string('x', 128 * 1024)));
        const int writerCount = 16;
        var start = new ManualResetEventSlim(false);
        var tasks = new Task<Stage01FailureReportWriteResult>[writerCount];

        for (int index = 0; index < writerCount; ++index)
        {
          tasks[index] = Task.Run(() =>
          {
            start.Wait();
            return Stage01FailureReportWriter.TryWrite(context);
          });
        }

        start.Set();
        Stage01FailureReportWriteResult[] results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.True(result.Success, result.ReportWriteErrorSummary));
        Assert.Equal(writerCount, results.Select(result => result.ReportPath).Distinct().Count());
        Assert.Equal(writerCount, Directory.GetFiles(directory, "*.json").Length);
      }
      finally
      {
        DeleteDirectoryBestEffort(directory);
      }
    }

    [Fact]
    public void TryWrite_WhenFirstReportNameIsDirectory_RetriesNextTimestamp()
    {
      string directory = CreateTemporaryDirectory();

      try
      {
        string assemblyPath = Path.Combine(directory, "BIMBaoGui.Stage01.gha");
        File.WriteAllText(assemblyPath, "assembly", new UTF8Encoding(false));
        Stage01FailureReportContext context = CreateContext(
          assemblyPath,
          new InvalidOperationException("commit failed"));
        string occupiedPath = Path.Combine(
          directory,
          "BIMBaoGui.Stage01.failure-"
            + context.OccurredLocal.ToString(
              "yyyyMMdd-HHmmss-fff",
              System.Globalization.CultureInfo.InvariantCulture)
            + ".json");
        Directory.CreateDirectory(occupiedPath);

        Stage01FailureReportWriteResult result =
          Stage01FailureReportWriter.TryWrite(context);

        Assert.True(result.Success, result.ReportWriteErrorSummary);
        Assert.True(Directory.Exists(occupiedPath));
        Assert.NotEqual(occupiedPath, result.ReportPath);
        Assert.True(File.Exists(result.ReportPath));
      }
      finally
      {
        DeleteDirectoryBestEffort(directory);
      }
    }

    [Fact]
    public void TryWrite_WhenExceptionMessageGetterThrows_NeverThrowsToCaller()
    {
      string directory = CreateTemporaryDirectory();

      try
      {
        string assemblyPath = Path.Combine(directory, "BIMBaoGui.Stage01.gha");
        var context = CreateContext(assemblyPath, new ThrowingMessageException());

        Stage01FailureReportWriteResult result = Stage01FailureReportWriter.TryWrite(context);

        Assert.False(result.Success);
        Assert.Equal("REPORT_WRITE_FAILED", result.ErrorCode);
        Assert.Contains(typeof(ThrowingMessageException).FullName, result.OriginalExceptionSummary);
        Assert.False(string.IsNullOrWhiteSpace(result.ReportWriteErrorSummary));
      }
      finally
      {
        DeleteDirectoryBestEffort(directory);
      }
    }

    private static Stage01FailureReportContext CreateContext(string assemblyPath, Exception exception)
    {
      return new Stage01FailureReportContext
      {
        AssemblyPath = assemblyPath,
        PluginName = "BIMBaoGui.Stage01",
        PluginVersion = "0.9.0.0",
        RevitVersionNumber = "2020",
        RevitVersionName = "Autodesk Revit 2020",
        RevitBuild = "20200220_1100(x64)",
        ProcessArchitecture = "x64",
        DocumentTitle = "20260731test02-v090-validation",
        DocumentPath = @"D:\18_建模项目\test.rvt",
        DocumentIsReadOnly = false,
        DocumentIsFamilyDocument = false,
        DocumentIsWorkshared = true,
        OperationStage = "OFFICIAL_PROJECTION",
        TransactionRolledBack = true,
        Exception = exception,
        OccurredUtc = new DateTimeOffset(2026, 8, 2, 1, 2, 3, 456, TimeSpan.Zero),
        OccurredLocal = new DateTimeOffset(2026, 8, 2, 9, 2, 3, 456, TimeSpan.FromHours(8))
      };
    }

    private static Exception CaptureNestedException()
    {
      try
      {
        try
        {
          throw new ArgumentException("inner failure");
        }
        catch (Exception inner)
        {
          throw new InvalidOperationException("outer failure", inner);
        }
      }
      catch (Exception outer)
      {
        return outer;
      }
    }

    private static Dictionary<string, object> GetObject(Dictionary<string, object> root, string key)
    {
      return Assert.IsType<Dictionary<string, object>>(root[key]);
    }

    private static string CreateTemporaryDirectory()
    {
      string directory = Path.Combine(Path.GetTempPath(), "BIMBaoGui.Stage01.Tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      return directory;
    }

    private static void DeleteDirectoryBestEffort(string directory)
    {
      if (Directory.Exists(directory))
      {
        Directory.Delete(directory, recursive: true);
      }
    }

    private sealed class ThrowingMessageException : Exception
    {
      public override string Message => throw new InvalidOperationException("message getter failed");
    }
  }
}
