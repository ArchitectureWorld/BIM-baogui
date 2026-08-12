using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using BIMBaoGui.HifcCore;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal static class NativeStage03ReportWriter
  {
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

    internal static void WriteSuccess(
      NativeStage03RunPaths paths,
      NativeStage03ScanResult scan,
      NativeStage03RawIfcArtifact raw,
      HifcTranslationResult translation,
      IReadOnlyList<NativeStage03FieldEvidence> fields)
    {
      if (paths == null) throw new ArgumentNullException(nameof(paths));
      if (scan == null) throw new ArgumentNullException(nameof(scan));
      if (raw == null) throw new ArgumentNullException(nameof(raw));
      if (translation == null) throw new ArgumentNullException(nameof(translation));

      var exactByIdentity = (translation.Fields
          ?? Array.Empty<HifcFieldEvidence>())
        .Where(value => value != null
          && !string.IsNullOrWhiteSpace(value.PropertyIdentity))
        .GroupBy(value => value.PropertyIdentity, StringComparer.Ordinal)
        .ToDictionary(
          group => group.Key,
          group => group.First(),
          StringComparer.Ordinal);
      var fieldRows = (fields ?? Array.Empty<NativeStage03FieldEvidence>())
        .Select(field => FieldRow(
          field,
          field.HifcField != null
            && exactByIdentity.TryGetValue(
              field.HifcField.PropertyIdentity,
              out HifcFieldEvidence exact)
                ? exact
                : null))
        .ToArray();
      WriteJson(paths.FieldsReportPath, new Dictionary<string, object>
      {
        ["schema"] = "HBR_NATIVE_STAGE03_FIELDS_V1",
        ["run_id"] = paths.RunId,
        ["scan_hash"] = scan.ScanHash,
        ["mode"] = scan.Mode.ToString(),
        ["forced"] = scan.Forced,
        ["force_reason"] = scan.ForceReason,
        ["field_count"] = fieldRows.Length,
        ["exact_field_count"] = exactByIdentity.Count,
        ["fields"] = fieldRows
      });

      WriteJson(paths.ValidationReportPath, new Dictionary<string, object>
      {
        ["schema"] = "HBR_NATIVE_STAGE03_VALIDATION_V1",
        ["product_version"] = "0.4.0",
        ["run_id"] = paths.RunId,
        ["created_utc"] = DateTimeOffset.UtcNow.ToString("O"),
        ["internal_status"] = translation.InternalStatus,
        ["ifcflux_status"] = translation.IfcFluxStatus,
        ["mode"] = scan.Mode.ToString(),
        ["forced"] = scan.Forced,
        ["force_reason"] = scan.ForceReason,
        ["scan_hash"] = scan.ScanHash,
        ["rule_package"] = new Dictionary<string, object>
        {
          ["id"] = scan.RulePackageId,
          ["version"] = scan.RulePackageVersion,
          ["sha256"] = scan.RulePackageSha256
        },
        ["document"] = new Dictionary<string, object>
        {
          ["title"] = scan.DocumentTitle,
          ["path"] = scan.DocumentPath,
          ["fingerprint"] = scan.DocumentFingerprint,
          ["stage01_payload_sha256"] = scan.Stage01PayloadSha256
        },
        ["raw_ifc"] = Artifact(raw.Path, raw.Length, raw.Sha256),
        ["hifc"] = Artifact(
          translation.FinalIfcPath,
          translation.FinalIfcLength,
          translation.FinalIfcSha256),
        ["reports"] = new Dictionary<string, object>
        {
          ["fields"] = paths.FieldsReportPath,
          ["validation"] = paths.ValidationReportPath,
          ["ifcflux_checklist"] = paths.IfcFluxChecklistPath
        },
        ["counts"] = new Dictionary<string, object>
        {
          ["scanned_fields"] = scan.Fields.Count,
          ["exported_fields"] = translation.Fields.Count,
          ["exact_pass_fields"] = translation.Fields.Count(value =>
            value != null && value.Success),
          ["exact_failed_fields"] = translation.Fields.Count(value =>
            value == null || !value.Success),
          ["business_blockers"] = scan.BusinessBlockers.Count,
          ["technical_fatals"] = scan.TechnicalFatalCodes.Count
        },
        ["technical_fatals"] = scan.TechnicalFatalCodes.ToArray(),
        ["business_blockers"] = scan.BusinessBlockers.ToArray(),
        ["message"] = translation.Message
      });

      WriteText(paths.IfcFluxChecklistPath, Checklist(
        paths,
        scan,
        raw,
        translation));
    }

    internal static void WriteFailure(
      NativeStage03RunPaths paths,
      NativeStage03ScanResult scan,
      string errorCode,
      string message,
      Exception exception = null,
      NativeStage03RawIfcArtifact raw = null,
      HifcTranslationResult translation = null)
    {
      if (paths == null || string.IsNullOrWhiteSpace(paths.FailureReportPath))
        return;
      WriteJson(paths.FailureReportPath, new Dictionary<string, object>
      {
        ["schema"] = "HBR_NATIVE_STAGE03_FAILURE_V1",
        ["product_version"] = "0.4.0",
        ["run_id"] = paths.RunId,
        ["created_utc"] = DateTimeOffset.UtcNow.ToString("O"),
        ["internal_status"] = HifcCoreStatus.InternalFailed,
        ["ifcflux_status"] = HifcCoreStatus.IfcFluxManualPending,
        ["error_code"] = errorCode ?? string.Empty,
        ["message"] = message ?? string.Empty,
        ["exception_type"] = exception?.GetType().FullName ?? string.Empty,
        ["exception"] = exception?.ToString() ?? string.Empty,
        ["scan_hash"] = scan?.ScanHash ?? string.Empty,
        ["mode"] = scan?.Mode.ToString() ?? string.Empty,
        ["forced"] = scan?.Forced ?? false,
        ["force_reason"] = scan?.ForceReason ?? string.Empty,
        ["technical_fatals"] = scan?.TechnicalFatalCodes?.ToArray()
          ?? Array.Empty<string>(),
        ["business_blockers"] = scan?.BusinessBlockers?.ToArray()
          ?? Array.Empty<string>(),
        ["raw_ifc"] = raw == null
          ? null
          : Artifact(raw.Path, raw.Length, raw.Sha256),
        ["candidate_ifc"] = translation?.CandidateIfcPath ?? string.Empty,
        ["final_ifc"] = translation?.FinalIfcPath ?? string.Empty,
        ["run_directory"] = paths.RunDirectory
      });
    }

    private static IDictionary<string, object> FieldRow(
      NativeStage03FieldEvidence field,
      HifcFieldEvidence exact)
    {
      return new Dictionary<string, object>
      {
        ["property_id"] = field.PropertyId,
        ["property_identity"] = field.HifcField?.PropertyIdentity
          ?? string.Empty,
        ["role_id"] = field.RoleId,
        ["entity"] = field.Entity,
        ["property_set"] = field.PropertySet,
        ["property"] = field.IfcProperty,
        ["declared_ifc_type"] = field.DeclaredIfcType,
        ["actual_ifc_type"] = exact?.ActualIfcType ?? string.Empty,
        ["typed_token"] = exact?.TypedToken ?? string.Empty,
        ["canonical_unit"] = field.CanonicalUnit,
        ["requirement"] = field.Requirement,
        ["runtime_status"] = field.RuntimeStatus,
        ["element_id"] = field.ElementId,
        ["owner_unique_id"] = field.OwnerUniqueId,
        ["owner_strategy"] = field.OwnerStrategy,
        ["owner_global_id"] = field.OwnerGlobalId,
        ["owner_step_id"] = exact?.OwnerId,
        ["property_set_step_id"] = exact?.PropertySetId,
        ["property_step_id"] = exact?.PropertyId,
        ["relationship_step_id"] = exact?.RelationshipId,
        ["canonical_value"] = field.CanonicalValue,
        ["status"] = field.Status,
        ["active"] = field.Active,
        ["strict_export_ready"] = field.StrictExportReady,
        ["forced_export_ready"] = field.ExportableInForcedMode,
        ["exact_success"] = exact?.Success,
        ["exact_error_code"] = exact?.ErrorCode ?? string.Empty,
        ["exact_message"] = exact?.Message ?? string.Empty,
        ["message"] = field.Message
      };
    }

    private static IDictionary<string, object> Artifact(
      string path,
      long length,
      string sha256)
    {
      return new Dictionary<string, object>
      {
        ["path"] = path ?? string.Empty,
        ["length"] = length,
        ["sha256"] = sha256 ?? string.Empty
      };
    }

    private static string Checklist(
      NativeStage03RunPaths paths,
      NativeStage03ScanResult scan,
      NativeStage03RawIfcArtifact raw,
      HifcTranslationResult translation)
    {
      var builder = new StringBuilder();
      builder.AppendLine("# IFCFlux 人工检查清单");
      builder.AppendLine();
      builder.AppendLine("## 文件与状态");
      builder.AppendLine();
      builder.AppendLine("- 内部验证：`" + translation.InternalStatus + "`");
      builder.AppendLine("- IFCFlux：`" + translation.IfcFluxStatus + "`");
      builder.AppendLine("- 模式：`" + scan.Mode + "`");
      builder.AppendLine("- 强制原因：" + (scan.Forced
        ? scan.ForceReason
        : "不适用"));
      builder.AppendLine("- H-IFC：`" + translation.FinalIfcPath + "`");
      builder.AppendLine("- H-IFC SHA-256：`"
        + translation.FinalIfcSha256 + "`");
      builder.AppendLine("- RAW IFC：`" + raw.Path + "`");
      builder.AppendLine("- RAW SHA-256：`" + raw.Sha256 + "`");
      builder.AppendLine("- 字段报告：`" + paths.FieldsReportPath + "`");
      builder.AppendLine("- 验证报告：`" + paths.ValidationReportPath + "`");
      builder.AppendLine();
      builder.AppendLine("## 在 IFCFlux 中手动核对");
      builder.AppendLine();
      builder.AppendLine("- [ ] 文件能够完整打开，未提示 STEP/IFC4 结构错误");
      builder.AppendLine("- [ ] IfcProject、IfcSite、IfcBuilding 等预期对象存在");
      builder.AppendLine("- [ ] 抽查 `fields.json` 中的 Entity / Pset / Property 路径");
      builder.AppendLine("- [ ] 抽查 expected/actual IFC 类型、typed token 和 canonical value");
      builder.AppendLine("- [ ] 检查 Owner/Pset/Property/Relationship STEP ID 与对象路径");
      builder.AppendLine("- [ ] 检查同一 Owner/Pset 没有重复关系或重复属性");
      builder.AppendLine("- [ ] 将异常截图、对象路径和字段名反馈给开发侧");
      builder.AppendLine();
      builder.AppendLine("> 插件只完成内部 exact 回读；IFCFlux 无 API，"
        + "因此当前外部状态保持 `IFCFLUX_MANUAL_PENDING`。" );
      return builder.ToString();
    }

    private static void WriteJson(string path, object value)
    {
      var serializer = new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 512
      };
      WriteText(path, serializer.Serialize(value));
    }

    private static void WriteText(string path, string text)
    {
      string full = Path.GetFullPath(path);
      string directory = Path.GetDirectoryName(full) ?? string.Empty;
      Directory.CreateDirectory(directory);
      string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
      File.WriteAllText(temporary, text ?? string.Empty, Utf8);
      if (File.Exists(full))
        throw new IOException("Stage03 报告目标已存在：" + full);
      File.Move(temporary, full);
    }
  }
}
