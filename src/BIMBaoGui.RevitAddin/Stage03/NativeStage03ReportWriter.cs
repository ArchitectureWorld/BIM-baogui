using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using BIMBaoGui.HifcCore;
using BIMBaoGui.RevitAddin.Issues;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal static class NativeStage03ReportWriter
  {
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

    internal static string WriteScanEvidence(NativeStage03ScanResult scan)
    {
      if (scan == null) throw new ArgumentNullException(nameof(scan));
      ValidateConfirmedIdentity(scan);
      ValidateConfirmedScanHash(scan);
      string outputDirectory = Path.GetFullPath(
        scan.NormalizedOutputDirectory);
      string path = Path.Combine(outputDirectory,
        scan.ScanHash + "-stage03-scan-evidence.json");
      var report = SharedIdentity(scan, "STAGE03_SCAN", false);
      byte[] bytes = Serialize(report);
      Directory.CreateDirectory(outputDirectory);
      if (File.Exists(path))
      {
        if (File.ReadAllBytes(path).SequenceEqual(bytes)) return path;
        throw new NativeStage03ReportException(
          NativeStage03Codes.ScanEvidenceCollision,
          "同一 scan_hash 的 Stage03 scan evidence 内容不一致：" + path);
      }
      try
      {
        WriteBytes(path, bytes);
      }
      catch (IOException) when (File.Exists(path))
      {
        if (File.ReadAllBytes(path).SequenceEqual(bytes)) return path;
        throw new NativeStage03ReportException(
          NativeStage03Codes.ScanEvidenceCollision,
          "并发创建的同一 scan_hash evidence 内容不一致：" + path);
      }
      return path;
    }

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
      ValidateConfirmedIdentity(scan);
      ValidateConfirmedScanHash(scan);
      ValidateManifest(scan.OfficialAcceptanceManifest);
      if (scan.Mode == NativeStage03Mode.Strict
        && (scan.FailedCount != 0
          || scan.NotCheckedCount != 0
          || (scan.TechnicalFatalCodes?.Count ?? 0) != 0))
      {
        throw new NativeStage03ReportException(
          NativeStage03Codes.FieldNotReady,
          "Strict validation 不允许红项、未检查项或技术 blocker。" );
      }
      paths.ValidationReportPath = Path.Combine(paths.RunDirectory,
        scan.ScanHash + "-validation.json");

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
        ["checklist"] = ProjectChecklist(scan.Checklist),
        ["fields"] = fieldRows
      });

      WriteText(paths.IfcFluxChecklistPath, Checklist(
        paths,
        scan,
        raw,
        translation));

      bool normalPass = scan.Mode == NativeStage03Mode.Strict
        && translation.Success
        && string.Equals(translation.InternalStatus,
          HifcCoreStatus.InternalValidated, StringComparison.Ordinal)
        && scan.FailedCount == 0
        && scan.NotCheckedCount == 0
        && (scan.TechnicalFatalCodes?.Count ?? 0) == 0;
      Dictionary<string, object> validation = SharedIdentity(
        scan, "VALIDATION", normalPass);
      validation["schema"] = "HBR_NATIVE_STAGE03_VALIDATION_V1";
      validation["run_id"] = paths.RunId;
      validation["created_utc"] = DateTimeOffset.UtcNow.ToString("O");
      validation["execution_mode"] = scan.Mode == NativeStage03Mode.ForcedTest
        ? "FORCED_TEST"
        : "STRICT";
      validation["export_succeeded"] = translation.Success;
      validation["blockers"] = scan.Mode == NativeStage03Mode.Strict
        ? (scan.TechnicalFatalCodes ?? Array.Empty<string>())
          .Concat(scan.BusinessBlockers ?? Array.Empty<string>())
          .Distinct(StringComparer.Ordinal)
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray()
        : (scan.TechnicalFatalCodes ?? Array.Empty<string>()).ToArray();
      validation["internal_status"] = translation.InternalStatus;
      validation["ifcflux_status"] = translation.IfcFluxStatus;
      validation["force_reason"] = scan.ForceReason;
      validation["raw_ifc"] = Artifact(raw.Path, raw.Length, raw.Sha256);
      validation["hifc"] = Artifact(
        translation.FinalIfcPath,
        translation.FinalIfcLength,
        translation.FinalIfcSha256);
      validation["reports"] = new Dictionary<string, object>
      {
        ["fields"] = paths.FieldsReportPath,
        ["validation"] = paths.ValidationReportPath,
        ["ifcflux_checklist"] = paths.IfcFluxChecklistPath
      };
      validation["message"] = translation.Message;
      WriteJson(paths.ValidationReportPath, validation);
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
      Dictionary<string, object> report = scan == null
        ? new Dictionary<string, object>(StringComparer.Ordinal)
        {
          ["report_kind"] = "FAILURE",
          ["is_test_export"] = false,
          ["counts_as_normal_export_pass"] = false,
          ["official_acceptance_status"] = "PENDING",
          ["scan_hash"] = string.Empty
        }
        : SharedIdentity(scan, "FAILURE", false);
      report["schema"] = "HBR_NATIVE_STAGE03_FAILURE_V1";
      report["run_id"] = paths.RunId;
      report["created_utc"] = DateTimeOffset.UtcNow.ToString("O");
      report["internal_status"] = HifcCoreStatus.InternalFailed;
      report["ifcflux_status"] = HifcCoreStatus.IfcFluxManualPending;
      report["error_code"] = errorCode ?? string.Empty;
      report["message"] = message ?? string.Empty;
      report["exception_type"] = exception?.GetType().FullName ?? string.Empty;
      report["exception"] = exception?.ToString() ?? string.Empty;
      report["execution_mode"] = scan == null
        ? string.Empty
        : scan.Mode == NativeStage03Mode.ForcedTest
          ? "FORCED_TEST"
          : "STRICT";
      report["force_reason"] = scan?.ForceReason ?? string.Empty;
      report["business_blockers"] = scan?.BusinessBlockers?.ToArray()
        ?? Array.Empty<string>();
      report["raw_ifc"] = raw == null
        ? null
        : Artifact(raw.Path, raw.Length, raw.Sha256);
      report["candidate_ifc"] = translation?.CandidateIfcPath ?? string.Empty;
      report["final_ifc"] = translation?.FinalIfcPath ?? string.Empty;
      report["run_directory"] = paths.RunDirectory;
      WriteJson(paths.FailureReportPath, report);
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
        ["owner_export_guid"] = field.OwnerExportGuid,
        ["owner_global_id"] = field.OwnerGlobalId,
        ["owner_resolution_status"] = exact?.Success == true
          ? "OWNER_ENTITY_MATCH"
          : string.IsNullOrWhiteSpace(exact?.ErrorCode)
            ? field.OwnerResolutionStatus
            : exact.ErrorCode,
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

    private static Dictionary<string, object> SharedIdentity(
      NativeStage03ScanResult scan,
      string reportKind,
      bool countsAsNormalExportPass)
    {
      return new Dictionary<string, object>(StringComparer.Ordinal)
      {
        ["report_kind"] = reportKind,
        ["is_test_export"] = scan.Mode == NativeStage03Mode.ForcedTest,
        ["counts_as_normal_export_pass"] = countsAsNormalExportPass,
        ["official_acceptance_status"] = "PENDING",
        ["checklist_counts"] = new Dictionary<string, object>
        {
          ["passed"] = scan.PassedCount,
          ["failed"] = scan.FailedCount,
          ["warning"] = scan.WarningCount,
          ["not_checked"] = scan.NotCheckedCount
        },
        ["workflow_results"] = new Dictionary<string, object>
        {
          ["stage01"] = WorkflowResult(scan.Stage01WorkflowResult),
          ["stage02a"] = WorkflowResult(scan.Stage02AWorkflowResult),
          ["stage02b"] = WorkflowResult(scan.Stage02BWorkflowResult)
        },
        ["rule_package"] = new Dictionary<string, object>
        {
          ["id"] = scan.RulePackageId,
          ["version"] = scan.RulePackageVersion,
          ["sha256"] = scan.RulePackageSha256
        },
        ["document_fingerprint"] = scan.DocumentFingerprint,
        ["document_path"] = scan.DocumentPath,
        ["plugin_runtime"] = ProjectRuntime(scan.PluginRuntime),
        ["official_acceptance_manifest"] =
          ProjectManifest(scan.OfficialAcceptanceManifest),
        ["official_acceptance_revit_readbacks"] =
          ProjectReadbacks(scan.OfficialAcceptanceRevitReadbacks),
        ["checklist"] = ProjectChecklist(scan.Checklist),
        ["revit_version"] = scan.RevitVersion,
        ["scan_hash"] = scan.ScanHash,
        ["normalized_output_directory"] =
          scan.NormalizedOutputDirectory,
        ["preflight_hash"] = scan.PreflightHash,
        ["technical_fatals"] = scan.TechnicalFatalCodes?.ToArray()
          ?? Array.Empty<string>()
      };
    }

    private static IDictionary<string, object> WorkflowResult(
      NativeWorkflowResultEnvelope result)
    {
      return new Dictionary<string, object>
      {
        ["run_id"] = result?.RunId ?? string.Empty,
        ["result_hash"] = result?.ResultHash ?? string.Empty,
        ["input_snapshot_hash"] = result?.InputSnapshotHash ?? string.Empty
      };
    }

    private static IDictionary<string, object> ProjectRuntime(
      NativePluginRuntimeIdentity runtime)
    {
      runtime = runtime ?? new NativePluginRuntimeIdentity();
      return new Dictionary<string, object>
      {
        ["product_version"] = runtime.ProductVersion,
        ["assembly_version"] = runtime.AssemblyVersion,
        ["informational_version"] = runtime.InformationalVersion,
        ["commit_sha"] = runtime.CommitSha,
        ["addin_dll_path"] = runtime.AddinDllPath,
        ["addin_dll_sha256"] = runtime.AddinDllSha256
      };
    }

    private static IDictionary<string, object> ProjectManifest(
      NativeOfficialAcceptanceManifest manifest)
    {
      return new Dictionary<string, object>
      {
        ["schema_version"] = manifest?.SchemaVersion ?? string.Empty,
        ["sha256"] = manifest?.Sha256 ?? string.Empty,
        ["properties"] = (manifest?.Properties
            ?? Array.Empty<NativeOfficialAcceptanceManifestEntry>())
          .Where(value => value != null)
          .OrderBy(value => value.PropertyId, StringComparer.Ordinal)
          .Select(value => (object)new Dictionary<string, object>
          {
            ["property_id"] = value.PropertyId,
            ["identity"] = value.Identity,
            ["declared_ifc_type"] = value.DeclaredIfcType,
            ["canonical_unit"] = value.CanonicalUnit,
            ["parameter_guid"] = value.ParameterGuid,
            ["binding_scope"] = value.BindingScope,
            ["source_stage"] = SourceStage(value.SourceStage)
          }).ToArray()
      };
    }

    private static object[] ProjectReadbacks(
      IEnumerable<NativeOfficialAcceptancePropertyReadback> readbacks)
    {
      return (readbacks ?? Array.Empty<
          NativeOfficialAcceptancePropertyReadback>())
        .Where(value => value != null)
        .OrderBy(value => value.PropertyId, StringComparer.Ordinal)
        .Select(value => (object)new Dictionary<string, object>
        {
          ["property_id"] = value.PropertyId,
          ["source_stage"] = SourceStage(value.SourceStage),
          ["source_result_hash"] = value.SourceResultHash,
          ["values"] = (value.Values
              ?? Array.Empty<NativeOfficialAcceptanceOwnerReadback>())
            .Where(owner => owner != null)
            .OrderBy(owner => owner.ExpectedIfcGlobalId,
              StringComparer.Ordinal)
            .ThenBy(owner => owner.RevitUniqueId, StringComparer.Ordinal)
            .Select(owner => (object)new Dictionary<string, object>
            {
              ["revit_unique_id"] = owner.RevitUniqueId,
              ["expected_ifc_global_id"] = owner.ExpectedIfcGlobalId,
              ["canonical_value"] = owner.CanonicalValue
            }).ToArray()
        }).ToArray();
    }

    private static object[] ProjectChecklist(
      IEnumerable<NativeStage03ChecklistItem> checklist)
    {
      return (checklist ?? Array.Empty<NativeStage03ChecklistItem>())
        .Where(value => value != null)
        .OrderBy(value => value.CheckId, StringComparer.Ordinal)
        .Select(value => (object)new Dictionary<string, object>
        {
          ["check_id"] = value.CheckId,
          ["check_kind"] = value.CheckKind.ToString(),
          ["display_name"] = value.DisplayName,
          ["source_stage"] = SourceStage(value.SourceStage),
          ["applicable_basis"] = value.ApplicableBasis,
          ["current_value"] = value.CurrentValue,
          ["unit"] = value.Unit,
          ["status"] = value.Status.ToString(),
          ["issue_code"] = value.IssueCode,
          ["remediation_target"] = value.RemediationTarget,
          ["field_key"] = value.FieldKey,
          ["property_id"] = value.PropertyId,
          ["role_id"] = value.RoleId,
          ["rule_text"] = value.RuleText,
          ["target_key"] = value.TargetKey,
          ["element_id"] = value.ElementId,
          ["element_unique_id"] = value.ElementUniqueId,
          ["elements"] = (value.Elements
              ?? Array.Empty<NativeIssueElementReference>())
            .Where(element => element != null)
            .OrderBy(element => element.UniqueId, StringComparer.Ordinal)
            .ThenBy(element => element.ElementId)
            .Select(element => (object)new Dictionary<string, object>
            {
              ["element_id"] = element.ElementId,
              ["element_unique_id"] = element.UniqueId,
              ["element_name"] = element.ElementName,
              ["category_name"] = element.CategoryName
            }).ToArray(),
          ["official_carrier_status"] =
            value.OfficialCarrierStatus.ToString(),
          ["official_projection_carrier_id"] =
            value.OfficialProjectionCarrierId,
          ["official_carrier_probe_ref"] = value.OfficialCarrierProbeRef,
          ["official_evidence_ref"] = value.OfficialEvidenceRef
        }).ToArray();
    }

    private static string SourceStage(NativeReportingSourceStage stage)
    {
      switch (stage)
      {
        case NativeReportingSourceStage.Stage01: return "STAGE01";
        case NativeReportingSourceStage.Stage02A: return "STAGE02A";
        case NativeReportingSourceStage.Stage02B: return "STAGE02B";
        case NativeReportingSourceStage.CrossStage: return "CROSS_STAGE";
        case NativeReportingSourceStage.ExportPreparation:
          return "EXPORT_PREPARATION";
        default: return "UNKNOWN";
      }
    }

    private static void ValidateManifest(
      NativeOfficialAcceptanceManifest manifest)
    {
      if (manifest == null)
      {
        throw new NativeStage03ReportException(
          NativeStage03Codes.ReportWriterUnavailable,
          "Stage03 validation 缺少 official acceptance manifest。" );
      }
      string actual;
      try
      {
        actual = OfficialAcceptanceManifestCanonicalizer.ComputeSha256(
          NativeStage03ChecklistGenerator.ToHifcManifest(manifest));
      }
      catch (Exception exception)
      {
        throw new NativeStage03ReportException(
          NativeStage03Codes.ReportWriterUnavailable,
          "Stage03 official acceptance manifest 无法验证："
            + exception.Message);
      }
      if (!string.Equals(actual, manifest.Sha256,
        StringComparison.OrdinalIgnoreCase))
      {
        throw new NativeStage03ReportException(
          NativeStage03Codes.ReportWriterUnavailable,
          "Stage03 official acceptance manifest SHA 与定义不一致。" );
      }
    }

    private static void ValidateConfirmedScanHash(
      NativeStage03ScanResult scan)
    {
      string actual = NativeStage03Canonicalizer.ComputeHash(scan);
      if (!string.Equals(actual, scan.ScanHash,
        StringComparison.OrdinalIgnoreCase))
      {
        throw new NativeStage03ReportException(
          NativeStage03Codes.ScanExpired,
          "Stage03 confirmed scan 的 manifest、readback 或其他身份数据已变化。" );
      }
    }

    private static void ValidateConfirmedIdentity(NativeStage03ScanResult scan)
    {
      if (string.IsNullOrWhiteSpace(scan.DocumentPath)
        || !Path.IsPathRooted(scan.DocumentPath))
      {
        throw new NativeStage03ReportException(
          NativeStage03Codes.UnsavedDocument,
          "Stage03 报告要求已保存项目的绝对 document_path。" );
      }
      if (string.IsNullOrWhiteSpace(scan.NormalizedOutputDirectory)
        || !Path.IsPathRooted(scan.NormalizedOutputDirectory))
      {
        throw new NativeStage03ReportException(
          NativeStage03Codes.ReportWriterUnavailable,
          "Stage03 报告目录不是绝对路径。" );
      }
      NativePluginRuntimeIdentity expected = CaptureCurrentRuntime();
      NativePluginRuntimeIdentity actual = scan.PluginRuntime;
      if (actual == null
        || !string.Equals(expected.ProductVersion, actual.ProductVersion,
          StringComparison.Ordinal)
        || !string.Equals(expected.AssemblyVersion, actual.AssemblyVersion,
          StringComparison.Ordinal)
        || !string.Equals(expected.InformationalVersion,
          actual.InformationalVersion, StringComparison.Ordinal)
        || !string.Equals(expected.CommitSha, actual.CommitSha,
          StringComparison.Ordinal)
        || !string.Equals(expected.AddinDllPath, actual.AddinDllPath,
          StringComparison.OrdinalIgnoreCase)
        || !string.Equals(expected.AddinDllSha256, actual.AddinDllSha256,
          StringComparison.OrdinalIgnoreCase))
      {
        throw new NativeStage03ReportException(
          NativeStage03Codes.RuntimeArtifactChanged,
          "Stage03 confirmed scan 的插件运行身份与当前 DLL 不一致。" );
      }
    }

    private static NativePluginRuntimeIdentity CaptureCurrentRuntime()
    {
      Assembly assembly = typeof(NativeStage03ReportWriter).Assembly;
      string location = Path.GetFullPath(assembly.Location);
      string informational = assembly.GetCustomAttribute<
        AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? string.Empty;
      Match commit = Regex.Match(informational,
        @"(?:^|\.)sha\.([0-9a-fA-F]{40})(?:$|\.)",
        RegexOptions.CultureInvariant);
      FileVersionInfo file = FileVersionInfo.GetVersionInfo(location);
      return new NativePluginRuntimeIdentity
      {
        ProductVersion = file.ProductVersion ?? string.Empty,
        AssemblyVersion = assembly.GetName().Version?.ToString()
          ?? string.Empty,
        InformationalVersion = informational,
        CommitSha = commit.Success
          ? commit.Groups[1].Value.ToLowerInvariant()
          : string.Empty,
        AddinDllPath = location,
        AddinDllSha256 = Sha256(location)
      };
    }

    private static string Sha256(string path)
    {
      using (SHA256 algorithm = SHA256.Create())
      using (FileStream stream = File.OpenRead(path))
      {
        return string.Concat(algorithm.ComputeHash(stream).Select(value =>
          value.ToString("x2", CultureInfo.InvariantCulture)));
      }
    }

    private static byte[] Serialize(object value)
    {
      var serializer = new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 512
      };
      return Utf8.GetBytes(serializer.Serialize(value));
    }

    private static void WriteBytes(string path, byte[] bytes)
    {
      string full = Path.GetFullPath(path);
      string directory = Path.GetDirectoryName(full) ?? string.Empty;
      Directory.CreateDirectory(directory);
      string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
      File.WriteAllBytes(temporary, bytes);
      try
      {
        if (File.Exists(full))
          throw new IOException("Stage03 报告目标已存在：" + full);
        File.Move(temporary, full);
      }
      finally
      {
        if (File.Exists(temporary)) File.Delete(temporary);
      }
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

  internal sealed class NativeStage03ReportException : IOException
  {
    internal NativeStage03ReportException(string code, string message)
      : base(message)
    {
      Code = code ?? NativeStage03Codes.ReportWriterUnavailable;
    }

    internal string Code { get; }
  }
}
