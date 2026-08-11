using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BIMBaoGui.McpContracts;

namespace BIMBaoGui.RevitAddin.McpBridge
{
  internal sealed class McpBridgeCommandRouter
  {
    private readonly BridgeSessionDescriptor _session;
    private readonly McpRevitCommandGateway _gateway;
    private readonly McpStage01Adapter _stage01;
    private readonly McpStage02Adapter _stage02;

    internal McpBridgeCommandRouter(
      BridgeSessionDescriptor session,
      McpRevitCommandGateway gateway,
      McpStage01Adapter stage01,
      McpStage02Adapter stage02)
    {
      _session = session ?? throw new ArgumentNullException(nameof(session));
      _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
      _stage01 = stage01 ?? throw new ArgumentNullException(nameof(stage01));
      _stage02 = stage02 ?? throw new ArgumentNullException(nameof(stage02));
    }

    internal async Task<BridgeResponse> RouteAsync(
      BridgeRequest request,
      CancellationToken cancellationToken)
    {
      if (request == null)
        return BridgeResponse.Failure(
          string.Empty,
          BridgeErrorCodes.InvalidArgument,
          "Bridge 请求为空。" );
      try
      {
        string payload;
        switch (request.Method)
        {
          case BridgeMethodNames.Ping:
          case BridgeMethodNames.CurrentSession:
            payload = SessionJson();
            break;
          case BridgeMethodNames.DocumentStatus:
            payload = await DocumentStatusJson(cancellationToken)
              .ConfigureAwait(false);
            break;
          case BridgeMethodNames.RulePackageIdentity:
            payload = IdentityJson();
            break;
          case BridgeMethodNames.Stage01FormSchema:
            payload = _stage01.GetFormSchemaJson();
            break;
          case BridgeMethodNames.Stage01Read:
            payload = await _stage01.ReadAsync(cancellationToken)
              .ConfigureAwait(false);
            break;
          case BridgeMethodNames.Stage01Validate:
            payload = RouteStage01Validate(request.PayloadJson);
            break;
          case BridgeMethodNames.Stage01Write:
            payload = await RouteStage01Write(
              request.PayloadJson,
              cancellationToken).ConfigureAwait(false);
            break;
          case BridgeMethodNames.Stage02Preview:
            payload = await RouteStage02Preview(
              request.PayloadJson,
              cancellationToken).ConfigureAwait(false);
            break;
          case BridgeMethodNames.Stage02Write:
            payload = await RouteStage02Write(
              request.PayloadJson,
              cancellationToken).ConfigureAwait(false);
            break;
          default:
            return BridgeResponse.Failure(
              request.RequestId,
              BridgeErrorCodes.UnknownMethod,
              "未知或未批准的 BIMBaoGui Bridge method。" );
        }
        return BridgeResponse.Ok(request.RequestId, "OK", payload);
      }
      catch (McpLeaseException exception)
      {
        return BridgeResponse.Failure(
          request.RequestId,
          exception.ErrorCode,
          exception.Message);
      }
      catch (McpCommandException exception)
      {
        return BridgeResponse.Failure(
          request.RequestId,
          exception.ErrorCode,
          exception.Message,
          exception.Status);
      }
      catch (OperationCanceledException)
      {
        return BridgeResponse.Failure(
          request.RequestId,
          BridgeErrorCodes.Timeout,
          "等待 Revit 完成操作已超时；已启动的 Revit 事务不会被后台线程强制中断。" );
      }
      catch (Exception exception)
      {
        return BridgeResponse.Failure(
          request.RequestId,
          BridgeErrorCodes.TechnicalFatal,
          "BIMBaoGui Bridge 发生技术错误：" + exception.Message);
      }
    }

    private string SessionJson()
    {
      return McpBridgeJson.Serialize(new Dictionary<string, object>(
        StringComparer.Ordinal)
      {
        ["process_id"] = _session.ProcessId,
        ["revit_version"] = _session.RevitVersion,
        ["plugin_version"] = _session.PluginVersion,
        ["rule_package_id"] = _session.RulePackageId,
        ["rule_package_version"] = _session.RulePackageVersion,
        ["rule_package_sha256"] = _session.RulePackageSha256,
        ["started_utc"] = _session.StartedUtc
      });
    }

