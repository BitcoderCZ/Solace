using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace Solace.Launcher;

internal sealed class Starter : IAsyncDisposable
{
    private readonly IConfiguration _configuration;

    private readonly Component _aspireDashboard;
    private readonly Component _eventBusServer;
    private readonly Component _objectStoreServer;
    private readonly Component _buildplateLauncher;
    private readonly Component _apiServer;
    private readonly Component _locator;
    private readonly Component _tappableGenerator;
    private readonly Component? _tileRenderer;
    private readonly Component _adminPanel;

    public Starter(IConfiguration configuration)
    {
        _configuration = configuration;

        const string PathToComponents = "../components";

        var dashboardOtlpPort = _configuration.GetValue<int>("AspireDashboard:OtlpPort", 4317);
        var dashboardUiPort = _configuration.GetValue<int>("AspireDashboard:UiPort", 18888);
        var otlpEndpoint = $"http://localhost:{dashboardOtlpPort}";

        var otlpApiKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

        var earthDbUseSqlite = _configuration.GetValue<bool>("Database:Earth:UseSqlite");
        if (!earthDbUseSqlite)
        {
            throw new NotImplementedException();
        }

        _aspireDashboard = Component.Builder.Command("aspire", ["dashboard", "run", "--frontend-url", $"http://localhost:{dashboardUiPort}", "--otlp-grpc-url", otlpEndpoint])
            .WithEnvironmentVariable("Dashboard__Otlp__AuthMode", "ApiKey")
            .WithEnvironmentVariable("Dashboard__Otlp__PrimaryApiKey", otlpApiKey)
            .Build();

        _eventBusServer = Component.Builder.Executable(new FileInfo($"{PathToComponents}/event-bus/EventBusServer"), [])
            .WithEndpoint("TCP_PORT", 5532)
            .WithOtel("event-bus", otlpEndpoint, otlpApiKey)
            .Build();

        var objectStoreDataDirectory = Path.GetFullPath(_configuration.GetValue<string>("ObjectStore:DataDirectory", "data"));

        _objectStoreServer = Component.Builder.Executable(new FileInfo($"{PathToComponents}/object-store/ObjectStoreServer"), [])
            .WithEndpoint("TCP_PORT", 5396)
            .WithEnvironmentVariable("DataDirectory", objectStoreDataDirectory)
            .WithOtel("object-store", otlpEndpoint, otlpApiKey)
            .Build();

        var staticDataPath = Path.GetFullPath(_configuration["Shared:StaticDataPath"]!);

        _buildplateLauncher = Component.Builder.Executable(new FileInfo($"{PathToComponents}/buildplate-launcher/BuildplateLauncher"), [])
            .WithEndpointReference("event-bus", "raw-tcp", "tcp", 5532)
            .WithEnvironmentFromSection(_configuration, "BuildplateLauncher", "BuildplateLauncher:")
            .WithEnvironmentVariable("StaticDataPath", staticDataPath)
            .WithOtel("buildplate-launcher", otlpEndpoint, otlpApiKey)
            .Build();

        var apiPort = _configuration.GetValue<int>("ApiServer:Port", 8088);

        _apiServer = Component.Builder.Executable(new FileInfo($"{PathToComponents}/api-server/ApiServer"), [])
            .WithHttpEndpoint(apiPort)
            .WithEndpointReference("event-bus", "raw-tcp", "tcp", 5532)
            .WithEndpointReference("object-store", "raw-tcp", "tcp", 5396)
            .WithEnvironmentVariable("DatabaseProvider", "Sqlite")
            .WithEnvironmentVariable("ConnectionStrings__EarthDb", $"Data Source={Path.GetFullPath("../data/earth.db")}")
            .WithEnvironmentVariable("StaticDataPath", staticDataPath)
            .WithEnvironmentFromSection(_configuration, "ApiServer:Authentication", "ApiServer:")
            .WithOtel("api-server", otlpEndpoint, otlpApiKey)
            .Build();

        var locatorPort = _configuration.GetValue<int>("Locator:Port", 8088);

        _locator = Component.Builder.Executable(new FileInfo($"{PathToComponents}/locator/Locator"), [])
            .WithHttpEndpoint(locatorPort)
            .WithEndpointReference("api-server", "http", "http", apiPort)
            .WithOtel("locator", otlpEndpoint, otlpApiKey)
            .Build();

        _tappableGenerator = Component.Builder.Executable(new FileInfo($"{PathToComponents}/tappable-generator/TappablesGenerator"), [])
            .WithEndpointReference("event-bus", "raw-tcp", "tcp", 5532)
            .WithEnvironmentVariable("StaticDataPath", staticDataPath)
            .WithOtel("tappable-generator", otlpEndpoint, otlpApiKey)
            .Build();

        var anyTileDataSources = _configuration.GetSection("TileRenderer:TileSource").AsEnumerable().Any(item => !string.IsNullOrWhiteSpace(item.Value));

        if (anyTileDataSources)
        {
            _tileRenderer = Component.Builder.Executable(new FileInfo($"{PathToComponents}/tile-renderer/TileRenderer"), [])
                .WithEndpointReference("event-bus", "raw-tcp", "tcp", 5532)
                .WithEnvironmentVariable("StaticDataPath", staticDataPath)
                .WithEnvironmentFromSection(_configuration, "TileRenderer:TileSource", "TileRenderer:")
                .WithOtel("tile-renderer", otlpEndpoint, otlpApiKey)
                .Build();
        }

        var adminPanelPort = _configuration.GetValue<int>("AdminPanel:Port", 5000);

        _adminPanel = Component.Builder.Executable(new FileInfo($"{PathToComponents}/admin-panel/AdminPanel"), [])
            .WithHttpEndpoint(adminPanelPort)
            .WithEndpointReference("event-bus", "raw-tcp", "tcp", 5532)
            .WithEndpointReference("object-store", "raw-tcp", "tcp", 5396)
            .WithEnvironmentVariable("DatabaseProvider", "Sqlite")
            .WithEnvironmentVariable("ConnectionStrings__EarthDb", $"Data Source={Path.GetFullPath("../data/earth.db")}")
            .WithEnvironmentVariable("StaticDataPath", staticDataPath)
            .WithEnvironmentVariable("EnableAdminPanelBuildplatePreview", _configuration["AdminPanel:EnableAdminPanelBuildplatePreview"]!)
            .WithOtel("admin-panel", otlpEndpoint, otlpApiKey)
            .Build();
    }

