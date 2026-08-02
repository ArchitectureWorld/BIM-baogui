using System;
using System.IO;
using System.Linq;
using BIMBaoGui.Stage01.Diagnostics;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage04FailureReportWriterTests
  {
    [Fact]
    public void TryWrite_creates_JSON_beside_the_plugin_without_temporary_files()
    {
      string directory = Path.Combine(
        Path.GetTempPath(),
        "BIMBaoGui-Stage04FailureReportTests-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      try
      {
        string assemblyPath = Path.Combine(directory, "BIMBaoGui.Stage01.gha");
        File.WriteAllText(assemblyPath, "plugin");
        var exception = new InvalidDataException("normalization failed");

        Stage04FailureReportWriteResult result = Stage04FailureReportWriter.TryWrite(
          new Stage04FailureReportContext
          {
            AssemblyPath = assemblyPath,
            SourcePath = @"D:\model.ifc",
            DestinationPath = @"D:\model-MVD.ifc",
            OperationStage = "validate-output",
            Exception = exception,
            OccurredUtc = new DateTimeOffset(2026, 8, 2, 9, 10, 11, TimeSpan.Zero),
            OccurredLocal = new DateTimeOffset(2026, 8, 2, 17, 10, 11, TimeSpan.FromHours(8))
          });

        Assert.True(result.Success, result.ReportWriteErrorSummary);
        Assert.Equal(directory, Path.GetDirectoryName(result.ReportPath));
        Assert.StartsWith(
          "BIMBaoGui.Stage04.failure-20260802-171011-000",
          Path.GetFileName(result.ReportPath));
        string json = File.ReadAllText(result.ReportPath);
        Assert.Contains("DIAG_STAGE04_MVD_NORMALIZATION_FAILED", json);
        Assert.Contains("validate-output", json);
        Assert.Contains("D:\\\\model.ifc", json);
        Assert.Contains("normalization failed", json);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        Assert.DoesNotContain(
          Directory.GetFiles(directory).Select(Path.GetFileName),
          name => name.Contains("backup") || name.EndsWith(".bak"));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }
  }
}