    private string IdentityJson()
    {
      return McpBridgeJson.Serialize(new Dictionary<string, object>(
        StringComparer.Ordinal)
      {
        ["plugin_version"] = _session.PluginVersion,
        ["package_id"] = _session.RulePackageId,
        ["package_version"] = _session.RulePackageVersion,
        ["rule_package_sha256"] = _session.RulePackageSha256,
        ["bridge_protocol_version"] = _session.BridgeProtocolVersion
      });
    }

    private async Task<string> DocumentStatusJson(
      CancellationToken cancellationToken)
    {
      CurrentDocumentSnapshot snapshot = await _gateway
        .GetDocumentStatusAsync(cancellationToken).ConfigureAwait(false);
      return McpBridgeJson.Serialize(new Dictionary<string, object>(
        StringComparer.Ordinal)
      {
        ["has_document"] = snapshot != null && snapshot.HasDocument,
        ["revit_version"] = snapshot?.RevitVersion ?? string.Empty,
        ["document_title"] = snapshot?.DocumentTitle ?? string.Empty,
        ["document_path"] = snapshot?.DocumentPath ?? string.Empty,
        ["is_family_document"] = snapshot != null
          && snapshot.IsFamilyDocument,
        ["is_read_only"] = snapshot != null && snapshot.IsReadOnly,
        ["is_saved"] = snapshot != null && snapshot.IsSaved
      });
    }

    private string RouteStage01Validate(string json)
    {
      Stage01ValidatePayload payload = McpBridgeJson
        .Deserialize<Stage01ValidatePayload>(json);
      if (string.IsNullOrWhiteSpace(payload.payload_json))
      {
        throw new McpCommandException(
          BridgeErrorCodes.InvalidArgument,
          "stage01.validate 缺少 payload_json。" );
      }
      return _stage01.Validate(payload.payload_json);
    }

    private Task<string> RouteStage01Write(
      string json,
      CancellationToken cancellationToken)
    {
      Stage01WritePayload payload = McpBridgeJson
        .Deserialize<Stage01WritePayload>(json);
      if (string.IsNullOrWhiteSpace(payload.validation_hash))
      {
        throw new McpCommandException(
          BridgeErrorCodes.InvalidArgument,
          "stage01.write 缺少 validation_hash。" );
      }
      return _stage01.WriteAsync(
        payload.validation_hash,
        payload.confirm,
        payload.confirm_blank_project,
        payload.allow_reinitialize,
        cancellationToken);
    }

    private Task<string> RouteStage02Preview(
      string json,
      CancellationToken cancellationToken)
    {
      Stage02PreviewPayload payload = McpBridgeJson
        .Deserialize<Stage02PreviewPayload>(json);
      return _stage02.PreviewAsync(payload.scope, cancellationToken);
    }

    private Task<string> RouteStage02Write(
      string json,
      CancellationToken cancellationToken)
    {
      Stage02WritePayload payload = McpBridgeJson
        .Deserialize<Stage02WritePayload>(json);
      if (string.IsNullOrWhiteSpace(payload.preview_hash))
      {
        throw new McpCommandException(
          BridgeErrorCodes.InvalidArgument,
          "stage02.write 缺少 preview_hash。" );
      }
      return _stage02.WriteAsync(
        payload.preview_hash,
        payload.confirm,
        cancellationToken);
    }

    private sealed class Stage01ValidatePayload
    {
      public string payload_json { get; set; }
    }

    private sealed class Stage01WritePayload
    {
      public string validation_hash { get; set; }
      public bool confirm { get; set; }
      public bool confirm_blank_project { get; set; }
      public bool allow_reinitialize { get; set; }
    }

    private sealed class Stage02PreviewPayload
    {
      public string scope { get; set; } = "full_model";
    }

    private sealed class Stage02WritePayload
    {
      public string preview_hash { get; set; }
      public bool confirm { get; set; }
    }
  }
}
