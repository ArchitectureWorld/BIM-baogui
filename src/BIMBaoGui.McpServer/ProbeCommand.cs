using System.Text.Json;
using BIMBaoGui.McpContracts;

namespace BIMBaoGui.McpServer;

internal static class ProbeCommand
{
  internal static async Task<int> RunAsync(
    CancellationToken cancellationToken)
  {
    try
    {
      var locator = new BridgeSessionLocator();
      var bridge = new NamedPipeBridgeService(locator);
      IReadOnlyList<BridgeSessionDescriptor> candidates =
        locator.ListCandidates();
      if (candidates.Count == 0)
      {
        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
          connected = false,
          status = BridgeErrorCodes.RevitNotConnected,
          sessions = Array.Empty<object>()
        }));
        return 2;
      }

      var sessions = new List<PublicBridgeSession>();
      foreach (BridgeSessionDescriptor descriptor in candidates)
      {
        bool reachable;
        try
        {
          BridgeResponse response = await bridge.SendAsync(
            descriptor,
            BridgeMethodNames.Ping,
            "{}",
            2000,
            cancellationToken).ConfigureAwait(false);
          reachable = response.Success;
        }
        catch
        {
          reachable = false;
        }
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

      bool connected = sessions.Any(value => value.PipeReachable);
      string status = connected
        ? candidates.Count > 1
          ? BridgeErrorCodes.MultipleRevitSessions
          : "OK"
        : BridgeErrorCodes.RevitNotConnected;
      Console.Out.WriteLine(JsonSerializer.Serialize(new
      {
        connected,
        status,
        sessions
      }));
      if (!connected) return 2;
      return candidates.Count > 1 ? 3 : 0;
    }
    catch (BridgeClientException exception)
    {
      Console.Out.WriteLine(JsonSerializer.Serialize(new
      {
        connected = false,
        status = exception.ErrorCode,
        message = exception.Message,
        sessions = Array.Empty<object>()
      }));
      return exception.ErrorCode == BridgeErrorCodes.MultipleRevitSessions
        ? 3
        : 2;
    }
    catch (Exception exception)
    {
      Console.Out.WriteLine(JsonSerializer.Serialize(new
      {
        connected = false,
        status = BridgeErrorCodes.TechnicalFatal,
        message = exception.Message,
        sessions = Array.Empty<object>()
      }));
      return 4;
    }
  }
}
