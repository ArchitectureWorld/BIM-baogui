using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using BIMBaoGui.McpContracts;

namespace BIMBaoGui.RevitAddin.McpBridge
{
  internal sealed class McpCommandException : Exception
  {
    internal McpCommandException(
      string errorCode,
      string message,
      string status = "FAILED")
      : base(message)
    {
      ErrorCode = string.IsNullOrWhiteSpace(errorCode)
        ? BridgeErrorCodes.TechnicalFatal
        : errorCode;
      Status = string.IsNullOrWhiteSpace(status) ? "FAILED" : status;
    }

    internal string ErrorCode { get; }
    internal string Status { get; }
  }

  internal static class McpBridgeJson
  {
    internal static string Serialize(object value)
    {
      return CreateSerializer().Serialize(value);
    }

    internal static T Deserialize<T>(string json)
    {
      string source = string.IsNullOrWhiteSpace(json) ? "{}" : json;
      try
      {
        T result = CreateSerializer().Deserialize<T>(source);
        if (ReferenceEquals(result, null))
          throw new InvalidOperationException("JSON 根对象为空。" );
        return result;
      }
      catch (Exception exception) when (
        exception is ArgumentException
        || exception is InvalidOperationException)
      {
        throw new McpCommandException(
          BridgeErrorCodes.InvalidArgument,
          "MCP 请求 JSON 无法解析：" + exception.Message);
      }
    }

    internal static BridgeRequest DeserializeRequest(string json)
    {
      return Deserialize<BridgeRequest>(json);
    }

    internal static string SerializeResponse(BridgeResponse response)
    {
      return Serialize(response ?? BridgeResponse.Failure(
        string.Empty,
        BridgeErrorCodes.TechnicalFatal,
        "Bridge 未返回响应。" ));
    }

    internal static IReadOnlyList<Dictionary<string, string>> Messages(
      IEnumerable<Tuple<string, string, string>> values)
    {
      var result = new List<Dictionary<string, string>>();
      foreach (Tuple<string, string, string> value in values
        ?? Array.Empty<Tuple<string, string, string>>())
      {
        result.Add(new Dictionary<string, string>(StringComparer.Ordinal)
        {
          ["code"] = value.Item1 ?? string.Empty,
          ["field_key"] = value.Item2 ?? string.Empty,
          ["message"] = value.Item3 ?? string.Empty
        });
      }
      return result;
    }

    private static JavaScriptSerializer CreateSerializer()
    {
      return new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 1024
      };
    }
  }
}
