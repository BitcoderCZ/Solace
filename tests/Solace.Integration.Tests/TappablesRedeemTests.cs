using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Asp.Versioning;
using Solace.ApiServer.Controllers;
using Solace.ApiServer.Utils;
using Solace.Common;
using Solace.EventBus.Client;
using Solace.EventBus.Server;
using Solace.StaticData;
using Solace.DB;
using Solace.TappablesGenerator;

namespace Solace.Integration.Tests;

public class TappablesRedeemTests
{
    [Test]
    public async Task RedeemTappableEndToEnd_Smoke()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        await using var host = await BuildTestHostAsync(typeof(TappablesManager).Assembly, connection);
        var client = host.App.GetTestClient();

        var accountId = Guid.NewGuid();
        using (var scope = host.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EarthDbContext>();
            await db.EnsureAccountExists(accountId);
        }

        client.DefaultRequestHeaders.Add("X-Account-Id", accountId.ToString());

        double latitude = 51.0;
        double longitude = 0.0;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string itemId;

        using (var scope = host.App.Services.CreateScope())
        {
            var staticData = scope.ServiceProvider.GetRequiredService<StaticData.StaticData>();
            itemId = staticData.Catalog.ItemsCatalog.Items.First(item => item.Experience.Tappable > 0).Id;
        }

        var tappableId = Guid.NewGuid();
        var tappable = new TappablesManager.Tappable(
            tappableId,
            (float)latitude,
            (float)longitude,
            now,
            60_000,
            "test-icon",
            TappablesManager.Tappable.RarityE.COMMON,
            [new TappablesManager.Tappable.Item(itemId, 1)]
        );

        var publisher = host.EventBusServer.AddPublisher();
        publisher.Publish("tappables", now, "tappableSpawn", JsonSerializer.Serialize(new[] { tappable }));

        string tileId = TappablesManager.LocationToTileId((float)latitude, (float)longitude);

        var locationsResponse = await client.GetAsync($"/1/api/v1.1/locations/{latitude}/{longitude}");
        await Assert.That(locationsResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var locationsBody = await locationsResponse.Content.ReadAsStringAsync();
        Console.WriteLine("GET /locations response:");
        Console.WriteLine(locationsBody);
        using (var document = JsonDocument.Parse(locationsBody))
        {
            var activeLocations = document.RootElement.GetProperty("result").GetProperty("activeLocations");
            await Assert.That(activeLocations.GetArrayLength()).IsEqualTo(1);
            await Assert.That(activeLocations[0].GetProperty("id").GetGuid()).IsEqualTo(tappableId);
        }

        var redeemRequest = new
        {
            id = tappableId,
            playerCoordinate = new { latitude, longitude }
        };

        var redeemContent = new StringContent(JsonSerializer.Serialize(redeemRequest), Encoding.UTF8, "application/json");
            using (var scope = host.App.Services.CreateScope())
            {
                var tm = scope.ServiceProvider.GetRequiredService<TappablesManager>();
                var tapp = tm.GetTappableWithId(redeemRequest.id, tileId);
                if (tapp is null)
                {
                    Console.WriteLine("TappablesManager does not contain the tappable before redeem");
                }
                else
                {
                    Console.WriteLine($"Tappable present: spawn={tapp.SpawnTime}, validFor={tapp.ValidFor}");
                    Console.WriteLine($"IsTappableValidFor: {tm.IsTappableValidFor(tapp, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), (float)latitude, (float)longitude)}");
                }
            }

            var redeemResponse = await client.PostAsync($"/1/api/v1.1/tappables/{tileId}", redeemContent);
        if (redeemResponse.StatusCode != HttpStatusCode.OK)
        {
            var redeemBodyDbg = await redeemResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"POST /tappables/{tileId} returned {redeemResponse.StatusCode}:");
            Console.WriteLine(redeemBodyDbg);
        }
        await Assert.That(redeemResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var redeemBody = await redeemResponse.Content.ReadAsStringAsync();
        using (var document = JsonDocument.Parse(redeemBody))
        {
            var result = document.RootElement.GetProperty("result");
            await Assert.That(result.GetProperty("token").GetProperty("clientType").GetString()).IsEqualTo("redeemtappable");
            await Assert.That(result.GetProperty("updates").ValueKind).IsEqualTo(JsonValueKind.Null);
        }

        using (var scope = host.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EarthDbContext>();
            var redeemed = await db.RedeemedTappables.FindAsync(accountId);
            await Assert.That(redeemed).IsNotNull();
            await Assert.That(redeemed!.IsRedeemed(tappableId)).IsTrue();
        }
    }

