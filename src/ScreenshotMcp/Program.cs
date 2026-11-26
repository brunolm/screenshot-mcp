using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenshotMcp.Capture;

// Must run before anything reads window or monitor geometry, otherwise Windows reports
// virtualised coordinates and captures come back cropped or blurry on scaled displays.
NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DpiAwarenessPerMonitorV2);

int? port = ReadPortFlag(args);
if (port is null)
{
    await RunStdioAsync(args);
}
else
{
    await RunHttpAsync(port.Value);
}

static int? ReadPortFlag(string[] args)
{
    int index = Array.IndexOf(args, "--port");
    if (index == -1)
    {
        return null;
    }

    if (index + 1 < args.Length && int.TryParse(args[index + 1], out int port) && port is > 0 and < 65536)
    {
        return port;
    }

    throw new ArgumentException("--port needs a port number.");
}

static async Task RunStdioAsync(string[] args)
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
    ConfigureLogging(builder.Logging);

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}

// Stateless, so one process can serve every client on the machine and restart without
// invalidating anyone's session.
static async Task RunHttpAsync(int port)
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
    // Host filtering rejects any other Host header, which blocks DNS rebinding.
    builder.Configuration["AllowedHosts"] = "127.0.0.1;localhost";
    ConfigureLogging(builder.Logging);

    builder.Services
        .AddMcpServer()
        .WithHttpTransport(options => options.Stateless = true)
        .WithToolsFromAssembly();

    WebApplication app = builder.Build();
    app.Use(RejectForeignOrigins);
    app.MapMcp("/mcp");

    await app.RunAsync();
}

// Web pages can reach localhost too, so only requests without an Origin or from a local one get through.
static async Task RejectForeignOrigins(HttpContext context, RequestDelegate next)
{
    string? origin = context.Request.Headers.Origin;
    bool local = string.IsNullOrEmpty(origin)
        || (Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) && uri.Host is "127.0.0.1" or "localhost");

    if (!local)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    await next(context);
}

static void ConfigureLogging(ILoggingBuilder logging)
{
    // In stdio mode stdout carries the MCP protocol, so all logging has to go to stderr.
    logging.ClearProviders();
    logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    logging.SetMinimumLevel(LogLevel.Warning);
}
