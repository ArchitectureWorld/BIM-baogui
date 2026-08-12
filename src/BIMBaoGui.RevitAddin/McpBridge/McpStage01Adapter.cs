using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIMBaoGui.McpContracts;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;

namespace BIMBaoGui.RevitAddin.McpBridge
{
  internal sealed class McpStage01Adapter
  {
    private readonly McpRevitCommandGateway _gateway;
    private readonly McpLeaseStore<NativeStage01Model> _validationLeases;

    internal McpStage01Adapter(
      McpRevitCommandGateway gateway,
      McpLeaseStore<NativeStage01Model> validationLeases)
    {
      _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
      _validationLeases = validationLeases
        ?? throw new ArgumentNullException(nameof(validationLeases));
    }

    internal string GetFormSchemaJson()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      var fields = catalog.Stage01Fields
        .OrderBy(value => value.UiGroup, StringComparer.Ordinal)
        .ThenBy(value => value.FieldKey, StringComparer.Ordinal)
        .Select(value => new Dictionary<string, object>(StringComparer.Ordinal)
        {
          ["field_key"] = value.FieldKey,
          ["property_id"] = value.PropertyId,
          ["label"] = value.Label,
          ["ui_group"] = value.UiGroup,
          ["kind"] = value.Kind.ToString(),
          ["essential"] = value.Essential,
          ["required"] = NativeStage01Validator.IsRequired(value),
          ["read_only"] = value.ReadOnly,
          ["deferred"] = value.Deferred,
          ["default_strategy"] = value.DefaultStrategy,
          ["default_value"] = value.DefaultValue,
          ["allowed_values"] = value.AllowedValues,
          ["ifc_entity"] = value.IfcEntity,
          ["ifc_property_set"] = value.IfcPropertySet,
          ["ifc_property"] = value.IfcProperty,
          ["canonical_unit"] = value.CanonicalUnit,
          ["parameter_guid"] = value.ParameterGuid.HasValue
            ? value.ParameterGuid.Value.ToString("D")
            : string.Empty
        }).ToArray();
      var conditions = catalog.Conditions
        .OrderBy(value => value.ConditionId, StringComparer.Ordinal)
        .Select(value => new Dictionary<string, object>(StringComparer.Ordinal)
        {
          ["condition_id"] = value.ConditionId,
          ["display_name"] = value.DisplayName,
          ["group"] = value.Group,
          ["default_active"] = value.DefaultActive,
          ["declaration_option"] = "actual"
        }).ToList();
      conditions.Add(new Dictionary<string, object>(StringComparer.Ordinal)
      {
        ["condition_id"] =
          NativeProjectConditionDeclarationPolicy.NoneConditionId,
        ["display_name"] =
          NativeProjectConditionDeclarationPolicy.NoneDisplayName,
        ["group"] = NativeStage01ViewModel.ConditionsGroup,
        ["default_active"] = false,
        ["declaration_option"] = "none"
      });
      return McpBridgeJson.Serialize(new Dictionary<string, object>(
        StringComparer.Ordinal)
      {
        ["package_id"] = catalog.Identity.PackageId,
        ["package_version"] = catalog.Identity.PackageVersion,
        ["rule_package_sha256"] = catalog.Identity.RulePackageSha256,
        ["payload_schema_version"] =
          NativeStage01Canonicalizer.PayloadSchemaVersion,
        ["default_active_group"] = NativeStage01ViewModel.ConditionsGroup,
        ["condition_declaration"] = new Dictionary<string, object>(
          StringComparer.Ordinal)
        {
          ["required"] = true,
          ["none_condition_id"] =
            NativeProjectConditionDeclarationPolicy.NoneConditionId,
          ["none_display_name"] =
            NativeProjectConditionDeclarationPolicy.NoneDisplayName,
          ["exclusive_with_actual_conditions"] = true
        },
        ["model_profiles"] = catalog.ModelProfiles
          .Select(value => value.ProfileId)
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray(),
        ["conditions"] = conditions,
        ["fields"] = fields
      });
    }