    [Test]
    public async Task RedeemTappableEndToEnd_SpawnerProducesTappable()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        await using var host = await BuildTestHostWithGeneratorAsync(typeof(TappablesManager).Assembly, connection);
        var client = host.App.GetTestClient();

        var accountId = Guid.NewGuid();
        using (var scope = host.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EarthDbContext>();
            await db.EnsureAccountExists(accountId);
        }

        client.DefaultRequestHeaders.Add("X-Account-Id", accountId.ToString());

        double latitude = 51.0;
        double longitude = 0.0;
        string tileId = string.Empty;

        Guid? tappableId = null;
        const int maxAttempts = 10;
        for (int attempt = 0; attempt < maxAttempts && tappableId is null; attempt++)
        {
            var locationsResponse = await client.GetAsync($"/1/api/v1.1/locations/{latitude}/{longitude}");
            await Assert.That(locationsResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

            var locationsBody = await locationsResponse.Content.ReadAsStringAsync();
            if (locationsResponse.StatusCode != HttpStatusCode.OK)
            {
                Console.WriteLine($"GET /locations attempt {attempt} returned {locationsResponse.StatusCode}");
                Console.WriteLine(locationsBody);
            }
            using (var document = JsonDocument.Parse(locationsBody))
            {
                var activeLocations = document.RootElement.GetProperty("result").GetProperty("activeLocations");
                if (activeLocations.GetArrayLength() > 0)
                {
                    var activeLocation = activeLocations[0];
                    tappableId = activeLocation.GetProperty("id").GetGuid();
                    tileId = activeLocation.GetProperty("tileId").GetString()!;
                    break;
                }
            }

            await Task.Delay(500);
        }

        await Assert.That(tappableId).IsNotNull();

        var redeemRequest = new
        {
            id = tappableId.Value,
            playerCoordinate = new { latitude, longitude }
        };

        var redeemContent = new StringContent(JsonSerializer.Serialize(redeemRequest), Encoding.UTF8, "application/json");
        var redeemResponse = await client.PostAsync($"/1/api/v1.1/tappables/{tileId}", redeemContent);
        await Assert.That(redeemResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var redeemBody = await redeemResponse.Content.ReadAsStringAsync();
        using (var document = JsonDocument.Parse(redeemBody))
        {
            var result = document.RootElement.GetProperty("result");
            await Assert.That(result.GetProperty("token").GetProperty("clientType").GetString()).IsEqualTo("redeemtappable");
            await Assert.That(result.GetProperty("updates").ValueKind).IsEqualTo(JsonValueKind.Null);
        }

        using (var scope = host.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EarthDbContext>();
            var redeemed = await db.RedeemedTappables.FindAsync(accountId);
            await Assert.That(redeemed).IsNotNull();
            await Assert.That(redeemed!.IsRedeemed(tappableId.Value)).IsTrue();
        }
    }

    private static async Task<IntegrationTestHost> BuildTestHostWithGeneratorAsync(System.Reflection.Assembly controllersAssembly, SqliteConnection connection)
    {
        var host = await BuildTestHostAsync(controllersAssembly, connection);

        var staticData = host.App.Services.GetRequiredService<StaticData.StaticData>();
        var tappableGenerator = new TappableGenerator(staticData);
        var encounterGenerator = new EncounterGenerator(staticData);

        Spawner? spawner = null;
        var activeTiles = await ActiveTiles.CreateAsync(host.EventBusClient, new ActiveTiles.ActiveTileListener(
            activeTiles => spawner!.SpawnTiles(activeTiles),
            _ => Task.CompletedTask
        ));

        spawner = await Spawner.CreateAsync(host.EventBusClient, activeTiles, tappableGenerator, encounterGenerator);

        return host;
    }

    private static async Task<IntegrationTestHost> BuildTestHostAsync(System.Reflection.Assembly controllersAssembly, SqliteConnection connection)
    {
        // Start an in-process EventBus server on ephemeral port
        var server = new Server();
        var networkServer = new NetworkServer(server, 0);
        _ = Task.Run(async () => await networkServer.RunAsync());

        // wait for the server to assign an actual port
        int port = 0;
        var listenerField = typeof(NetworkServer).GetField("_serverSocket", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (listenerField is not null)
        {
            var listener = listenerField.GetValue(networkServer) as System.Net.Sockets.TcpListener;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (listener is not null && port == 0)
            {
                if (listener.LocalEndpoint is System.Net.IPEndPoint endpoint && endpoint.Port != 0)
                {
                    port = endpoint.Port;
                }
                else if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("Timed out waiting for EventBus server port");
                }
                else
                {
                    await Task.Delay(10);
                }
            }
        }

        var eventBusClient = await EventBusClient.ConnectAsync($"127.0.0.1:{port}");

        var tappablesManager = await TappablesManager.CreateAsync(eventBusClient);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers()
            .ConfigureApplicationPartManager(manager =>
            {
                manager.ApplicationParts.Clear();
                manager.ApplicationParts.Add(new AssemblyPart(controllersAssembly));
                manager.FeatureProviders.Add(new InternalControllerFeatureProvider());
            });

        // Add simple auth scheme for tests
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });
        builder.Services.AddAuthorization();

