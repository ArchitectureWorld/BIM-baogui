using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIMBaoGui.McpContracts;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage03;

namespace BIMBaoGui.RevitAddin.McpBridge
{
  internal interface IMcpStage03Gateway
  {
    Task<NativeStage03ScanResult> ScanStage03Async(
      NativeStage03ScanRequest request,
      CancellationToken cancellationToken);
    Task<NativeStage03ExecutionResult> ExportStage03Async(
      NativeStage03ExportRequest request,
      CancellationToken cancellationToken);
    Task<CurrentDocumentSnapshot> GetDocumentStatusAsync(
      CancellationToken cancellationToken);
    Task<NativeStage03ExecutionResult> RevalidateStage03Async(
      string ifcPath,
      CancellationToken cancellationToken);
  }

  internal sealed class McpStage03Adapter
  {
    private readonly IMcpStage03Gateway _gateway;
    private readonly McpLeaseStore<NativeStage03ScanResult> _scanLeases;

    internal McpStage03Adapter(
      IMcpStage03Gateway gateway,
      McpLeaseStore<NativeStage03ScanResult> scanLeases)
    {
      _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
      _scanLeases = scanLeases
        ?? throw new ArgumentNullException(nameof(scanLeases));
    }

    internal async Task<string> ScanAsync(
      string mode,
      string forceReason,
      string outputDirectory,
      CancellationToken cancellationToken)
    {
      NativeStage03Mode parsedMode;
      switch ((mode ?? string.Empty).Trim().ToLowerInvariant())
      {
        case "":
        case "strict":
          parsedMode = NativeStage03Mode.Strict;
          break;
        case "forced_test":
        case "forced":
          parsedMode = NativeStage03Mode.ForcedTest;
          break;
        default:
          throw new McpCommandException(
            BridgeErrorCodes.InvalidArgument,
            "Stage03 mode 必须为 strict 或 forced_test。" );
      }
      NativeStage03ScanResult result = await _gateway.ScanStage03Async(
        new NativeStage03ScanRequest
        {
          Mode = parsedMode,
          ForceReason = forceReason ?? string.Empty,
          OutputDirectory = outputDirectory ?? string.Empty
        },
        cancellationToken).ConfigureAwait(false);
      if (ShouldCreateLease(result))
      {
        _scanLeases.Create(result.ScanHash, result);
      }
      return McpBridgeJson.Serialize(ProjectScan(result));
    }

    internal async Task<string> ExportAsync(
      string scanHash,
      bool confirm,
      string outputDirectory,
      CancellationToken cancellationToken)
    {
      if (!confirm)
      {
        throw new McpCommandException(
          BridgeErrorCodes.ConfirmationRequired,
          "Stage03 导出必须明确设置 confirm=true。" );
      }
      if (string.IsNullOrWhiteSpace(outputDirectory))
      {
        throw new McpCommandException(
          BridgeErrorCodes.InvalidArgument,
          "Stage03 导出缺少 output_directory。" );
      }
      NativeStage03ScanResult lease = _scanLeases.Consume(scanHash);
      NativeStage03ExecutionResult result = await _gateway.ExportStage03Async(
        new NativeStage03ExportRequest
        {
          ConfirmedScan = lease,
          OutputDirectory = outputDirectory
        },
        cancellationToken).ConfigureAwait(false);
      return McpBridgeJson.Serialize(ProjectExecution(result));
    }

    internal async Task<string> GetLastResultAsync(
      CancellationToken cancellationToken)
    {
      CurrentDocumentSnapshot document = await _gateway
        .GetDocumentStatusAsync(cancellationToken).ConfigureAwait(false);
      NativeStage03ExecutionResult result = document == null
        ? null
        : NativeStage03WorkflowService.GetLastResult(document.DocumentPath);
      return McpBridgeJson.Serialize(ProjectExecution(result));
    }

