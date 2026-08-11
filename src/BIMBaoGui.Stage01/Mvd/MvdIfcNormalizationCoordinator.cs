using System;
using System.Collections.Generic;
using BIMBaoGui.Stage01.Diagnostics;

namespace BIMBaoGui.Stage01.Mvd
{
  internal sealed class MvdIfcComponentResult
  {
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public IReadOnlyList<string> Messages { get; set; } = Array.Empty<string>();
  }

  internal sealed class MvdIfcNormalizationCoordinator
  {
    private readonly string _assemblyPath;

    public MvdIfcNormalizationCoordinator(string assemblyPath)
    {
      _assemblyPath = assemblyPath ?? string.Empty;
    }

    public MvdIfcComponentResult Execute(
      string sourcePath,
      string destinationPath)
    {
      string resolvedDestination = destinationPath ?? string.Empty;
      try
      {
        resolvedDestination = MvdIfcPathPolicy.ResolveDestination(
          sourcePath,
          destinationPath);
        MvdIfcFileResult result = new MvdIfcFileService().Execute(
          sourcePath,
          resolvedDestination);
        return new MvdIfcComponentResult
        {
          Success = true,
          Status = "MVD IFC 规范化与回读验收通过",
          OutputPath = result.OutputPath,
          Messages = result.Messages
        };
      }
      catch (Exception exception)
      {
        DateTimeOffset occurredUtc = DateTimeOffset.UtcNow;
        Stage04FailureReportWriteResult report = Stage04FailureReportWriter.TryWrite(
          new Stage04FailureReportContext
          {
            AssemblyPath = _assemblyPath,
            SourcePath = sourcePath,
            DestinationPath = resolvedDestination,
            OperationStage = "normalize-ifc",
            Exception = exception,
            OccurredUtc = occurredUtc,
            OccurredLocal = occurredUtc.ToLocalTime()
          });
        var messages = new List<string>
        {
          exception.GetType().Name + "：" + exception.Message
        };
        if (report.Success)
          messages.Add("失败报告：" + report.ReportPath);
        else
          messages.Add(
            "失败报告写入失败：" + report.ReportWriteErrorSummary);
        return new MvdIfcComponentResult
        {
          Success = false,
          Status = "MVD IFC 规范化失败",
          OutputPath = string.Empty,
          Messages = messages
        };
      }
    }
  }
}
