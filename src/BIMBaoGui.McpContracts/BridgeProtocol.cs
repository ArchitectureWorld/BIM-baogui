using System;

namespace BIMBaoGui.McpContracts
{
  public static class BridgeProtocol
  {
    public const string Version = "1.0";
    public const string McpProtocolVersion = "2025-11-25";
    public const int HeaderBytes = 4;
    public const int MaxRequestBytes = 8 * 1024 * 1024;
    public const int MaxResponseBytes = 32 * 1024 * 1024;
    public const int MinimumTimeoutMs = 1000;
    public const int MaximumTimeoutMs = 10 * 60 * 1000;
  }

  public static class BridgeErrorCodes
  {
    public const string RevitNotConnected = "REVIT_NOT_CONNECTED";
    public const string MultipleRevitSessions = "MULTIPLE_REVIT_SESSIONS";
    public const string RevitSessionNotFound = "REVIT_SESSION_NOT_FOUND";
    public const string ProtocolMismatch = "BRIDGE_PROTOCOL_MISMATCH";
    public const string AuthenticationFailed = "BRIDGE_AUTH_FAILED";
    public const string MessageTooLarge = "BRIDGE_MESSAGE_TOO_LARGE";
    public const string InvalidFrame = "BRIDGE_INVALID_FRAME";
    public const string InvalidUtf8 = "BRIDGE_INVALID_UTF8";
    public const string Timeout = "BRIDGE_TIMEOUT";
    public const string Busy = "BRIDGE_BUSY";
    public const string UnknownMethod = "UNKNOWN_METHOD";
    public const string InvalidArgument = "INVALID_ARGUMENT";
    public const string ConfirmationRequired = "CONFIRMATION_REQUIRED";
    public const string LeaseNotFound = "LEASE_NOT_FOUND";
    public const string LeaseExpired = "LEASE_EXPIRED";
    public const string StaleResult = "STALE_RESULT";
    public const string BusinessBlocker = "BUSINESS_BLOCKER";
    public const string TechnicalFatal = "TECHNICAL_FATAL";
    public const string PartialSuccess = "PARTIAL_SUCCESS";
  }

  public sealed class BridgeProtocolException : Exception
  {
    public BridgeProtocolException(string errorCode, string message)
      : base(message)
    {
      ErrorCode = string.IsNullOrWhiteSpace(errorCode)
        ? BridgeErrorCodes.InvalidFrame
        : errorCode;
    }

    public BridgeProtocolException(
      string errorCode,
      string message,
      Exception innerException)
      : base(message, innerException)
    {
      ErrorCode = string.IsNullOrWhiteSpace(errorCode)
        ? BridgeErrorCodes.InvalidFrame
        : errorCode;
    }

    public string ErrorCode { get; }
  }
}