    internal async Task<string> RevalidateAsync(
      string ifcPath,
      CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(ifcPath))
      {
        throw new McpCommandException(
          BridgeErrorCodes.InvalidArgument,
          "Stage03 文件复检缺少 ifc_path。" );
      }
      NativeStage03ExecutionResult result = await _gateway
        .RevalidateStage03Async(ifcPath, cancellationToken)
        .ConfigureAwait(false);
      return McpBridgeJson.Serialize(ProjectExecution(result));
    }

    private static Dictionary<string, object> ProjectScan(
      NativeStage03ScanResult result)
    {
      return new Dictionary<string, object>(StringComparer.Ordinal)
      {
        ["success"] = result != null && result.Success,
        ["status"] = result?.Status ?? string.Empty,
        ["mode"] = result?.Mode.ToString() ?? string.Empty,
        ["forced"] = result != null && result.Forced,
        ["force_reason"] = result?.ForceReason ?? string.Empty,
        ["allow_export"] = result != null && result.AllowExport,
        ["scan_hash"] = result?.ScanHash ?? string.Empty,
        ["lease_minutes"] = result?.AllowExport == true ? 30 : 0,
        ["rule_package_id"] = result?.RulePackageId ?? string.Empty,
        ["rule_package_version"] = result?.RulePackageVersion ?? string.Empty,
        ["rule_package_sha256"] = result?.RulePackageSha256 ?? string.Empty,
        ["document_fingerprint"] = result?.DocumentFingerprint ?? string.Empty,
        ["document_path"] = result?.DocumentPath ?? string.Empty,
        ["normalized_output_directory"] =
          result?.NormalizedOutputDirectory ?? string.Empty,
        ["preflight_hash"] = result?.PreflightHash ?? string.Empty,
        ["stage01_payload_sha256"] = result?.Stage01PayloadSha256 ?? string.Empty,
        ["official_acceptance_status"] = "PENDING",
        ["official_acceptance_manifest"] =
          ProjectManifest(result?.OfficialAcceptanceManifest),
        ["official_acceptance_revit_readbacks"] =
          ProjectReadbacks(result?.OfficialAcceptanceRevitReadbacks),
        ["checklist_counts"] = new Dictionary<string, object>
        {
          ["passed"] = result?.PassedCount ?? 0,
          ["failed"] = result?.FailedCount ?? 0,
          ["warning"] = result?.WarningCount ?? 0,
          ["not_checked"] = result?.NotCheckedCount ?? 0
        },
        ["checklist"] = ProjectChecklist(result?.Checklist),
        ["technical_fatals"] = result?.TechnicalFatalCodes
          ?? Array.Empty<string>(),
        ["business_blockers"] = result?.BusinessBlockers
          ?? Array.Empty<string>(),
        ["messages"] = result?.Messages ?? Array.Empty<string>(),
        ["export_field_count"] = result?.ExportFields.Count ?? 0,
        ["fields"] = (result?.Fields
          ?? Array.Empty<NativeStage03FieldEvidence>())
          .Select(ProjectField).ToArray()
      };
    }

