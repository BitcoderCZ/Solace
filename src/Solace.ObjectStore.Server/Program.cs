using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Solace.Common;
using System.Diagnostics;

namespace Solace.ObjectStore.Server;

internal static partial class Program
{
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

        string dataDirectory = Path.GetFullPath(builder.Configuration.GetValue<string>("ObjectStore:DataDirectory", "data/object_store"));

        builder.Services.AddSingleton<DataStore>(new DataStore(new DirectoryInfo(dataDirectory)));
        builder.Services.AddSingleton<Server>();
        builder.Services.AddSingleton<NetworkServer>(sp =>
        {
            var port = builder.Configuration.GetValue<int>("TCP_PORT", 5396);

            var server = sp.GetRequiredService<Server>();
            var networkServerLogger = sp.GetRequiredService<ILogger<NetworkServer>>();

            return new NetworkServer(server, port, networkServerLogger);
        });

        using var host = builder.Build();

        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        GlobalLoggerFactory.Initialize(loggerFactory);

        var logger = loggerFactory.CreateLogger(nameof(Program));
        LogDataStoragePath(logger, dataDirectory);

        try
        {
            var server = host.Services.GetRequiredService<NetworkServer>();
            await server.RunAsync();
        }
        catch (IOException exception)
        {
            LogFatalErrorDuringServerStartup(logger, exception);
            return 1;
        }

        return 0;
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception")]
    private static partial void LogUnhandledException(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Using {Path} for data storage")]
    private static partial void LogDataStoragePath(ILogger logger, string Path);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Fatal error during server startup")]
    private static partial void LogFatalErrorDuringServerStartup(ILogger logger, Exception exception);
}
