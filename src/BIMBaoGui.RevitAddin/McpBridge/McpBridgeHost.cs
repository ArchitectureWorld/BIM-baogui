using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BIMBaoGui.McpContracts;
using BIMBaoGui.RevitAddin.Stage01;
using BIMBaoGui.RevitAddin.Stage02;
using BIMBaoGui.RevitAddin.Stage03;

namespace BIMBaoGui.RevitAddin.McpBridge
{
  internal static class McpBridgeHost
  {
    private static readonly object SyncRoot = new object();
    private static McpNamedPipeServer _server;
    private static McpBridgeDiscoveryWriter _discovery;
    private static McpLeaseStore<NativeStage01Model> _stage01Leases;
    private static McpLeaseStore<Stage02PreviewLease> _stage02Leases;
    private static McpLeaseStore<NativeStage03ScanResult> _stage03Leases;
    private static string _startupFailure = string.Empty;

    internal static string PipeName { get; private set; } = string.Empty;
    internal static string SessionToken { get; private set; } = string.Empty;
    internal static BridgeSessionDescriptor Session { get; private set; }

    internal static void Start()
    {
      lock (SyncRoot)
      {
        if (_server != null) return;
        RevitExternalEventDispatcher.EnsureInitialized();
        int processId = Process.GetCurrentProcess().Id;
        SessionToken = CreateRandomHex(32);
        PipeName = "BIMBaoGui.Revit2020."
          + processId.ToString(CultureInfo.InvariantCulture)
          + "."
          + CreateRandomHex(8);
        RulePackageIdentity identity = RulePackageIdentityReader.ReadEmbedded();
        string pluginVersion = Assembly.GetExecutingAssembly()
          .GetName().Version?.ToString() ?? "0.4.0";
        Session = new BridgeSessionDescriptor
        {
          BridgeProtocolVersion = BridgeProtocol.Version,
          ProcessId = processId,
          PipeName = PipeName,
          SessionToken = SessionToken,
          RevitVersion = "2020",
          PluginVersion = pluginVersion,
          RulePackageId = identity.PackageId,
          RulePackageVersion = identity.PackageVersion,
          RulePackageSha256 = identity.RulePackageSha256,
          StartedUtc = DateTimeOffset.UtcNow.ToString(
            "O",
            CultureInfo.InvariantCulture)
        };
        var gateway = new McpRevitCommandGateway();
        _stage01Leases = new McpLeaseStore<NativeStage01Model>(
          SystemMcpClock.Instance,
          TimeSpan.FromMinutes(30));
        _stage02Leases = new McpLeaseStore<Stage02PreviewLease>(
          SystemMcpClock.Instance,
          TimeSpan.FromMinutes(30));
        _stage03Leases = new McpLeaseStore<NativeStage03ScanResult>(
          SystemMcpClock.Instance,
          TimeSpan.FromMinutes(30));
        var stage01 = new McpStage01Adapter(gateway, _stage01Leases);
        var stage02 = new McpStage02Adapter(gateway, _stage02Leases);
        var stage03 = new McpStage03Adapter(gateway, _stage03Leases);
        var router = new McpBridgeCommandRouter(
          Session,
          gateway,
          stage01,
          stage02,
          stage03);
        _server = new McpNamedPipeServer(PipeName, SessionToken, router);
        _discovery = new McpBridgeDiscoveryWriter(processId);
        try
        {
          _server.Start();
          _discovery.Write(Session);
          _startupFailure = string.Empty;
        }
        catch
        {
          try { _server.Dispose(); } catch { }
          _server = null;
          _discovery.Delete();
          _discovery = null;
          Session = null;
          PipeName = string.Empty;
          SessionToken = string.Empty;
          throw;
        }
      }
    }

    internal static void Stop()
    {
      lock (SyncRoot)
      {
        try { _server?.Dispose(); } catch { }
        _server = null;
        try { _discovery?.Delete(); } catch { }
        _discovery = null;
        _stage01Leases?.Clear();
        _stage02Leases?.Clear();
        _stage03Leases?.Clear();
        _stage01Leases = null;
        _stage02Leases = null;
        _stage03Leases = null;
        Session = null;
        PipeName = string.Empty;
        SessionToken = string.Empty;
      }
    }

    internal static void RecordStartupFailure(Exception exception)
    {
      _startupFailure = exception?.Message ?? "未知 MCP Bridge 启动错误。";
      try
      {
        string directory = Path.Combine(
          Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
          "BIMBaoGui",
          "logs");
        Directory.CreateDirectory(directory);
        File.AppendAllText(
          Path.Combine(directory, "mcp-bridge-startup.log"),
          DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            + "｜"
            + _startupFailure
            + Environment.NewLine,
          new UTF8Encoding(false));
      }
      catch
      {
      }
    }

    private static string CreateRandomHex(int byteCount)
    {
      var bytes = new byte[byteCount];
      using (RandomNumberGenerator random = RandomNumberGenerator.Create())
        random.GetBytes(bytes);
      var builder = new StringBuilder(bytes.Length * 2);
      foreach (byte value in bytes) builder.Append(value.ToString("x2"));
      return builder.ToString();
    }
  }
}
