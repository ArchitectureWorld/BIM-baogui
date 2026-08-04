using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BIMBaoGui.Stage01.Stage03
{
  internal sealed class AutodeskIfcExportTarget
  {
    internal AutodeskIfcExportTarget(
      string rawIfcPath,
      string directoryPath,
      string fileStem,
      string revitOutputPath)
    {
      RawIfcPath = rawIfcPath ?? string.Empty;
      DirectoryPath = directoryPath ?? string.Empty;
      FileStem = fileStem ?? string.Empty;
      RevitOutputPath = revitOutputPath ?? string.Empty;
    }

    internal string RawIfcPath { get; }
    internal string DirectoryPath { get; }
    internal string FileStem { get; }
    internal string RevitOutputPath { get; }
  }

  internal static class AutodeskIfcExportPathPolicy
  {
    internal static AutodeskIfcExportTarget Validate(string rawIfcPath)
    {
      if (string.IsNullOrWhiteSpace(rawIfcPath))
        throw new ArgumentException("RAW IFC 路径不能为空。", nameof(rawIfcPath));
      if (!string.Equals(
        rawIfcPath,
        rawIfcPath.Trim(),
        StringComparison.Ordinal))
      {
        throw new ArgumentException(
          "RAW IFC 路径不能包含首尾空白。",
          nameof(rawIfcPath));
      }
      if (!Path.IsPathRooted(rawIfcPath))
        throw new ArgumentException("RAW IFC 路径必须是绝对路径。", nameof(rawIfcPath));

      string fullPath;
      try
      {
        fullPath = Path.GetFullPath(rawIfcPath);
      }
      catch (Exception exception) when (
        exception is ArgumentException
        || exception is NotSupportedException
        || exception is PathTooLongException)
      {
        throw new ArgumentException(
          "RAW IFC 路径无效。",
          nameof(rawIfcPath),
          exception);
      }
      if (!string.Equals(
        Path.GetExtension(fullPath),
        ".ifc",
        StringComparison.OrdinalIgnoreCase))
      {
        throw new ArgumentException(
          "RAW IFC 路径扩展名必须是 .ifc。",
          nameof(rawIfcPath));
      }
      string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
      if (!Directory.Exists(directory))
        throw new DirectoryNotFoundException("RAW IFC 输出目录不存在：" + directory);
      string stem = Path.GetFileNameWithoutExtension(fullPath);
      if (string.IsNullOrWhiteSpace(stem))
        throw new ArgumentException("RAW IFC 文件名主体不能为空。", nameof(rawIfcPath));
      string revitOutputPath = Path.GetFullPath(
        Path.Combine(directory, stem + ".ifc"));
      string[] occupied = new[] { fullPath, revitOutputPath }
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Where(path => File.Exists(path) || Directory.Exists(path))
        .ToArray();
      if (occupied.Length > 0)
      {
        throw new IOException(
          "RAW IFC 目标已存在，禁止覆盖："
          + string.Join(", ", occupied.Select(Path.GetFileName)));
      }
      return new AutodeskIfcExportTarget(
        fullPath,
        directory,
        stem,
        revitOutputPath);
    }
  }

  internal sealed class AutodeskIfcExportCompletionDecision
  {
    internal AutodeskIfcExportCompletionDecision(
      bool success,
      string errorCode,
      string message)
    {
      Success = success;
      ErrorCode = errorCode ?? string.Empty;
      Message = message ?? string.Empty;
    }

    internal bool Success { get; }
    internal string ErrorCode { get; }
    internal string Message { get; }
  }

  internal static class AutodeskIfcExportCompletionPolicy
  {
    internal static AutodeskIfcExportCompletionDecision Evaluate(
      bool exportReturned,
      bool fileExists,
      long fileLength,
      string transactionStatus)
    {
      string message = string.Empty;
      if (!exportReturned)
        message = "Autodesk IFC Export 返回 false。";
      else if (!string.Equals(
        transactionStatus,
        "RolledBack",
        StringComparison.Ordinal))
      {
        message = "Autodesk IFC Export 临时事务未成功回滚。";
      }
      else if (!fileExists)
        message = "Autodesk IFC Export 未生成 RAW IFC 文件。";
      else if (fileLength <= 0)
        message = "Autodesk IFC Export 生成的 RAW IFC 文件为空。";

      return message.Length == 0
        ? new AutodeskIfcExportCompletionDecision(
          true,
          string.Empty,
          string.Empty)
        : new AutodeskIfcExportCompletionDecision(
          false,
          Stage03TechnicalFatalCodes.ExportFailed,
          message);
    }
  }

  internal static class AutodeskIfcExportFailurePolicy
  {
    private const string RollbackFailureMessage =
      "Autodesk IFC4 RAW 导出事务回滚失败。";
    private const string DisposeFailureMessage =
      "Autodesk IFC4 RAW 导出事务释放失败。";

    internal static Exception Combine(
      Exception exportFailure,
      Exception rollbackFailure,
      Exception disposeFailure = null)
    {
      var failures = new List<Exception>();
      if (exportFailure != null) failures.Add(exportFailure);
      if (rollbackFailure != null)
        failures.Add(WrapRollbackFailure(rollbackFailure));
      if (disposeFailure != null)
        failures.Add(WrapDisposeFailure(disposeFailure));

      if (failures.Count == 0) return null;
      if (failures.Count == 1) return failures[0];
      return new AggregateException(
        "Autodesk IFC4 RAW 导出或事务清理发生多个失败。",
        failures);
    }

    private static Exception WrapRollbackFailure(Exception rollbackFailure)
    {
      return new InvalidOperationException(
        RollbackFailureMessage,
        rollbackFailure);
    }

    private static Exception WrapDisposeFailure(Exception disposeFailure)
    {
      return new InvalidOperationException(
        DisposeFailureMessage,
        disposeFailure);
    }
  }
}