    private static Dictionary<string, object> ProjectExecution(
      NativeStage03ExecutionResult result)
    {
      return new Dictionary<string, object>(StringComparer.Ordinal)
      {
        ["available"] = result != null,
        ["success"] = result != null && result.Success,
        ["status"] = result?.Status ?? string.Empty,
        ["internal_status"] = result?.InternalValidationStatus ?? string.Empty,
        ["ifcflux_status"] = result?.IfcFluxStatus ?? string.Empty,
        ["error_code"] = result?.ErrorCode ?? string.Empty,
        ["message"] = result?.Message ?? string.Empty,
        ["raw_ifc_sha256"] = result?.RawIfcSha256 ?? string.Empty,
        ["final_ifc_sha256"] = result?.FinalIfcSha256 ?? string.Empty,
        ["is_test_export"] = result != null && result.IsTestExport,
        ["counts_as_normal_export_pass"] = result != null
          && result.CountsAsNormalExportPass,
        ["official_acceptance_status"] =
          result?.OfficialAcceptanceStatus ?? "PENDING",
        ["official_acceptance_manifest"] =
          ProjectManifest(result?.OfficialAcceptanceManifest),
        ["official_acceptance_revit_readbacks"] =
          ProjectReadbacks(result?.OfficialAcceptanceRevitReadbacks),
        ["checklist_counts"] = ChecklistCounts(result?.Checklist),
        ["checklist"] = ProjectChecklist(result?.Checklist),
        ["paths"] = result?.Paths == null
          ? null
          : new Dictionary<string, object>(StringComparer.Ordinal)
          {
            ["run_directory"] = result.Paths.RunDirectory,
            ["raw_ifc"] = result.Paths.RawIfcPath,
            ["hifc"] = result.Paths.FinalIfcPath,
            ["fields_report"] = result.Paths.FieldsReportPath,
            ["validation_report"] = result.Paths.ValidationReportPath,
            ["failure_report"] = result.Paths.FailureReportPath,
            ["ifcflux_checklist"] = result.Paths.IfcFluxChecklistPath
          },
        ["messages"] = result?.Messages ?? Array.Empty<string>(),
        ["fields"] = (result?.Fields
          ?? Array.Empty<NativeStage03FieldEvidence>())
          .Select(ProjectField).ToArray()
      };
    }

    private static bool ShouldCreateLease(NativeStage03ScanResult result)
    {
      return result != null
        && result.AllowExport
        && !string.IsNullOrWhiteSpace(result.ScanHash)
        && (result.TechnicalFatalCodes?.Count ?? 0) == 0
        && (result.Mode != NativeStage03Mode.ForcedTest
          || !string.IsNullOrWhiteSpace(result.ForceReason));
    }

    private static Dictionary<string, object> ChecklistCounts(
      IEnumerable<NativeStage03ChecklistItem> checklist)
    {
      NativeStage03ChecklistItem[] values = (checklist
          ?? Array.Empty<NativeStage03ChecklistItem>())
        .Where(value => value != null)
        .ToArray();
      return new Dictionary<string, object>
      {
        ["passed"] = values.Count(value =>
          value.Status == NativeStage03ChecklistStatus.Passed),
        ["failed"] = values.Count(value =>
          value.Status == NativeStage03ChecklistStatus.Failed),
        ["warning"] = values.Count(value =>
          value.Status == NativeStage03ChecklistStatus.Warning),
        ["not_checked"] = values.Count(value =>
          value.Status == NativeStage03ChecklistStatus.NotChecked)
      };
    }

    private static Dictionary<string, object> ProjectManifest(
      NativeOfficialAcceptanceManifest manifest)
    {
      return new Dictionary<string, object>(StringComparer.Ordinal)
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

    private static Dictionary<string, object> ProjectField(
      NativeStage03FieldEvidence field)
    {
      return new Dictionary<string, object>(StringComparer.Ordinal)
      {
        ["property_id"] = field.PropertyId,
        ["role_id"] = field.RoleId,
        ["entity"] = field.Entity,
        ["property_set"] = field.PropertySet,
        ["property"] = field.IfcProperty,
        ["declared_ifc_type"] = field.DeclaredIfcType,
        ["canonical_unit"] = field.CanonicalUnit,
        ["requirement"] = field.Requirement,
        ["runtime_status"] = field.RuntimeStatus,
        ["element_id"] = field.ElementId,
        ["owner_unique_id"] = field.OwnerUniqueId,
        ["owner_strategy"] = field.OwnerStrategy,
        ["owner_export_guid"] = field.OwnerExportGuid,
        ["owner_global_id"] = field.OwnerGlobalId,
        ["owner_resolution_status"] = field.OwnerResolutionStatus,
        ["canonical_value"] = field.CanonicalValue,
        ["status"] = field.Status,
        ["strict_export_ready"] = field.StrictExportReady,
        ["forced_export_ready"] = field.ExportableInForcedMode,
        ["message"] = field.Message
      };
    }
  }
}
