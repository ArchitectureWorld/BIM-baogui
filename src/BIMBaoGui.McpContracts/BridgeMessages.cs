using System;

namespace BIMBaoGui.McpContracts
{
  public sealed class BridgeRequest
  {
    public string ProtocolVersion { get; set; } = BridgeProtocol.Version;
    public string RequestId { get; set; } = string.Empty;
    public string SessionToken { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public int TimeoutMs { get; set; } = 15000;
    public string PayloadJson { get; set; } = "{}";
  }

  public sealed class BridgeResponse
  {
    public string ProtocolVersion { get; set; } = BridgeProtocol.Version;
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";

    public static BridgeResponse Ok(
      string requestId,
      string status,
      string payloadJson)
    {
      return new BridgeResponse
      {
        RequestId = requestId ?? string.Empty,
        Success = true,
        Status = status ?? "OK",
        PayloadJson = string.IsNullOrWhiteSpace(payloadJson)
          ? "{}"
          : payloadJson
      };
    }

    public static BridgeResponse Failure(
      string requestId,
      string errorCode,
      string message,
      string status = "FAILED")
    {
      return new BridgeResponse
      {
        RequestId = requestId ?? string.Empty,
        Success = false,
        Status = status ?? "FAILED",
        ErrorCode = errorCode ?? BridgeErrorCodes.TechnicalFatal,
        Message = message ?? string.Empty,
        PayloadJson = "{}"
      };
    }
  }

  public static class BridgeMethodNames
  {
    public const string Ping = "ping";
    public const string CurrentSession = "sessions.current";
    public const string DocumentStatus = "status.document";
    public const string RulePackageIdentity = "identity.rule_package";
    public const string Stage01FormSchema = "stage01.form_schema";
    public const string Stage01Read = "stage01.read";
    public const string Stage01Validate = "stage01.validate";
    public const string Stage01Write = "stage01.write";
    public const string Stage02Preview = "stage02.preview";
    public const string Stage02Write = "stage02.write";
    public const string Stage03Scan = "stage03.scan";
    public const string Stage03Export = "stage03.export";
    public const string Stage03GetLastResult = "stage03.last_result";
    public const string Stage03RevalidateFile = "stage03.revalidate_file";
  }
}