        builder.Services.AddApiVersioning(config =>
        {
            config.DefaultApiVersion = new Asp.Versioning.ApiVersion(1, 1);
            config.AssumeDefaultVersionWhenUnspecified = true;
            config.ReportApiVersions = true;
        });

        builder.Services.AddSingleton(tappablesManager);

        string staticDataPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "staticdata"));
        if (!Directory.Exists(staticDataPath))
        {
            staticDataPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "staticdata"));
        }

        builder.Services.AddSingleton(new StaticData.StaticData(staticDataPath));
        builder.Services.AddDbContext<EarthDbContext>(options => options.UseSqlite(connection));

        var app = builder.Build();

        // Ensure the request timestamp middleware is present in the test host (matches Program.cs)
        app.Use(async (context, next) =>
        {
            context.Items.Add("RequestStartedOn", DateTimeOffset.UtcNow);
            await next();
        });

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        await app.StartAsync();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EarthDbContext>();
        await db.Database.EnsureCreatedAsync();

        return new IntegrationTestHost(app, eventBusClient, networkServer, server);
    }

    private sealed class IntegrationTestHost : IAsyncDisposable
    {
        public IntegrationTestHost(WebApplication app, EventBusClient eventBusClient, NetworkServer networkServer, Server eventBusServer)
        {
            App = app;
            EventBusClient = eventBusClient;
            NetworkServer = networkServer;
            EventBusServer = eventBusServer;
        }

        public WebApplication App { get; }

        public EventBusClient EventBusClient { get; }

        public NetworkServer NetworkServer { get; }

        public Server EventBusServer { get; }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await EventBusClient.DisposeAsync();
            NetworkServer.Dispose();
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Account-Id", out var accountIds) || accountIds.Count == 0 || !Guid.TryParse(accountIds[0], out var accountId))
            {
                return Task.FromResult(AuthenticateResult.Fail("Missing or invalid X-Account-Id header"));
            }

            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, accountId.ToString()) };
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class InternalControllerFeatureProvider : ControllerFeatureProvider
    {
        private const string ControllerTypeNameSuffix = "Controller";

        protected override bool IsController(System.Reflection.TypeInfo typeInfo)
        {
            if (!typeInfo.IsClass)
            {
                return false;
            }

            if (typeInfo.IsAbstract)
            {
                return false;
            }

            if (typeInfo.ContainsGenericParameters)
            {
                return false;
            }

            if (typeInfo.IsDefined(typeof(NonControllerAttribute)))
            {
                return false;
            }

            if (typeInfo.IsAssignableTo(typeof(ControllerBase)))
            {
                return true;
            }

            if (!typeInfo.Name.EndsWith(ControllerTypeNameSuffix, StringComparison.OrdinalIgnoreCase) &&
                !typeInfo.IsDefined(typeof(ControllerAttribute)))
            {
                return false;
            }

            return true;
        }
    }
}
