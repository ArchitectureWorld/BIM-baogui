using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Autodesk.Revit.DB;
using BIMBaoGui.Stage01.Stage03;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class AutodeskIfcExportResult
  {
    internal string RawIfcPath { get; set; } = string.Empty;
    internal long RawIfcLength { get; set; }
    internal string RawIfcSha256 { get; set; } = string.Empty;
    internal string TransactionStrategy { get; set; } = string.Empty;
    internal string TransactionStatus { get; set; } = string.Empty;
  }

  internal sealed class AutodeskIfcExportService
  {
    private const string RollbackStrategy = "ROLLBACK_AFTER_EXPORT";

    internal AutodeskIfcExportResult Export(
      Document document,
      string rawIfcPath)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      AutodeskIfcExportTarget target = ValidateTarget(rawIfcPath);

      bool exportReturned = false;
      TransactionStatus rollbackStatus = TransactionStatus.Uninitialized;
      Transaction transaction = null;
      Exception exportFailure = null;
      Exception rollbackFailure = null;
      Exception disposeFailure = null;
      try
      {
        transaction = new Transaction(
          document,
          "湖北BIM报规｜Autodesk IFC4 RAW 导出");
        TransactionStatus startStatus = transaction.Start();
        if (startStatus != TransactionStatus.Started)
        {
          throw new InvalidOperationException(
            "无法启动 Autodesk IFC4 RAW 导出事务：" + startStatus);
        }
        using (var options = new IFCExportOptions
        {
          FileVersion = IFCVersion.IFC4
        })
        {
          options.AddOption(
            "ExportInternalRevitPropertySets",
            "true");
          exportReturned = document.Export(
            target.DirectoryPath,
            Path.GetFileNameWithoutExtension(target.RawIfcPath),
            options);
        }
      }
      catch (Exception exception)
      {
        exportFailure = exception;
      }
      finally
      {
        if (transaction != null)
        {
          try
          {
            TransactionStatus currentStatus = transaction.GetStatus();
            if (currentStatus == TransactionStatus.Started)
              rollbackStatus = transaction.RollBack();
            else
              rollbackStatus = currentStatus;
          }
          catch (Exception exception)
          {
            rollbackFailure = exception;
          }
          try
          {
            transaction.Dispose();
          }
          catch (Exception exception)
          {
            disposeFailure = exception;
          }
        }
      }

      Exception combinedFailure = AutodeskIfcExportFailurePolicy.Combine(
        exportFailure,
        rollbackFailure,
        disposeFailure);
      if (combinedFailure != null)
        ExceptionDispatchInfo.Capture(combinedFailure).Throw();

      string transactionStatus = rollbackStatus == TransactionStatus.RolledBack
        ? TransactionStatus.RolledBack.ToString()
        : rollbackStatus.ToString();
      bool fileExists = File.Exists(target.RawIfcPath);
      FileInfo file = fileExists
        ? new FileInfo(target.RawIfcPath)
        : null;
      AutodeskIfcExportCompletionDecision completion =
        AutodeskIfcExportCompletionPolicy.Evaluate(
          exportReturned,
          fileExists,
          file == null ? 0L : file.Length,
          transactionStatus);
      if (!completion.Success)
        throw new InvalidOperationException(completion.Message);

      return new AutodeskIfcExportResult
      {
        RawIfcPath = target.RawIfcPath,
        RawIfcLength = file.Length,
        RawIfcSha256 = ComputeSha256(target.RawIfcPath),
        TransactionStrategy = RollbackStrategy,
        TransactionStatus = transactionStatus
      };
    }

    private static AutodeskIfcExportTarget ValidateTarget(string rawIfcPath)
    {
      if (!Path.IsPathRooted(rawIfcPath ?? string.Empty))
        throw new ArgumentException("RAW IFC 路径必须是绝对路径。", nameof(rawIfcPath));
      string fullPath = Path.GetFullPath(rawIfcPath);
      string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
      if (!Directory.Exists(directory))
        throw new DirectoryNotFoundException("RAW IFC 输出目录不存在：" + directory);
      return AutodeskIfcExportPathPolicy.Validate(rawIfcPath);
    }

    private static string ComputeSha256(string path)
    {
      using (SHA256 algorithm = SHA256.Create())
      using (FileStream stream = File.OpenRead(path))
      {
        return BitConverter.ToString(algorithm.ComputeHash(stream))
          .Replace("-", string.Empty)
          .ToLowerInvariant();
      }
    }
  }
}
