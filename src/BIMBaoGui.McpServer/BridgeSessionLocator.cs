using System.Diagnostics;
using System.Text.Json;
using BIMBaoGui.McpContracts;

namespace BIMBaoGui.McpServer;

internal sealed class BridgeClientException : Exception
{
  internal BridgeClientException(string errorCode, string message)
    : base(message)
  {
    ErrorCode = errorCode;
  }

  internal string ErrorCode { get; }
}

internal sealed class BridgeSessionLocator
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  internal string DiscoveryDirectory => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "BIMBaoGui",
    "Revit2020",
    "bridges");

  internal IReadOnlyList<BridgeSessionDescriptor> ListCandidates()
  {
    if (!Directory.Exists(DiscoveryDirectory))
      return Array.Empty<BridgeSessionDescriptor>();
    var result = new List<BridgeSessionDescriptor>();
    foreach (string path in Directory.GetFiles(
      DiscoveryDirectory,
      "*.json",
      SearchOption.TopDirectoryOnly).OrderBy(value => value, StringComparer.Ordinal))
    {
      BridgeSessionDescriptor? descriptor = null;
      try
      {
        descriptor = JsonSerializer.Deserialize<BridgeSessionDescriptor>(
          File.ReadAllText(path),
          JsonOptions);
      }
      catch
      {
      }
      if (descriptor == null
        || descriptor.ProcessId <= 0
        || string.IsNullOrWhiteSpace(descriptor.PipeName)
        || string.IsNullOrWhiteSpace(descriptor.SessionToken)
        || !string.Equals(
          descriptor.BridgeProtocolVersion,
          BridgeProtocol.Version,
          StringComparison.Ordinal)
        || !string.Equals(
          descriptor.RevitVersion,
          "2020",
          StringComparison.Ordinal)
        || !IsProcessAlive(descriptor.ProcessId))
      {
        DeleteStale(path);
        continue;
      }
      descriptor.DiscoveryPath = path;
      result.Add(descriptor);
    }
    return result
      .GroupBy(value => value.ProcessId)
      .Select(group => group.OrderByDescending(value => value.StartedUtc,
        StringComparer.Ordinal).First())
      .OrderBy(value => value.ProcessId)
      .ToArray();
  }

  internal BridgeSessionDescriptor Resolve(int? processId)
  {
    IReadOnlyList<BridgeSessionDescriptor> sessions = ListCandidates();
    if (processId.HasValue)
    {
      BridgeSessionDescriptor? match = sessions.FirstOrDefault(value =>
        value.ProcessId == processId.Value);
      return match ?? throw new BridgeClientException(
        BridgeErrorCodes.RevitSessionNotFound,
        "找不到指定 processId 的 Revit 2020 BIMBaoGui Bridge。" );
    }
    if (sessions.Count == 0)
    {
      throw new BridgeClientException(
        BridgeErrorCodes.RevitNotConnected,
        "未发现正在运行的 Revit 2020 BIMBaoGui Bridge。" );
    }
    if (sessions.Count > 1)
    {
      throw new BridgeClientException(
        BridgeErrorCodes.MultipleRevitSessions,
        "检测到多个 Revit 2020 会话；请明确提供 revit_process_id。" );
    }
    return sessions[0];
  }

  private static bool IsProcessAlive(int processId)
  {
    try
    {
      using Process process = Process.GetProcessById(processId);
      return !process.HasExited;
    }
    catch
    {
      return false;
    }
  }

  private static void DeleteStale(string path)
  {
    try { File.Delete(path); }
    catch { }
  }
}