    internal async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
      NativeStage01ReadResult result = await _gateway
        .ReadStage01Async(cancellationToken).ConfigureAwait(false);
      string payloadJson = result?.Model == null
        ? string.Empty
        : NativeStage01Canonicalizer.ToJson(result.Model);
      string payloadHash = payloadJson.Length == 0
        ? string.Empty
        : NativeStage01Canonicalizer.Sha256(payloadJson);
      return McpBridgeJson.Serialize(new Dictionary<string, object>(
        StringComparer.Ordinal)
      {
        ["success"] = result != null && result.Success,
        ["status"] = result?.Status ?? string.Empty,
        ["initialized"] = result?.StorageDecision != null
          && result.StorageDecision.IsInitialized,
        ["storage_state"] = result?.StorageDecision == null
          ? string.Empty
          : result.StorageDecision.State.ToString(),
        ["storage_error_code"] =
          result?.StorageDecision?.ErrorCode ?? string.Empty,
        ["payload_json"] = payloadJson,
        ["payload_sha256"] = payloadHash,
        ["file_guid"] = result?.Model?.GetValue(
          NativeStage01Keys.FileGuid) ?? string.Empty,
        ["workflow_version"] = result?.Model?.GetValue(
          NativeStage01Keys.WorkflowVersion) ?? string.Empty,
        ["validation_messages"] = ProjectValidation(
          result?.Validation),
        ["messages"] = result?.Messages ?? Array.Empty<string>()
      });
    }

    internal string Validate(string payloadJson)
    {
      if (!NativeStage01PayloadCodec.TryDecode(
        payloadJson,
        out NativeStage01Payload payload,
        out string error))
      {
        throw new McpCommandException(
          BridgeErrorCodes.InvalidArgument,
          error);
      }
      if (payload?.Model == null)
      {
        throw new McpCommandException(
          BridgeErrorCodes.InvalidArgument,
          "Stage01 Payload 没有可验证模型。" );
      }
      NativeStage01ValidationResult validation = NativeStage01Validator
        .Validate(payload.Model, NativeRuleCatalog.Current);
      string canonicalPayload = NativeStage01Canonicalizer.ToJson(payload.Model);
      string validationHash = NativeStage01Canonicalizer.Sha256(
        canonicalPayload);
      if (validation.IsValid)
        _validationLeases.Create(validationHash, payload.Model.Clone());
      return McpBridgeJson.Serialize(new Dictionary<string, object>(
        StringComparer.Ordinal)
      {
        ["valid"] = validation.IsValid,
        ["validation_hash"] = validation.IsValid
          ? validationHash
          : string.Empty,
        ["canonical_payload_json"] = canonicalPayload,
        ["lease_minutes"] = validation.IsValid ? 30 : 0,
        ["messages"] = ProjectValidation(validation)
      });
    }

    internal async Task<string> WriteAsync(
      string validationHash,
      bool confirm,
      bool confirmBlankProject,
      bool allowReinitialize,
      CancellationToken cancellationToken)
    {
      if (!confirm)
      {
        throw new McpCommandException(
          BridgeErrorCodes.ConfirmationRequired,
          "Stage01 写入必须明确设置 confirm=true。" );
      }
      NativeStage01Model model = _validationLeases
        .Consume(validationHash)
        .Clone();
      NativeStage01WriteResult result = await _gateway.WriteStage01Async(
        new NativeStage01WriteRequest
        {
          Model = model,
          ConfirmBlankProject = confirmBlankProject,
          AllowReinitialize = allowReinitialize
        },
        cancellationToken).ConfigureAwait(false);
      return McpBridgeJson.Serialize(new Dictionary<string, object>(
        StringComparer.Ordinal)
      {
        ["success"] = result != null && result.Success,
        ["status"] = result?.Status ?? string.Empty,
        ["payload_json"] = result?.PayloadJson ?? string.Empty,
        ["payload_sha256"] = result?.PayloadHash ?? string.Empty,
        ["failure_report_path"] = result?.FailureReportPath ?? string.Empty,
        ["blockers"] = (result?.Blockers
          ?? Array.Empty<NativeStage01PreflightBlocker>())
          .Select(value => new Dictionary<string, string>(
            StringComparer.Ordinal)
          {
            ["code"] = value.Code,
            ["message"] = value.Message
          }).ToArray(),
        ["messages"] = result?.Messages ?? Array.Empty<string>()
      });
    }

    private static object[] ProjectValidation(
      NativeStage01ValidationResult validation)
    {
      return (validation?.Messages
        ?? Array.Empty<NativeStage01ValidationMessage>())
        .Select(value => new Dictionary<string, string>(
          StringComparer.Ordinal)
        {
          ["code"] = value.Code,
          ["field_key"] = value.FieldKey,
          ["message"] = value.Message
        }).Cast<object>().ToArray();
    }
  }
}
