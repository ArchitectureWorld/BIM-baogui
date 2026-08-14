using System;

namespace BIMBaoGui.McpContracts
{
  public sealed class BridgeSessionDescriptor
  {
    public string BridgeProtocolVersion { get; set; } = BridgeProtocol.Version;
    public int ProcessId { get; set; }
    public string PipeName { get; set; } = string.Empty;
    public string SessionToken { get; set; } = string.Empty;
    public string RevitVersion { get; set; } = string.Empty;
    public string PluginVersion { get; set; } = string.Empty;
    public string RulePackageId { get; set; } = string.Empty;
    public string RulePackageVersion { get; set; } = string.Empty;
    public string RulePackageSha256 { get; set; } = string.Empty;
    public string StartedUtc { get; set; } = string.Empty;
    public string DiscoveryPath { get; set; } = string.Empty;
  }

  public sealed class PublicBridgeSession
  {
    public int ProcessId { get; set; }
    public string RevitVersion { get; set; } = string.Empty;
    public string PluginVersion { get; set; } = string.Empty;
    public string RulePackageId { get; set; } = string.Empty;
    public string RulePackageVersion { get; set; } = string.Empty;
    public string RulePackageSha256 { get; set; } = string.Empty;
    public string StartedUtc { get; set; } = string.Empty;
    public bool PipeReachable { get; set; }
  }
}
