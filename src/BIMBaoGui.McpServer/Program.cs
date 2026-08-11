using BIMBaoGui.McpServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (args.Any(value => string.Equals(
  value,
  "--probe",
  StringComparison.OrdinalIgnoreCase)))
{
  return await ProbeCommand.RunAsync(CancellationToken.None);
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
  options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton<BridgeSessionLocator>();
builder.Services.AddSingleton<NamedPipeBridgeService>();
builder.Services
  .AddMcpServer()
  .WithStdioServerTransport()
  .WithToolsFromAssembly();

await builder.Build().RunAsync();
return 0;
