using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
{
  Console.Error.WriteLine("Usage: BIMBaoGui.McpSmoke <BIMBaoGui.McpServer.exe>");
  return 64;
}

string serverPath = Path.GetFullPath(args[0]);
if (!File.Exists(serverPath))
{
  Console.Error.WriteLine("MCP server executable not found: " + serverPath);
  return 66;
}

string[] expectedTools =
{
  "bimbaogui_get_document_status",
  "bimbaogui_get_rule_package_identity",
  "bimbaogui_list_revit_sessions",
  "bimbaogui_stage01_get_form_schema",
  "bimbaogui_stage01_read",
  "bimbaogui_stage01_validate",
  "bimbaogui_stage01_write",
  "bimbaogui_stage02_preview",
  "bimbaogui_stage02_write",
  "bimbaogui_stage03_export",
  "bimbaogui_stage03_get_last_result",
  "bimbaogui_stage03_revalidate_file",
  "bimbaogui_stage03_scan"
};

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var transport = new StdioClientTransport(new StdioClientTransportOptions
{
  Name = "BIMBaoGui MCP CI smoke",
  Command = serverPath,
  Arguments = Array.Empty<string>()
});

await using McpClient client = await McpClient.CreateAsync(
  transport,
  cancellationToken: timeout.Token);
IList<McpClientTool> tools = await client.ListToolsAsync(
  cancellationToken: timeout.Token);
string[] actualTools = tools
  .Select(tool => tool.Name)
  .OrderBy(name => name, StringComparer.Ordinal)
  .ToArray();

if (!actualTools.SequenceEqual(expectedTools, StringComparer.Ordinal))
{
  Console.Error.WriteLine(
    "Unexpected MCP tools. Expected="
      + string.Join(",", expectedTools)
      + " Actual="
      + string.Join(",", actualTools));
  return 1;
}

CallToolResult result = await client.CallToolAsync(
  "bimbaogui_list_revit_sessions",
  new Dictionary<string, object?>(),
  cancellationToken: timeout.Token);
string responseText = result.Content
  .OfType<TextContentBlock>()
  .Select(block => block.Text)
  .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))
  ?? string.Empty;
if (responseText.Length == 0)
{
  Console.Error.WriteLine("Session-list tool returned no text content.");
  return 1;
}

using JsonDocument response = JsonDocument.Parse(responseText);
JsonElement root = response.RootElement;
if (!root.TryGetProperty("success", out JsonElement success)
  || success.ValueKind != JsonValueKind.True)
{
  Console.Error.WriteLine("Session-list tool did not return success=true: " + responseText);
  return 1;
}
if (!root.TryGetProperty("status", out JsonElement status)
  || !string.Equals(
    status.GetString(),
    "REVIT_NOT_CONNECTED",
    StringComparison.Ordinal))
{
  Console.Error.WriteLine("Unexpected no-Revit status: " + responseText);
  return 1;
}
if (!root.TryGetProperty("sessions", out JsonElement sessions)
  || sessions.ValueKind != JsonValueKind.Array
  || sessions.GetArrayLength() != 0)
{
  Console.Error.WriteLine("Unexpected no-Revit sessions payload: " + responseText);
  return 1;
}

Console.WriteLine(JsonSerializer.Serialize(new
{
  connected = true,
  tool_count = actualTools.Length,
  tools = actualTools,
  list_sessions_status = status.GetString()
}));
return 0;
