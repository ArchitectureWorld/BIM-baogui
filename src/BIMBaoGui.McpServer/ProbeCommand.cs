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
      string json = await bridge.ListSessionsJsonAsync(cancellationToken)
        .ConfigureAwait(false);
      Console.Out.WriteLine(json);
      return candidates.Count > 0 ? 0 : 2;
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
