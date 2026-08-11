using System.IO.Pipes;
using System.Text.Json;
using BIMBaoGui.McpContracts;

namespace BIMBaoGui.McpServer;

public sealed class NamedPipeBridgeService
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    WriteIndented = false
  };
  private readonly BridgeSessionLocator _locator;

  public NamedPipeBridgeService(BridgeSessionLocator locator)
  {
    _locator = locator ?? throw new ArgumentNullException(nameof(locator));
  }

  internal async Task<string> ListSessionsJsonAsync(
    CancellationToken cancellationToken)
  {
    IReadOnlyList<BridgeSessionDescriptor> candidates =
      _locator.ListCandidates();
    var sessions = new List<PublicBridgeSession>();
    foreach (BridgeSessionDescriptor descriptor in candidates)
    {
      bool reachable = await ProbeAsync(descriptor, cancellationToken)
        .ConfigureAwait(false);
      sessions.Add(new PublicBridgeSession
      {
        ProcessId = descriptor.ProcessId,
        RevitVersion = descriptor.RevitVersion,
        PluginVersion = descriptor.PluginVersion,
        RulePackageId = descriptor.RulePackageId,
        RulePackageVersion = descriptor.RulePackageVersion,
        RulePackageSha256 = descriptor.RulePackageSha256,
        StartedUtc = descriptor.StartedUtc,
        PipeReachable = reachable
      });
    }
    return JsonSerializer.Serialize(new
    {
      success = true,
      status = sessions.Count == 0
        ? BridgeErrorCodes.RevitNotConnected
        : "OK",
      sessions
    }, JsonOptions);
  }

  internal Task<string> CallPayloadAsync(
    string method,
    object payload,
    int? processId,
    int timeoutMs,
    CancellationToken cancellationToken)
  {
    BridgeSessionDescriptor session = _locator.Resolve(processId);
    string payloadJson = JsonSerializer.Serialize(payload ?? new { },
      JsonOptions);
    return CallPayloadAsync(
      session,
      method,
      payloadJson,
      timeoutMs,
      cancellationToken);
  }

  private async Task<string> CallPayloadAsync(
    BridgeSessionDescriptor session,
    string method,
    string payloadJson,
    int timeoutMs,
    CancellationToken cancellationToken)
  {
    BridgeResponse response;
    try
    {
      response = await SendAsync(
        session,
        method,
        payloadJson,
        timeoutMs,
        cancellationToken).ConfigureAwait(false);
    }
    catch (BridgeClientException exception)
    {
      return JsonSerializer.Serialize(new
      {
        success = false,
        status = "FAILED",
        error_code = exception.ErrorCode,
        message = exception.Message,
        payload = new { }
      }, JsonOptions);
    }
    catch (OperationCanceledException)
    {
      return JsonSerializer.Serialize(new
      {
        success = false,
        status = "FAILED",
        error_code = BridgeErrorCodes.Timeout,
        message = "等待 BIMBaoGui Revit Bridge 超时。",
        payload = new { }
      }, JsonOptions);
    }
    catch (Exception exception)
    {
      return JsonSerializer.Serialize(new
      {
        success = false,
        status = "FAILED",
        error_code = BridgeErrorCodes.TechnicalFatal,
        message = exception.Message,
        payload = new { }
      }, JsonOptions);
    }

    if (response.Success)
      return response.PayloadJson;
    return JsonSerializer.Serialize(new
    {
      success = false,
      status = response.Status,
      error_code = response.ErrorCode,
      message = response.Message,
      payload = ParsePayload(response.PayloadJson)
    }, JsonOptions);
  }

  internal async Task<BridgeResponse> SendAsync(
    BridgeSessionDescriptor session,
    string method,
    string payloadJson,
    int timeoutMs,
    CancellationToken cancellationToken)
  {
    if (session == null) throw new ArgumentNullException(nameof(session));
    int boundedTimeout = Math.Max(
      BridgeProtocol.MinimumTimeoutMs,
      Math.Min(timeoutMs, BridgeProtocol.MaximumTimeoutMs));
    string requestId = Guid.NewGuid().ToString("D");
    var request = new BridgeRequest
    {
      ProtocolVersion = BridgeProtocol.Version,
      RequestId = requestId,
      SessionToken = session.SessionToken,
      Method = method,
      TimeoutMs = boundedTimeout,
      PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson
    };
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
      cancellationToken);
    linked.CancelAfter(boundedTimeout + 5000);
    await using var pipe = new NamedPipeClientStream(
      ".",
      session.PipeName,
      PipeDirection.InOut,
      PipeOptions.Asynchronous);
    try
    {
      await pipe.ConnectAsync(boundedTimeout, linked.Token)
        .ConfigureAwait(false);
    }
    catch (TimeoutException exception)
    {
      throw new BridgeClientException(
        BridgeErrorCodes.Timeout,
        "连接 BIMBaoGui Revit Bridge 超时：" + exception.Message);
    }
    string requestJson = JsonSerializer.Serialize(request, JsonOptions);
    await BridgeFrameCodec.WriteJsonAsync(
      pipe,
      requestJson,
      BridgeProtocol.MaxRequestBytes,
      linked.Token).ConfigureAwait(false);
    string responseJson = await BridgeFrameCodec.ReadJsonAsync(
      pipe,
      BridgeProtocol.MaxResponseBytes,
      linked.Token).ConfigureAwait(false);
    BridgeResponse? response = JsonSerializer.Deserialize<BridgeResponse>(
      responseJson,
      JsonOptions);
    if (response == null)
    {
      throw new BridgeClientException(
        BridgeErrorCodes.TechnicalFatal,
        "Revit Bridge 返回空响应。" );
    }
    if (!string.Equals(
      response.ProtocolVersion,
      BridgeProtocol.Version,
      StringComparison.Ordinal))
    {
      throw new BridgeClientException(
        BridgeErrorCodes.ProtocolMismatch,
        "Revit Bridge 响应协议版本不一致。" );
    }
    if (!string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
    {
      throw new BridgeClientException(
        BridgeErrorCodes.TechnicalFatal,
        "Revit Bridge request_id 回读不一致。" );
    }
    return response;
  }

  private async Task<bool> ProbeAsync(
    BridgeSessionDescriptor session,
    CancellationToken cancellationToken)
  {
    try
    {
      BridgeResponse response = await SendAsync(
        session,
        BridgeMethodNames.Ping,
        "{}",
        2000,
        cancellationToken).ConfigureAwait(false);
      return response.Success;
    }
    catch
    {
      return false;
    }
  }

  private static object ParsePayload(string payloadJson)
  {
    if (string.IsNullOrWhiteSpace(payloadJson)) return new { };
    try
    {
      return JsonSerializer.Deserialize<JsonElement>(payloadJson, JsonOptions);
    }
    catch
    {
      return new { raw = payloadJson };
    }
  }
}
