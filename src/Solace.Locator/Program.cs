using System.Diagnostics;
using System.Net;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateSlimBuilder(args);

builder.AddServiceDefaults();
builder.WebHost.UseKestrelHttpsConfiguration();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

var app = builder.Build();

// app.UseHttpsRedirection();

var apiServerConnectionString = builder.Configuration["services:api-server:http:0"];
Debug.Assert(apiServerConnectionString is not null);
var apiServerUri = new Uri(apiServerConnectionString);

var apiServerPort = apiServerUri.Port;

EarthApiResponse LocatorHandler(HttpContext context, ILogger<Program> logger)
{
    var protocol = context.Request.IsHttps ? "https://" : "http://";
    var baseServerIP = $"{protocol}{context.Request.Host.Host}:{apiServerPort}";

    Logs.LogLocatorIssued(logger, context.Connection.RemoteIpAddress, baseServerIP);

    return new EarthApiResponse(new LocatorResponse(new()
    {
        ["production"] = new LocatorResponse.Environment(baseServerIP, baseServerIP + "/cdn", "20CA2"),
    },
    new()
    {
        ["2020.1217.02"] = ["production"],
        ["2020.1210.01"] = ["production"],
    }
    ));
}

app.MapGet("/player/environment", LocatorHandler);
app.MapGet("/api/v1.0/player/environment", LocatorHandler);
app.MapGet("/api/v1.1/player/environment", LocatorHandler);

app.Run();

internal static partial class Logs
{
    [LoggerMessage(Level = LogLevel.Information, Message = "{RemoteIp} has issued locator, replying with {ServerIp}")]
    public static partial void LogLocatorIssued(ILogger logger, IPAddress? RemoteIp, string ServerIp);
}

internal sealed record LocatorResponse(
    Dictionary<string, LocatorResponse.Environment> ServiceEnvironments,
    Dictionary<string, List<string>> SupportedEnvironments
)
{
    public sealed record Environment(string ServiceUri, string CdnUri, string PlayfabTitleId);
}

internal sealed record EarthApiResponse(LocatorResponse Result);

[JsonSerializable(typeof(EarthApiResponse))]
[JsonSerializable(typeof(LocatorResponse))]
[JsonSerializable(typeof(Dictionary<string, LocatorResponse.Environment>))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
{
}