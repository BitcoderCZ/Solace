using CommandLine;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using System.ComponentModel;
using System.Diagnostics;
using Uma.Uuid;
using Solace.ApiServer.Utils;
using Solace.BuildplateImporter;
using Solace.Common;
using Solace.Common.Utils;
using Solace.DB;
using Solace.EventBus.Client;
using Solace.ObjectStore.Client;
using Solace.StaticData;
using SData = Solace.StaticData.StaticData;
using Microsoft.AspNetCore.Authentication;
using Asp.Versioning;
using Solace.ApiServer.Authentication;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog.Extensions.Logging;

namespace Solace.ApiServer;

public static class Program
{
    // initialized in main
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    internal static Config config;

#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    private sealed class Options
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    {
        [Option("earth-db", Default = "./earth.db", Required = false, HelpText = "Earth database connection string")]
        public string EarthDatabaseConnectionString { get; set; }

        [Option("live-db", Default = "./live.db", Required = false, HelpText = "Live database connection string")]
        public string LiveDatabaseConnectionString { get; set; }

        [Option("dir", Default = "./staticdata", Required = false, HelpText = "Static data path")]
        public string StaticDataPath { get; set; }

        [Option("eventbus", Default = "localhost:5532", Required = false, HelpText = "Event bus address")]
        public string EventBusConnectionString { get; set; }

        [Option("objectstore", Default = "localhost:5396", Required = false, HelpText = "Object storage address")]
        public string ObjectStoreConnectionString { get; set; }

        [Option("logger-url", Default = null, Required = false, HelpText = "Url to send logs to")]
        public string? LoggerUrl { get; set; }

        [Option("local-login-only", Default = false, Required = false, HelpText = "Whenther to only allow local accounts, or also allow microsoft accounts")]
        public bool LocalLoginOnly { get; set; }
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    public static async Task<int> Main(string[] args)
    {
        Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;

        if (!Debugger.IsAttached)
        {
            AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
            {
                try
                {
                    var logger = GlobalLoggerFactory.CreateLogger(nameof(Program));
                    LogUnhandledException(logger, e.ExceptionObject as Exception);
                }
                catch
                {
                    Console.Error.WriteLine($"Unhandled exception before logger initialization: {e.ExceptionObject}");
                }

                Environment.Exit(1);
            };
        }

        if (options.LocalLoginOnly)
        {
            Log.Information("Local account only login enabled, Microsoft accounts will not work");
        }
        else
        {
            Log.Warning("Local account only login disabled, account credentials cannot be verified");
        }

        Log.Information("Loading configuration");
        try
        {
            const string configFileName = "api_config.json";
            if (!File.Exists(configFileName))
            {
                config = Config.Default;
                File.WriteAllText(configFileName, Json.SerializeIndented(config));
                Log.Information($"Configuration file not found or invalid, created with default values: {Path.GetFullPath(configFileName)}");
            }
            else
            {
                config = Json.Deserialize<Config>(File.ReadAllText(configFileName)) ?? Config.Default;
            }
        }
        catch (Exception ex)
        {
            Log.Fatal($"Failed to load configuration: {ex}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Loaded configuration");

        Log.Information("Loading static data");
        SData staticData;
        try
        {
            staticData = new SData(options.StaticDataPath);
        }
        catch (StaticDataException staticDataException)
        {
            Log.Fatal($"Failed to load static data: {staticDataException}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Loaded static data");

        Log.Information("Importing shop buildplates");

        await using (var earthDbContext = EarthDbContext.CreateFromPath(options.EarthDatabaseConnectionString!))
        {
            var currentShopBuildplates = await earthDbContext.TemplateBuildplates
                .AsNoTracking()
                .ToListAsync();

            await using var importer = new Importer(earthDbContext, eventBus, objectStore, importerLogger)
            {
                OwnsEarthDb = false,
                OwnsEventBusClient = false,
                OwnsObjectStoreClient = false,
            };

            foreach (var buidplate in staticData.Buildplates.ShopBuildplates)
            {
                if (earthDbContext.TemplateBuildplates.Any(bp => bp.Id == buidplate.Id))
                {
                    Log.Debug($"Shop buildplate {buidplate.Id} already exists");
                    continue;
                }

                try
                {
                    Log.Information($"Importing shop buildplate {buidplate.Id}");

                    string name = "unknown buildplate";
                    var bpPlayfabItem = staticData.Playfab.Items.Values.FirstOrDefault(item => item.Data is Playfab.Item.BuildplateData bpData && bpData.Id == buidplate.Id);
                    if (bpPlayfabItem is not null)
                    {
                        name = bpPlayfabItem.Title;
                    }

                    using (var buidplateData = buidplate.OpenRead())
                    {
                        await importer.ImportTemplateAsync(buidplate.Id, $"[SHOP] {name}", buidplateData);
                    }
                }
                catch (Exception ex)
                {
                    Log.Fatal($"Failed to import shop buidplate {buidplate.Id}: {ex}");
                    Log.CloseAndFlush();
                    return 1;
                }
            }
        }

        Log.Information("Imported shop buidplates");

        var builder = WebApplication.CreateBuilder(args);

        var earthDbConnectionString = builder.Configuration.GetConnectionString("EarthDb");
        var earthDbProvider = builder.Configuration["DatabaseProvider"];
        Debug.Assert(earthDbConnectionString is not null);
        Debug.Assert(earthDbProvider is not null);

        var eventBusConnectionString = builder.Configuration.GetConnectionString("event-bus");
        Debug.Assert(eventBusConnectionString is not null);
        var eventBusUri = new Uri(eventBusConnectionString);

        Log.Information("Connecting to event bus");
        EventBusClient eventBus;
        try
        {
            eventBus = await EventBusClient.ConnectAsync($"{eventBusUri.Host}:{eventBusUri.Port}");
        }
        catch (EventBusClientException ex)
        {
            Log.Fatal($"Could not connect to event bus: {ex}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Connected to event bus");

        var objectStoreConnectionString = builder.Configuration.GetConnectionString("object-store");
        Debug.Assert(objectStoreConnectionString is not null);
        var objectStoreUri = new Uri(objectStoreConnectionString);

        Log.Information("Connecting to object storage");
        ObjectStoreClient objectStore;
        try
        {
            objectStore = await ObjectStoreClient.ConnectAsync($"{objectStoreUri.Host}:{objectStoreUri.Port}");
        }
        catch (ObjectStoreClientException ex)
        {
            Log.Fatal($"Could not connect to object storage: {ex}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Connected to object storage");

        // builder.Host.UseSerilog();

        // builder.WebHost.UseUrls($"http://*:8080/");

        builder.AddServiceDefaults();

        builder.Services.AddSingleton(eventBus);
        builder.Services.AddSingleton(objectStore);
        builder.Services.AddSingleton(staticData);
        builder.Services.AddSingleton<TappablesManager>();
        builder.Services.AddSingleton<BuildplateInstancesManager>();
        builder.Services.AddSingleton<BuildplateInstanceRequestHandler>();

        builder.Services.AddMemoryCache();

        builder.Services.AddSingleton<CatalogResponseCacheService>();

        builder.Services.AddControllers()
           .ConfigureApplicationPartManager(manager =>
           {
               manager.FeatureProviders.Add(new InternalControllerFeatureProvider());
           });

        builder.Services.AddResponseCompression(options =>
        {
            options.Providers.Add<GzipCompressionProvider>();
        });

        builder.Services.AddResponseCaching();

        builder.Services.AddApiVersioning(config =>
        {
            config.DefaultApiVersion = new ApiVersion(1, 1);
            config.AssumeDefaultVersionWhenUnspecified = true;
            config.ReportApiVersions = true;
        });

        builder.Services.AddAuthentication("GenoaAuth")
            .AddScheme<AuthenticationSchemeOptions, GenoaAuthenticationHandler>("GenoaAuth", null);

        builder.Services.AddDbContext<EarthDbContext>(options => EarthDbContext.ConfigureBuilder(options, earthDbConnectionString, earthDbProvider));

        await using (var earthDbContext = EarthDbContext.CreateFromConnection(earthDbConnectionString, earthDbProvider))
        {
            var secrets = await earthDbContext.GetOrInitializeSecretsAsync();

            builder.Services.AddSingleton(secrets);
        }

        var app = builder.Build();

        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };

        forwardedHeadersOptions.KnownIPNetworks.Clear();
        forwardedHeadersOptions.KnownProxies.Clear();

        app.UseForwardedHeaders(forwardedHeadersOptions);

        app.Use(async (context, next) =>
        {
            context.Items.Add(RequestExtensions.TimestampKey, DateTimeOffset.UtcNow);
            await next();
        });

        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseETagger();

        app.UseResponseCaching();
        app.UseResponseCompression();

        app.MapControllers();

        // init stuff that needs async initialization
        await app.Services.GetRequiredService<TappablesManager>().InitializeAsync(eventBus);
        await app.Services.GetRequiredService<BuildplateInstancesManager>().InitializeAsync(eventBus);
        await app.Services.GetRequiredService<BuildplateInstanceRequestHandler>().InitializeAsync(eventBus);

        await app.RunAsync();

        return 0;
    }
}
