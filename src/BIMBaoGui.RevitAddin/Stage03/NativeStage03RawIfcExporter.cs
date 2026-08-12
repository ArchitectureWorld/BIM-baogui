using System;
using System.IO;
using System.Runtime.ExceptionServices;
using Autodesk.Revit.DB;
using BIMBaoGui.HifcCore;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal sealed class NativeStage03RawIfcArtifact
  {
    internal string Path { get; set; } = string.Empty;
    internal long Length { get; set; }
    internal string Sha256 { get; set; } = string.Empty;
    internal string TransactionStrategy { get; set; } = string.Empty;
    internal string TransactionStatus { get; set; } = string.Empty;
  }

  internal sealed class NativeStage03RawIfcExporter
  {
    private const string TransactionStrategy = "ROLLBACK_AFTER_EXPORT";

    internal NativeStage03RawIfcArtifact Export(
      Document document,
      string rawIfcPath)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      string path = ValidateTarget(rawIfcPath);
      string directory = Path.GetDirectoryName(path) ?? string.Empty;
      string fileStem = Path.GetFileNameWithoutExtension(path);

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
          "湖北BIM报规｜Stage03 Autodesk IFC4 RAW 导出");
        TransactionStatus startStatus = transaction.Start();
        if (startStatus != TransactionStatus.Started)
          throw new InvalidOperationException(
            "无法启动 IFC4 RAW 导出事务：" + startStatus);

        using (var options = new IFCExportOptions
        {
          FileVersion = IFCVersion.IFC4
        })
        {
          options.AddOption("ExportInternalRevitPropertySets", "true");
          options.AddOption("ExportIFCCommonPropertySets", "true");
          options.AddOption("ExportBaseQuantities", "true");
          options.AddOption("Use2DRoomBoundaryForVolume", "false");
          exportReturned = document.Export(directory, fileStem, options);
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
            TransactionStatus current = transaction.GetStatus();
            rollbackStatus = current == TransactionStatus.Started
              ? transaction.RollBack()
              : current;
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

      Exception combined = Combine(
        exportFailure,
        rollbackFailure,
        disposeFailure);
      if (combined != null) ExceptionDispatchInfo.Capture(combined).Throw();
      if (!exportReturned)
        throw new InvalidOperationException("Revit Document.Export 返回 false。" );
      if (rollbackStatus != TransactionStatus.RolledBack)
        throw new InvalidOperationException(
          "IFC4 RAW 导出事务未按预期回滚：" + rollbackStatus);
      if (!File.Exists(path))
        throw new FileNotFoundException(
          "Revit 返回导出成功，但 RAW IFC 不存在。",
          path);
      var info = new FileInfo(path);
      if (info.Length <= 0)
        throw new InvalidDataException("Revit 生成的 RAW IFC 为空。" );
      return new NativeStage03RawIfcArtifact
      {
        Path = path,
        Length = info.Length,
        Sha256 = HifcCoreService.ComputeSha256(path),
        TransactionStrategy = TransactionStrategy,
        TransactionStatus = rollbackStatus.ToString()
      };
    }

    private static string ValidateTarget(string rawIfcPath)
    {
      if (string.IsNullOrWhiteSpace(rawIfcPath)
        || !Path.IsPathRooted(rawIfcPath))
        throw new ArgumentException("RAW IFC 路径必须是绝对路径。" );
      string path = Path.GetFullPath(rawIfcPath);
      if (!string.Equals(
        Path.GetExtension(path),
        ".ifc",
        StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("RAW IFC 必须使用 .ifc 扩展名。" );
      string directory = Path.GetDirectoryName(path) ?? string.Empty;
      if (!Directory.Exists(directory))
        throw new DirectoryNotFoundException(
          "RAW IFC 输出目录不存在：" + directory);
      if (File.Exists(path) || Directory.Exists(path))
        throw new IOException("RAW IFC 目标已存在：" + path);
      return path;
    }

    private static Exception Combine(params Exception[] exceptions)
    {
      Exception[] failures = Array.FindAll(
        exceptions ?? Array.Empty<Exception>(),
        value => value != null);
      if (failures.Length == 0) return null;
      if (failures.Length == 1) return failures[0];
      return new AggregateException(
        "IFC4 RAW 导出与事务清理同时出现异常。",
        failures);
    }
  }
}
