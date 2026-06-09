using System.Text.Json;
using Solace.Common.Utils;

namespace Solace.LauncherUI;

public sealed partial class Settings
{
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static readonly Settings Default = new Settings()
    {
        ApiPort = 8080,
        EventBusPort = 5532,
        ObjectStorePort = 5396,
        IPv4 = "192.168.x.x",
        EarthDatabaseConnectionString = Path.Combine(Program.DataDirRelative, "earth.db"),
        LiveDatabaseConnectionString = Path.Combine(Program.DataDirRelative, "live.db"),
        EnableTileRenderingLabel = true,
        TileDataSource = TileDataSourceE.MapTiler,
        MapTilerApiKey = null,
        TileDatabaseConnectionString = "Host=localhost;Username=mylogin;Password=mypass;Database=genoa_tile_data",
        GeneratePreviewOnImport = true,
        SkipFileChecks = false,
        StaticDataPath = "../staticdata",
        LauncherBuildplatePreview = false,
        OnlyAllowLocalLogin = false,
    };

    public static Settings Instance { get; set; } = Default;

    public static string DefaultPath => "config.json";

    public ushort? ApiPort { get; set; }
    public ushort? EventBusPort { get; set; }
    public ushort? ObjectStorePort { get; set; }
    public string? IPv4 { get; set; }

    public string? EarthDatabaseConnectionString { get; set; }
    public string? LiveDatabaseConnectionString { get; set; }

    public bool? EnableTileRenderingLabel { get; set; }
    public TileDataSourceE? TileDataSource { get; set; }
    public string? MapTilerApiKey { get; set; }
    public string? TileDatabaseConnectionString { get; set; }

    public bool? GeneratePreviewOnImport { get; set; } // TODO: is this really needed?
    public bool? SkipFileChecks { get; set; }

    public string? StaticDataPath { get; set; }

    public bool? LauncherBuildplatePreview { get; set; }

    public bool? OnlyAllowLocalLogin { get; set; }

    public enum TileDataSourceE
    {
        MapTiler,
        PostgreSQL,
    }

    public void Save(string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(this, jsonOptions));

    public async Task SaveAsync(string path)
    {
        using (var fs = File.OpenWriteNew(path))
        {
            await JsonSerializer.SerializeAsync(fs, this, jsonOptions);
        }
    }

    public static async Task<Settings> LoadAsync(string path, ILogger logger)
    {
        LogLoadingSettings(logger);

        Settings? settings;

        if (!File.Exists(path))
        {
            LogConfigFileDoesNotExistUsingDefault(logger);
            settings = Default;
        }
        else
        {
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    settings = await JsonSerializer.DeserializeAsync<Settings>(fs, jsonOptions);
                }

                if (settings is null)
                {
                    throw new InvalidDataException("Settings is null");
                }
            }
            catch (Exception exception)
            {
                LogErrorWhenParsingSettingsUsingDefault(logger, exception);
                settings = Default;
            }
        }

        if (settings.ApiPort is null)
        {
            LogInvalidOption(logger, "Api port", Default.ApiPort);
            settings.ApiPort = Default.ApiPort;
        }

        if (settings.EventBusPort is null)
        {
            LogInvalidOption(logger, "EventBus port", Default.EventBusPort);
            settings.EventBusPort = Default.EventBusPort;
        }

        if (settings.ObjectStorePort is null)
        {
            LogInvalidOption(logger, "ObjectStore port", Default.ObjectStorePort);
            settings.ObjectStorePort = Default.ObjectStorePort;
        }

        UriHostNameType nameType = Uri.CheckHostName(settings.IPv4);

        if (nameType != UriHostNameType.IPv4 && nameType != UriHostNameType.Dns)
        {
            LogInvalidOption(logger, "IPv4", Default.IPv4);
            settings.IPv4 = Default.IPv4;
        }

        if (string.IsNullOrWhiteSpace(settings.EarthDatabaseConnectionString))
        {
            LogInvalidOption(logger, "EarthDatabaseConnectionString", Default.EarthDatabaseConnectionString);
            settings.EarthDatabaseConnectionString = Default.EarthDatabaseConnectionString;
        }

        if (string.IsNullOrWhiteSpace(settings.LiveDatabaseConnectionString))
        {
            LogInvalidOption(logger, "LiveDatabaseConnectionString", Default.LiveDatabaseConnectionString);
            settings.LiveDatabaseConnectionString = Default.LiveDatabaseConnectionString;
        }

        if (settings.EnableTileRenderingLabel is null)
        {
            LogInvalidOption(logger, "EnableTileRenderingLabel", Default.EnableTileRenderingLabel);
            settings.EnableTileRenderingLabel = Default.EnableTileRenderingLabel;
        }

        if (settings.EnableTileRenderingLabel is true)
        {
            if (settings.TileDataSource is null)
            {
            LogInvalidOption(logger, "TileDataSource", Default.TileDataSource);
                settings.TileDataSource = Default.TileDataSource;
            }

            if (string.IsNullOrWhiteSpace(settings.MapTilerApiKey))
            {
            LogInvalidOption(logger, "MapTilerApiKey", Default.MapTilerApiKey);
                settings.MapTilerApiKey = Default.MapTilerApiKey;
            }
        }

        if (settings.TileDatabaseConnectionString is null)
        {
            LogInvalidOption(logger, "TileDatabaseConnectionString", Default.TileDatabaseConnectionString);
            settings.TileDatabaseConnectionString = Default.TileDatabaseConnectionString;
        }

        if (settings.GeneratePreviewOnImport is null)
        {
            LogInvalidOption(logger, "Generate preview on import", Default.GeneratePreviewOnImport);
            settings.GeneratePreviewOnImport = Default.GeneratePreviewOnImport;
        }

        if (settings.SkipFileChecks is null)
        {
            LogInvalidOption(logger, "Skip file checks", Default.SkipFileChecks);
            settings.SkipFileChecks = Default.SkipFileChecks;
        }

        if (string.IsNullOrWhiteSpace(settings.StaticDataPath))
        {
            LogInvalidOption(logger, "StaticData path", Default.StaticDataPath);
            settings.StaticDataPath = Default.StaticDataPath;
        }

        if (settings.OnlyAllowLocalLogin is null)
        {
            LogInvalidOption(logger, "OnlyAllowLocalLogin", Default.OnlyAllowLocalLogin);
            settings.OnlyAllowLocalLogin = Default.OnlyAllowLocalLogin;
        }

        LogLoadedSettings(logger);

        await settings.SaveAsync(path);

        return settings;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Loading settings...")]
    private static partial void LogLoadingSettings(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Config file doesn't exist, using default")]
    private static partial void LogConfigFileDoesNotExistUsingDefault(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error when parsing settings, using default")]
    private static partial void LogErrorWhenParsingSettingsUsingDefault(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{OptionName} is invalid, using default: '{DefaultValue}'")]
    private static partial void LogInvalidOption(ILogger logger, string OptionName, object? DefaultValue);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Loaded settings")]
    private static partial void LogLoadedSettings(ILogger logger);
}
