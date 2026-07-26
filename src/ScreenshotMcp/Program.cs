using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenshotMcp.Capture;
using ScreenshotMcp.Imaging;

// Must run before anything reads window or monitor geometry, otherwise Windows reports
// virtualised coordinates and captures come back cropped or blurry on scaled displays.
NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DpiAwarenessPerMonitorV2);

ImageStore.Prune();

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol, so all logging has to go to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
