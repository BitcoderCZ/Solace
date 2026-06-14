using System.Diagnostics;
using Solace.Common;
using Solace.Common.Utils;
using Solace.EventBus.Client;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Solace.Buildplate.Launcher;

internal static partial class Program
{
    internal static string StaticDataPath = "./staticdata";

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    private sealed class Options
    {
        [Option("bridgeJar", Required = true, HelpText = "Fountain bridge JAR file")]
        public string BridgeJar { get; set; }
        [Option("serverTemplateDir", Required = true, HelpText = "Minecraft/Fabric server template directory, containing the Fabric JAR, mods, and libraries")]
        public string ServerTemplateDir { get; set; }
        [Option("fabricJarName", Required = true, HelpText = "Name of the Fabric JAR to run within the server template directory")]
        public string FabricJarName { get; set; }
        [Option("connectorPluginJar", Required = true, HelpText = "Fountain connector plugin JAR")]
        public string ConnectorPluginJar { get; set; }

        [Option("dir", Default = "./staticdata", Required = false, HelpText = "Static data path")]
        public string StaticDataPath { get; set; }
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    private static async Task<int> Main(string[] args)
    {
        if (!Debugger.IsAttached)
        {
            AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
            {
                Console.Error.WriteLine($"Unhandled exception: {e.ExceptionObject}");

                try
                {
                    var logger = GlobalLoggerFactory.CreateLogger(nameof(Program));
                    LogUnhandledException(logger, e.ExceptionObject as Exception);
                }
                catch
                {
                    Console.Error.WriteLine($"Unhandled exception before logger initialization");
                }

                Console.Out.Flush();
                Console.Error.Flush();

                Environment.Exit(2);
            };
        }

        var builder = Host.CreateApplicationBuilder(args);

        builder.AddServiceDefaults();

        builder.Services.AddSingleton<StartupDependencies>();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<StartupDependencies>().EventBus);
        builder.Services.AddSingleton(sp => sp.GetRequiredService<StartupDependencies>().Starter);
        builder.Services.AddSingleton<InstanceManager>();

        using var app = builder.Build();

        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        GlobalLoggerFactory.Initialize(loggerFactory);

        var programLogger = loggerFactory.CreateLogger(nameof(Program));

        StaticDataPath = options.StaticDataPath;

        // init stuff that requires logger but needs to be injected
        var startupDeps = app.Services.GetRequiredService<StartupDependencies>();

        var eventBusConnectionString = builder.Configuration["services:event-bus:raw-tcp:0"];
        Debug.Assert(eventBusConnectionString is not null);
        var eventBusUri = new Uri(eventBusConnectionString);

        LogConnectingToEventBus(programLogger);
        EventBusClient eventBusClient;
        try
        {
            eventBusClient = await EventBusClient.ConnectAsync($"{eventBusUri.Host}:{eventBusUri.Port}");
        }
        catch (EventBusClientException exception)
        {
            LogConnectToEventBusError(programLogger, exception);
            loggerFactory.Dispose();
            return 3;
        }

        LogConnectedToEventBus(programLogger);

        string javaCmd = JavaLocator.Locate(GlobalLoggerFactory.CreateLogger(nameof(JavaLocator)));
        var starter = new Starter(eventBusClient, options.EventBusConnectionString, builder.Configuration["PublicEndPoint"], checked((ushort)builder.Configuration.GetValue<int>("BaseInstancePublicPort")), javaCmd, options.BridgeJar, options.ServerTemplateDir, options.FabricJarName, options.ConnectorPluginJar, GlobalLoggerFactory.CreateLogger<Starter>());

        startupDeps.EventBus = eventBusClient;
        startupDeps.Starter = starter;

        // init stuff that needs async initialization
        await app.Services.GetRequiredService<InstanceManager>().InitializeAsync(eventBusClient);


        Console.CancelKeyPress += (sender, e) =>
        {
            Log.Information("Ctrl+C received");
            instanceManager.ShutdownAsync().Forget();
            e.Cancel = true;
        };

        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            instanceManager.ShutdownAsync().Wait();
        };

        Log.Information("Started, public address: {Address}, base port: {BasePort}", options.PublicAddress, options.BasePublicPort);
        LogStarted(programLogger);

        while (true)
        {
            await Task.Delay(1000);
        }
    }

    internal sealed class StartupDependencies
    {
        public EventBusClient EventBus { get; set; } = null!;
        public Starter Starter { get; set; } = null!;
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception")]
    private static partial void LogUnhandledException(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connecting to event bus")]
    private static partial void LogConnectingToEventBus(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Could not connect to event bus")]
    private static partial void LogConnectToEventBusError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to event bus")]
    private static partial void LogConnectedToEventBus(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Started, public address: {Address}, base port: {BasePort}")]
    private static partial void LogStarted(ILogger logger, string Address, int BasePort);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Fatal error during server startup")]
    private static partial void LogFatalErrorDuringServerStartup(ILogger logger, Exception exception);
}