    public IEnumerable<KeyValuePair<string, bool>> GetComponentStatus()
    {
        yield return new("Dashboard", _aspireDashboard.IsRunning);
        yield return new("Event Bus", _eventBusServer.IsRunning);
        yield return new("Object Store", _objectStoreServer.IsRunning);
        yield return new("Buildplate Launcher", _buildplateLauncher.IsRunning);
        yield return new("Api Server", _apiServer.IsRunning);
        yield return new("Locator", _locator.IsRunning);
        yield return new("Tappables Generator", _tappableGenerator.IsRunning);
        if (_tileRenderer is not null)
        {
            yield return new("Tile Renderer", _tileRenderer.IsRunning);
        }

        yield return new("Admin Panel", _adminPanel.IsRunning);
    }

    public async Task StartAsync()
    {
        await _aspireDashboard.StartAsync();

        await Task.Delay(800);

        await _eventBusServer.StartAsync();

        await _objectStoreServer.StartAsync();

        await Task.Delay(800);

        await _buildplateLauncher.StartAsync();

        await Task.Delay(800);

        await _apiServer.StartAsync();

        await _locator.StartAsync();

        await _tappableGenerator.StartAsync();

        if (_tileRenderer is not null)
        {
            await _tileRenderer.StartAsync();
        }

        await _adminPanel.StartAsync();
    }

    public async Task StopAsync()
    {
        await _adminPanel.StopAsync();

        if (_tileRenderer is not null)
        {
            await _tileRenderer.StopAsync();
        }

        await _tappableGenerator.StopAsync();

        await _locator.StopAsync();

        await _buildplateLauncher.StopAsync();

        await _apiServer.StopAsync();

        await _eventBusServer.StopAsync();

        await _objectStoreServer.StopAsync();

        await _aspireDashboard.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _adminPanel.DisposeAsync();

        if (_tileRenderer is not null)
        {
            await _tileRenderer.DisposeAsync();
        }

        await _tappableGenerator.DisposeAsync();

        await _locator.DisposeAsync();

        await _buildplateLauncher.DisposeAsync();

        await _apiServer.DisposeAsync();

        await _eventBusServer.DisposeAsync();

        await _objectStoreServer.DisposeAsync();

        await _aspireDashboard.DisposeAsync();
    }
}