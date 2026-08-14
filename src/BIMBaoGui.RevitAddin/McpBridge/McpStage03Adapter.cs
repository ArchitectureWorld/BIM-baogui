using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIMBaoGui.McpContracts;
using BIMBaoGui.RevitAddin.Stage03;

namespace BIMBaoGui.RevitAddin.McpBridge
{
  internal sealed class McpStage03Adapter
  {
    private readonly McpRevitCommandGateway _gateway;
    private readonly McpLeaseStore<NativeStage03ScanResult> _scanLeases;

    internal McpStage03Adapter(
      McpRevitCommandGateway gateway,
      McpLeaseStore<NativeStage03ScanResult> scanLeases)
    {
      _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
      _scanLeases = scanLeases
        ?? throw new ArgumentNullException(nameof(scanLeases));
    }

    internal async Task<string> ScanAsync(
      string mode,
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
          ForceReason = string.Empty
        },
        cancellationToken).ConfigureAwait(false);
      if (result != null && result.AllowExport
        && !string.IsNullOrWhiteSpace(result.ScanHash))
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
        ["stage01_payload_sha256"] = result?.Stage01PayloadSha256 ?? string.Empty,
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
        ["owner_global_id"] = field.OwnerGlobalId,
        ["canonical_value"] = field.CanonicalValue,
        ["status"] = field.Status,
        ["strict_export_ready"] = field.StrictExportReady,
        ["forced_export_ready"] = field.ExportableInForcedMode,
        ["message"] = field.Message
      };
    }
  }
}
