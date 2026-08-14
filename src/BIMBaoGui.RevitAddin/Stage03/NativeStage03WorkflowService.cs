using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.HifcCore;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal static class NativeStage03WorkflowService
  {
    internal static NativeStage03ScanResult Scan(
      UIApplication application,
      NativeStage03ScanRequest request)
    {
      NativeStage03ScanResult result = NativeStage03Scanner.Scan(
        application,
        request);
      NativeStage03SessionStore.StoreScan(result);
      return result;
    }

    internal static NativeStage03ExecutionResult Execute(
      UIApplication application,
      NativeStage03ExportRequest request)
    {
      if (application == null)
        throw new ArgumentNullException(nameof(application));
      if (request?.ConfirmedScan == null)
        return Failure(null, NativeStage03Codes.ScanExpired,
          "导出请求缺少已确认的 Stage03 预检。" );
      NativeStage03ScanResult confirmed = request.ConfirmedScan;
      NativeStage03ScanResult current = NativeStage03Scanner.Scan(
        application,
        new NativeStage03ScanRequest
        {
          Mode = confirmed.Mode,
          ForceReason = confirmed.ForceReason
        });
      NativeStage03SessionStore.StoreScan(current);
      if (!string.Equals(
        confirmed.ScanHash,
        current.ScanHash,
        StringComparison.OrdinalIgnoreCase))
      {
        return Failure(
          null,
          NativeStage03Codes.ScanExpired,
          "Stage03 预检已过期；模型、参数、规则或项目条件已变化。原="
            + confirmed.ScanHash + "，当前=" + current.ScanHash);
      }
      if (!current.AllowExport)
      {
        return Failure(
          null,
          NativeStage03Codes.FieldNotReady,
          "当前 Stage03 门禁不允许导出："
            + string.Join(" ", current.Messages));
      }
      if (string.IsNullOrWhiteSpace(request.OutputDirectory)
        || !Path.IsPathRooted(request.OutputDirectory))
      {
        return Failure(
          null,
          NativeStage03Codes.InvalidOutputDirectory,
          "Stage03 输出目录必须是绝对路径。" );
      }

      Document document = application.ActiveUIDocument?.Document;
      if (document == null)
        return Failure(null, NativeStage03Codes.DocumentUnavailable,
          "当前没有活动 Revit 项目文档。" );
      string outputRoot = Path.GetFullPath(request.OutputDirectory);
      Directory.CreateDirectory(outputRoot);
      NativeStage03RunPaths paths = NativeStage03OutputPathPolicy.Create(
        outputRoot,
        document.PathName,
        Guid.NewGuid().ToString("N"),
        DateTimeOffset.Now,
        current.Mode);
      NativeStage03RawIfcArtifact raw = null;
      HifcTranslationResult translation = null;
      try
      {
        if (Directory.Exists(paths.RunDirectory)
          || File.Exists(paths.RunDirectory))
          throw new IOException("Stage03 run 目录已存在：" + paths.RunDirectory);
        Directory.CreateDirectory(paths.RunDirectory);
        Directory.CreateDirectory(paths.QuarantineDirectory);

        raw = new NativeStage03RawIfcExporter().Export(
          document,
          paths.RawIfcPath);
        translation = HifcCoreService.Translate(new HifcTranslationRequest
        {
          RawIfcPath = raw.Path,
          FinalIfcPath = paths.FinalIfcPath,
          QuarantineDirectory = paths.QuarantineDirectory,
          Fields = current.ExportFields
        });
        IReadOnlyList<NativeStage03FieldEvidence> merged = MergeFields(
          current,
          translation);
        if (!translation.Success)
        {
          NativeStage03ReportWriter.WriteFailure(
            paths,
            current,
            translation.ErrorCode,
            translation.Message,
            null,
            raw,
            translation);
          var failed = new NativeStage03ExecutionResult
          {
            Success = false,
            Status = "Stage03 H-IFC 转译失败",
            InternalValidationStatus = translation.InternalStatus,
            IfcFluxStatus = translation.IfcFluxStatus,
            ErrorCode = translation.ErrorCode,
            Message = translation.Message,
            Paths = paths,
            RawIfcSha256 = raw.Sha256,
            FinalIfcSha256 = translation.FinalIfcSha256,
            Fields = merged,
            Messages = new[]
            {
              "RAW IFC 已保留用于诊断。",
              "失败报告：" + paths.FailureReportPath,
              "candidate：" + translation.CandidateIfcPath
            }
          };
          NativeStage03SessionStore.StoreResult(current, failed);
          return failed;
        }

        NativeStage03ReportWriter.WriteSuccess(
          paths,
          current,
          raw,
          translation,
          merged);
        TryDeleteEmptyQuarantine(paths.QuarantineDirectory);
        var result = new NativeStage03ExecutionResult
        {
          Success = true,
          Status = current.Forced
            ? "Stage03 强制测试 H-IFC 已生成"
            : "Stage03 严格 H-IFC 已生成",
          InternalValidationStatus = translation.InternalStatus,
          IfcFluxStatus = translation.IfcFluxStatus,
          ErrorCode = string.Empty,
          Message = translation.Message,
          Paths = paths,
          RawIfcSha256 = raw.Sha256,
          FinalIfcSha256 = translation.FinalIfcSha256,
          Fields = merged,
          Messages = new[]
          {
            "H-IFC 已通过插件内部 exact 回读。",
            "IFCFlux 状态：" + translation.IfcFluxStatus,
            "H-IFC：" + paths.FinalIfcPath,
            "字段报告：" + paths.FieldsReportPath,
            "验收清单：" + paths.IfcFluxChecklistPath
          }
        };
        NativeStage03SessionStore.StoreResult(current, result);
        return result;
      }
      catch (Exception exception)
      {
        try
        {
          NativeStage03ReportWriter.WriteFailure(
            paths,
            current,
            "STAGE03_EXECUTION_FAILED",
            exception.Message,
            exception,
            raw,
            translation);
        }
        catch
        {
        }
        var failed = Failure(
          paths,
          "STAGE03_EXECUTION_FAILED",
          exception.Message,
          exception);
        NativeStage03SessionStore.StoreResult(current, failed);
        return failed;
      }
    }

    internal static NativeStage03ExecutionResult RevalidateFile(
      UIApplication application,
      string ifcPath)
    {
      if (application == null)
        throw new ArgumentNullException(nameof(application));
      NativeStage03ScanResult scan = NativeStage03SessionStore.GetLastScan(
        application.ActiveUIDocument?.Document?.PathName);
      if (scan == null || scan.ExportFields.Count == 0)
      {
        return Failure(
          null,
          NativeStage03Codes.ScanExpired,
          "复检前必须在当前文档完成一次 Stage03 预检。" );
      }
      try
      {
        HifcValidationResult validation = HifcCoreService.ValidateFile(
          ifcPath,
          scan.ExportFields);
        return new NativeStage03ExecutionResult
        {
          Success = validation.Success,
          Status = validation.Success
            ? "H-IFC 文件复检通过"
            : "H-IFC 文件复检失败",
          InternalValidationStatus = validation.InternalStatus,
          IfcFluxStatus = validation.IfcFluxStatus,
          ErrorCode = validation.ErrorCode,
          Message = validation.Message,
          FinalIfcSha256 = validation.IfcSha256,
          Fields = MergeValidationFields(scan, validation),
          Messages = new[]
          {
            "复检文件：" + validation.IfcPath,
            "SHA-256：" + validation.IfcSha256,
            "IFCFlux：" + validation.IfcFluxStatus
          }
        };
      }
      catch (Exception exception)
      {
        return Failure(
          null,
          HifcCoreErrorCodes.ExactValidationFailed,
          exception.Message,
          exception);
      }
    }

    internal static NativeStage03ExecutionResult GetLastResult(
      string documentPath)
    {
      return NativeStage03SessionStore.GetLastResult(documentPath);
    }

    private static IReadOnlyList<NativeStage03FieldEvidence> MergeFields(
      NativeStage03ScanResult scan,
      HifcTranslationResult translation)
    {
      var byIdentity = (translation?.Fields
          ?? Array.Empty<HifcFieldEvidence>())
        .Where(value => value != null)
        .ToDictionary(
          value => value.PropertyIdentity,
          StringComparer.Ordinal);
      return new ReadOnlyCollection<NativeStage03FieldEvidence>(scan.Fields
        .Select(field => MergeField(
          field,
          field.HifcField == null
            ? null
            : byIdentity.TryGetValue(
              field.HifcField.PropertyIdentity,
              out HifcFieldEvidence evidence)
                ? evidence
                : null,
          scan.Forced))
        .ToArray());
    }

    private static IReadOnlyList<NativeStage03FieldEvidence>
      MergeValidationFields(
      NativeStage03ScanResult scan,
      HifcValidationResult validation)
    {
      var byIdentity = (validation?.Fields
          ?? Array.Empty<HifcFieldEvidence>())
        .Where(value => value != null)
        .ToDictionary(
          value => value.PropertyIdentity,
          StringComparer.Ordinal);
      return new ReadOnlyCollection<NativeStage03FieldEvidence>(scan.Fields
        .Select(field => MergeField(
          field,
          field.HifcField == null
            ? null
            : byIdentity.TryGetValue(
              field.HifcField.PropertyIdentity,
              out HifcFieldEvidence evidence)
                ? evidence
                : null,
          scan.Forced))
        .ToArray());
    }

    private static NativeStage03FieldEvidence MergeField(
      NativeStage03FieldEvidence source,
      HifcFieldEvidence exact,
      bool forced)
    {
      string status = source.Status;
      string message = source.Message;
      string ownerResolutionStatus = source.OwnerResolutionStatus;
      if (source.HifcField != null)
      {
        if (exact == null)
        {
          status = "INTERNAL_NOT_INSPECTED";
          message = Append(message, "H-IFC exact 回读没有返回该字段。" );
        }
        else if (exact.Success)
        {
          status = "INTERNAL_PASS";
          ownerResolutionStatus = "OWNER_ENTITY_MATCH";
          message = Append(message, exact.Message);
        }
        else
        {
          status = "INTERNAL_FAIL";
          ownerResolutionStatus = exact.ErrorCode;
          message = Append(
            message,
            exact.ErrorCode + "：" + exact.Message);
        }
      }
      else if (forced && source.Active)
      {
        status = "FORCED_TEST_SKIPPED";
      }
      return new NativeStage03FieldEvidence
      {
        PropertyId = source.PropertyId,
        RoleId = source.RoleId,
        Entity = source.Entity,
        PropertySet = source.PropertySet,
        IfcProperty = source.IfcProperty,
        DeclaredIfcType = source.DeclaredIfcType,
        CanonicalUnit = source.CanonicalUnit,
        Requirement = source.Requirement,
        RuntimeStatus = source.RuntimeStatus,
        ElementId = source.ElementId,
        OwnerUniqueId = source.OwnerUniqueId,
        OwnerStrategy = source.OwnerStrategy,
        OwnerExportGuid = source.OwnerExportGuid,
        OwnerGlobalId = source.OwnerGlobalId,
        OwnerResolutionStatus = ownerResolutionStatus,
        CanonicalValue = source.CanonicalValue,
        Status = status,
        Message = message,
        Active = source.Active,
        StrictExportReady = source.StrictExportReady,
        ExportableInForcedMode = source.ExportableInForcedMode,
        HifcField = source.HifcField
      };
    }

    private static string Append(string current, string next)
    {
      if (string.IsNullOrWhiteSpace(next)) return current ?? string.Empty;
      if (string.IsNullOrWhiteSpace(current)) return next;
      return current + "｜" + next;
    }

    private static void TryDeleteEmptyQuarantine(string directory)
    {
      try
      {
        if (Directory.Exists(directory)
          && !Directory.EnumerateFileSystemEntries(directory).Any())
          Directory.Delete(directory);
      }
      catch
      {
      }
    }

    private static NativeStage03ExecutionResult Failure(
      NativeStage03RunPaths paths,
      string errorCode,
      string message,
      Exception exception = null)
    {
      return new NativeStage03ExecutionResult
      {
        Success = false,
        Status = "Stage03 执行失败",
        InternalValidationStatus = HifcCoreStatus.InternalFailed,
        IfcFluxStatus = HifcCoreStatus.IfcFluxManualPending,
        ErrorCode = errorCode ?? string.Empty,
        Message = message ?? string.Empty,
        Paths = paths,
        Messages = new[]
        {
          exception == null ? message ?? string.Empty : exception.ToString()
        }
      };
    }
  }

  internal static class NativeStage03SessionStore
  {
    private static readonly object SyncRoot = new object();
    private static readonly Dictionary<string, NativeStage03ScanResult> Scans =
      new Dictionary<string, NativeStage03ScanResult>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, NativeStage03ExecutionResult> Results =
      new Dictionary<string, NativeStage03ExecutionResult>(StringComparer.OrdinalIgnoreCase);

    internal static void StoreScan(NativeStage03ScanResult scan)
    {
      if (scan == null || string.IsNullOrWhiteSpace(scan.DocumentPath)) return;
      lock (SyncRoot) Scans[Path.GetFullPath(scan.DocumentPath)] = scan;
    }

    internal static void StoreResult(
      NativeStage03ScanResult scan,
      NativeStage03ExecutionResult result)
    {
      if (scan == null || result == null
        || string.IsNullOrWhiteSpace(scan.DocumentPath)) return;
      lock (SyncRoot)
      {
        string key = Path.GetFullPath(scan.DocumentPath);
        Scans[key] = scan;
        Results[key] = result;
      }
    }

    internal static NativeStage03ScanResult GetLastScan(string documentPath)
    {
      if (string.IsNullOrWhiteSpace(documentPath)) return null;
      lock (SyncRoot)
      {
        Scans.TryGetValue(
          Path.GetFullPath(documentPath),
          out NativeStage03ScanResult scan);
        return scan;
      }
    }

    internal static NativeStage03ExecutionResult GetLastResult(
      string documentPath)
    {
      if (string.IsNullOrWhiteSpace(documentPath)) return null;
      lock (SyncRoot)
      {
        Results.TryGetValue(
          Path.GetFullPath(documentPath),
          out NativeStage03ExecutionResult result);
        return result;
      }
    }
  }
}
