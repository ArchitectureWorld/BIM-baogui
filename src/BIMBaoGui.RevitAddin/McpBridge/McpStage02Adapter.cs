using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIMBaoGui.McpContracts;
using BIMBaoGui.RevitAddin.Stage02;

namespace BIMBaoGui.RevitAddin.McpBridge
{
  internal sealed class Stage02PreviewLease
  {
    internal NativeStage02Preview Preview { get; set; }
    internal NativeStage02PreviewRequest ResolvedRequest { get; set; }
  }

  internal sealed class McpStage02Adapter
  {
    private readonly McpRevitCommandGateway _gateway;
    private readonly McpLeaseStore<Stage02PreviewLease> _previewLeases;

    internal McpStage02Adapter(
      McpRevitCommandGateway gateway,
      McpLeaseStore<Stage02PreviewLease> previewLeases)
    {
      _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
      _previewLeases = previewLeases
        ?? throw new ArgumentNullException(nameof(previewLeases));
    }

    internal async Task<string> PreviewAsync(
      string scope,
      CancellationToken cancellationToken)
    {
      NativeStage02ScopeMode mode;
      switch ((scope ?? string.Empty).Trim().ToLowerInvariant())
      {
        case "full_model":
          mode = NativeStage02ScopeMode.FullModel;
          break;
        case "current_selection":
          mode = NativeStage02ScopeMode.CustomSelection;
          break;
        default:
          throw new McpCommandException(
            BridgeErrorCodes.InvalidArgument,
            "Stage02 scope 必须为 full_model 或 current_selection。" );
      }
      NativeStage02RevitPreviewResult result = await _gateway
        .PreviewStage02Async(
          new NativeStage02PreviewRequest
          {
            ScopeMode = mode,
            CustomUniqueIds = Array.Empty<string>()
          },
          cancellationToken).ConfigureAwait(false);
      if (result?.Success == true
        && result.Preview != null
        && result.ResolvedRequest != null)
      {
        _previewLeases.Create(
          result.Preview.PreviewHash,
          new Stage02PreviewLease
          {
            Preview = result.Preview,
            ResolvedRequest = result.ResolvedRequest.Clone()
          });
      }
      return McpBridgeJson.Serialize(ProjectPreview(result));
    }

    internal async Task<string> WriteAsync(
      string previewHash,
      bool confirm,
      CancellationToken cancellationToken)
    {
      if (!confirm)
      {
        throw new McpCommandException(
          BridgeErrorCodes.ConfirmationRequired,
          "Stage02 写入必须明确设置 confirm=true。" );
      }
      Stage02PreviewLease lease = _previewLeases.Consume(previewHash);
      NativeStage02WriteResult result = await _gateway.WriteStage02Async(
        new NativeStage02WriteRequest
        {
          Preview = lease.Preview,
          ResolvedRequest = lease.ResolvedRequest.Clone()
        },
        cancellationToken).ConfigureAwait(false);
      return McpBridgeJson.Serialize(new Dictionary<string, object>(
        StringComparer.Ordinal)
      {
        ["success"] = result != null && result.Success,
        ["partial_success"] = result != null && result.PartialSuccess,
        ["requires_new_preview"] = result != null
          && result.RequiresNewPreview,
        ["status"] = result?.Status ?? string.Empty,
        ["prepared_parameter_count"] =
          result?.PreparedParameterCount ?? 0,
        ["written_element_count"] = result?.WrittenElementCount ?? 0,
        ["failed_parameter_count"] = result?.FailedParameterCount ?? 0,
        ["failed_element_count"] = result?.FailedElementCount ?? 0,
        ["assigned_element_count"] = result?.AssignedElementCount ?? 0,
        ["removed_assignment_count"] = result?.RemovedAssignmentCount ?? 0,
        ["failed_assignment_count"] = result?.FailedAssignmentCount ?? 0,
        ["refreshed_preview_hash"] =
          result?.RefreshedPreview?.PreviewHash ?? string.Empty,
        ["messages"] = result?.Messages ?? Array.Empty<string>()
      });
    }

    private static Dictionary<string, object> ProjectPreview(
      NativeStage02RevitPreviewResult result)
    {
      NativeStage02Preview preview = result?.Preview;
      var response = new Dictionary<string, object>(StringComparer.Ordinal)
      {
        ["success"] = result != null && result.Success,
        ["status"] = result?.Status ?? string.Empty,
        ["messages"] = result?.Messages ?? Array.Empty<string>(),
        ["preview_hash"] = preview?.PreviewHash ?? string.Empty,
        ["lease_minutes"] = preview == null ? 0 : 30,
        ["rule_package_id"] = preview?.RulePackageId ?? string.Empty,
        ["rule_package_version"] =
          preview?.RulePackageVersion ?? string.Empty,
        ["rule_package_sha256"] =
          preview?.RulePackageSha256 ?? string.Empty,
        ["document_fingerprint"] =
          preview?.DocumentFingerprint ?? string.Empty,
        ["model_profile"] = preview?.ModelProfile ?? string.Empty,
        ["blocked_element_count"] = preview?.BlockedElementCount ?? 0,
        ["actionable_element_count"] =
          preview?.ActionableElementCount ?? 0,
        ["correct_field_count"] = preview?.CorrectFieldCount ?? 0,
        ["pending_binding_field_count"] =
          preview?.PendingBindingFieldCount ?? 0,
        ["pending_write_field_count"] =
          preview?.PendingWriteFieldCount ?? 0,
        ["pending_input_field_count"] =
          preview?.PendingInputFieldCount ?? 0,
        ["runtime_blocked_field_count"] =
          preview?.RuntimeBlockedFieldCount ?? 0
      };
      response["elements"] = (preview?.Elements
        ?? Array.Empty<NativeStage02ElementPlan>())
        .Select(element => new Dictionary<string, object>(StringComparer.Ordinal)
        {
          ["unique_id"] = element.Element.UniqueId,
          ["element_id"] = element.Element.ElementId,
          ["name"] = element.Element.ElementName,
          ["category"] = element.Element.Category,
          ["element_kind"] = element.Element.ElementKind,
          ["family_name"] = element.Element.FamilyName,
          ["type_name"] = element.Element.TypeName,
          ["level_name"] = element.Element.LevelName,
          ["role_match_status"] = element.RoleMatchStatus.ToString(),
          ["role_id"] = element.RoleId,
          ["role_match_source"] = element.RoleMatchSource,
          ["blocked"] = element.IsBlocked,
          ["message"] = element.Message,
          ["fields"] = element.Fields.Select(field =>
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
              ["property_id"] = field.Property.PropertyId,
              ["ifc_entity"] = field.Property.IfcEntity,
              ["property_set"] = field.Property.IfcPropertySet,
              ["property"] = field.Property.IfcProperty,
              ["parameter_guid"] =
                field.Property.ParameterGuid.ToString("D"),
              ["status"] = field.Status.ToString(),
              ["binding_action"] = field.BindingAction.ToString(),
              ["value_action"] = field.ValueAction.ToString(),
              ["current_value"] = field.CurrentCanonicalValue,
              ["proposed_value"] = field.ProposedCanonicalValue,
              ["value_source"] = field.ValueSource,
              ["strict_export_ready"] = field.StrictExportReady,
              ["message"] = field.Message
            }).ToArray()
        }).ToArray();
      return response;
    }
  }
}
