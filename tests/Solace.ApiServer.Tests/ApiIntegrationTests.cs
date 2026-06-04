using System.Net;
using System.Text;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Solace.ApiServer.Authentication;
using Solace.ApiServer.Controllers;
using Solace.ApiServer.Controllers.EarthApi;
using Solace.ApiServer.Controllers.PlayfabApi;
using Solace.ApiServer.Controllers.XboxLive.Auth;
using Solace.ApiServer.Models;
using Solace.ApiServer.Utils;
using Solace.DB;
using Solace.DB.Models;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Solace.ApiServer.Tests;

public class ApiIntegrationTests
{
    [Test]
    public async Task LocatorController_Get_ReturnsProductionEnvironmentJson()
    {
        var host = await BuildTestHostAsync(typeof(LocatorController));
        await using var app = host.App;

        var client = app.GetTestClient();

        var response = await client.GetAsync("/player/environment");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");

        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("\"production\"");
    }

    [Test]
    public async Task MojankLocatorController_Get_ReturnsProductionEnvironmentJson()
    {
        var host = await BuildTestHostAsync(typeof(MojankLocatorController));
        await using var app = host.App;
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/v1.1/player/environment");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");

        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("\"production\"");
    }

    [Test]
    public async Task UserController_Authenticate_ReturnsXapiAuthToken()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var host = await BuildTestHostAsync([typeof(UserController)], connection);
        await using var app = host.App;
        var secrets = host.Secrets;
        var client = app.GetTestClient();

        var userId = Guid.NewGuid();
        var ticket = new Tokens.Shared.XboxTicketToken(userId, "PlayerOne");
        var rpsTicket = JwtUtils.Sign(ticket, secrets.LoginXboxTokenSecret, ValidityDatePair.Create(Config.Default.Login.XboxTokenValidityMinutes));

        var request = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                RpsTicket = rpsTicket,
                SiteName = "user.auth.xboxlive.com",
            },
            RelyingParty = "http://xboxlive.com",
            TokenType = "JWT",
        };

        var response = await client.PostAsync("/user/authenticate", new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        string token = root.GetProperty("Token").GetString()!;
        await Assert.That(token).IsNotNull();

        var verified = JwtUtils.Verify<Tokens.Xbox.AuthToken>(token, secrets.LiveAuthTokenSecret);
        await Assert.That(verified).IsNotNull();
        await Assert.That(verified!.Data is Tokens.Xbox.UserToken).IsTrue();
        var userToken = (Tokens.Xbox.UserToken)verified.Data;
        await Assert.That(userToken.UserId).IsEqualTo(userId);
        await Assert.That(root.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString()).IsEqualTo(userId.ToString());
    }

    [Test]
    public async Task XstsController_Authenticate_ReturnsPlayfabTokenForMinecraftPlayfab()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var host = await BuildTestHostAsync([typeof(XstsController)], connection);
        await using var app = host.App;
        var secrets = host.Secrets;
        var client = app.GetTestClient();

        var userId = Guid.NewGuid();
        var deviceToken = JwtUtils.Sign<Tokens.Xbox.AuthToken>(new Tokens.Xbox.DeviceToken { Did = "device-1" }, secrets.LiveAuthTokenSecret, ValidityDatePair.Create(Config.Default.XboxLive.TokenValidityMinutes));
        var titleToken = JwtUtils.Sign<Tokens.Xbox.AuthToken>(new Tokens.Xbox.TitleToken { Tid = "title-1" }, secrets.LiveAuthTokenSecret, ValidityDatePair.Create(Config.Default.XboxLive.TokenValidityMinutes));
        var userToken = JwtUtils.Sign<Tokens.Xbox.AuthToken>(new Tokens.Xbox.UserToken { Xid = userId, Uhs = userId, UserId = userId, Username = "PlayerOne" }, secrets.LiveAuthTokenSecret, ValidityDatePair.Create(Config.Default.XboxLive.TokenValidityMinutes));

        var request = new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                DeviceToken = deviceToken,
                TitleToken = titleToken,
                UserTokens = new[] { userToken },
            },
            RelyingParty = "https://b980a380.minecraft.playfabapi.com/",
            TokenType = "JWT",
        };

        var response = await client.PostAsync("/xsts/authorize", new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        string token = root.GetProperty("Token").GetString()!;
        await Assert.That(token).IsNotNull();

        var verified = JwtUtils.Verify<Tokens.Shared.PlayfabXboxToken>(token, secrets.LivePlayfabTokenSecret);
        await Assert.That(verified).IsNotNull();
        await Assert.That(verified!.Data.UserId).IsEqualTo(userId);
    }

    [Test]
    public async Task LoginController_LoginWithXbox_ReturnsPlayfabSessionAndEntityTokens_WhenAccountExists()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var host = await BuildTestHostAsync([typeof(LoginController), typeof(XstsController)], connection);
        await using var app = host.App;
        var secrets = host.Secrets;
        var client = app.GetTestClient();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EarthDbContext>();
            var userId = Guid.NewGuid();
            db.Accounts.Add(new Account
            {
                Id = userId,
                CreatedDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Username = "player",
                ProfilePictureUrl = "https://example.com/pfp.png",
                PasswordSalt = new byte[16],
                PasswordHash = new byte[32],
            });
            await db.SaveChangesAsync();

            var deviceToken = JwtUtils.Sign<Tokens.Xbox.AuthToken>(new Tokens.Xbox.DeviceToken { Did = "device-1" }, secrets.LiveAuthTokenSecret, ValidityDatePair.Create(Config.Default.XboxLive.TokenValidityMinutes));
            var titleToken = JwtUtils.Sign<Tokens.Xbox.AuthToken>(new Tokens.Xbox.TitleToken { Tid = "title-1" }, secrets.LiveAuthTokenSecret, ValidityDatePair.Create(Config.Default.XboxLive.TokenValidityMinutes));
            var userToken = JwtUtils.Sign<Tokens.Xbox.AuthToken>(new Tokens.Xbox.UserToken { Xid = userId, Uhs = userId, UserId = userId, Username = "PlayerOne" }, secrets.LiveAuthTokenSecret, ValidityDatePair.Create(Config.Default.XboxLive.TokenValidityMinutes));

            var xstsRequest = new
            {
                Properties = new
                {
                    SandboxId = "RETAIL",
                    DeviceToken = deviceToken,
                    TitleToken = titleToken,
                    UserTokens = new[] { userToken },
                },
                RelyingParty = "https://b980a380.minecraft.playfabapi.com/",
                TokenType = "JWT",
            };

            var xstsResponse = await client.PostAsync("/xsts/authorize", new StringContent(JsonSerializer.Serialize(xstsRequest), Encoding.UTF8, "application/json"));
            await Assert.That(xstsResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

            using var xstsDocument = JsonDocument.Parse(await xstsResponse.Content.ReadAsStringAsync());
            var xstsToken = xstsDocument.RootElement.GetProperty("Token").GetString()!;

            var loginRequest = new
            {
                TitleId = "ABCDE",
                EncryptedRequest = (object?)null,
                PlayerSecret = (object?)null,
                CreateAccount = false,
                XboxToken = $"XBL3.0 x={userId};{xstsToken}",
            };

            var loginResponse = await client.PostAsync("/Client/LoginWithXbox", new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json"));
            await Assert.That(loginResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

            using var loginDocument = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
            var loginRoot = loginDocument.RootElement;
            var loginData = loginRoot.GetProperty("data");

            await Assert.That(loginData.GetProperty("PlayFabId").GetGuid()).IsEqualTo(userId);
            var sessionTicket = loginData.GetProperty("SessionTicket").GetString()!;
            await Assert.That(sessionTicket).StartsWith(userId.ToString().ToUpperInvariant() + "-");

            var entityTokenString = loginData.GetProperty("EntityToken").GetProperty("EntityToken").GetString()!;
            var verifiedEntityToken = JwtUtils.Verify<Tokens.Playfab.EntityToken>(entityTokenString, secrets.PlayfabEntityTokenSecret);
            await Assert.That(verifiedEntityToken).IsNotNull();
            await Assert.That(verifiedEntityToken!.Data.Id).IsEqualTo(userId);
        }
    }

    [Test]
    public async Task AuthenticationController_GetEntityToken_ReturnsEntityTokenForMasterPlayerAccount()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var host = await BuildTestHostAsync([typeof(AuthenticationController)], connection);
        await using var app = host.App;
        var secrets = host.Secrets;
        var client = app.GetTestClient();

        var entityId = Guid.NewGuid();
        var entityToken = JwtUtils.Sign(new Tokens.Playfab.EntityToken(entityId, "title_player_account"), secrets.PlayfabEntityTokenSecret, ValidityDatePair.Create(Config.Default.PlayfabApi.EntityTokenValidityMinutes));
        var request = new
        {
            Entity = new
            {
                Id = entityId,
                Type = "master_player_account",
            },
        };

        var message = new HttpRequestMessage(HttpMethod.Post, "/Authentication/GetEntityToken")
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("X-EntityToken", entityToken);

        var response = await client.SendAsync(message);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var returnedEntityId = root.GetProperty("data").GetProperty("Entity").GetProperty("Id").GetGuid();
        await Assert.That(returnedEntityId).IsEqualTo(entityId);
    }

    [Test]
    public async Task SigninController_Post_ReturnsAuthenticationToken_ForValidSessionTicket()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var host = await BuildTestHostAsync([typeof(SigninController)], connection);
        await using var app = host.App;
        var secrets = host.Secrets;
        var client = app.GetTestClient();

        var userId = Guid.NewGuid();
        var sessionTicketJwt = JwtUtils.Sign(new Tokens.Shared.PlayfabSessionTicket(userId), secrets.PlayfabSessionTicketSecret, ValidityDatePair.Create(Config.Default.PlayfabApi.SessionTicketValidityMinutes));
        var sessionTicket = $"{userId:D}-{sessionTicketJwt}";
        var request = new
        {
            SessionTicket = sessionTicket,
        };

        var response = await client.PostAsync("/api/v1.1/player/profile/signin", new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var authenticationToken = root.GetProperty("result").GetProperty("authenticationToken").GetString();
        await Assert.That(authenticationToken).IsNotNull();

        var protector = app.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(GenoaAuthenticationHandler.DataProtectionPurpose)
            .ToTimeLimitedDataProtector();
        await Assert.That(protector.Unprotect(authenticationToken!)).IsEqualTo(userId.ToString());
    }

    private static Task<(WebApplication App, CryptoSecrets Secrets)> BuildTestHostAsync(Type controllerType)
        => BuildTestHostAsync([controllerType], null);

    private static async Task<(WebApplication App, CryptoSecrets Secrets)> BuildTestHostAsync(Type[] controllerTypes, SqliteConnection? connection)
    {
        Program.config = Config.Default;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers()
            .ConfigureApplicationPartManager(manager =>
            {
                manager.ApplicationParts.Clear();
                manager.ApplicationParts.Add(new AssemblyPart(controllerTypes[0].Assembly));
                manager.FeatureProviders.Add(new InternalControllerFeatureProvider());
            });

        builder.Services.AddApiVersioning(config =>
        {
            config.DefaultApiVersion = new ApiVersion(1, 1);
            config.AssumeDefaultVersionWhenUnspecified = true;
            config.ReportApiVersions = true;
        });

        if (connection is not null)
        {
            builder.Services.AddDbContext<EarthDbContext>(options => options.UseSqlite(connection));
        }

        builder.Services.AddDataProtection();
        builder.Services.AddAuthentication("GenoaAuth")
            .AddScheme<AuthenticationSchemeOptions, GenoaAuthenticationHandler>("GenoaAuth", null);
        builder.Services.AddAuthorization();

        CryptoSecrets cryptoSecrets;
        if (connection is not null)
        {
            await using var tempProvider = builder.Services.BuildServiceProvider();
            using (var scope = tempProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EarthDbContext>();
                await db.Database.EnsureCreatedAsync();
                cryptoSecrets = await db.GetOrInitializeSecretsAsync();
            }
        }
        else
        {
            cryptoSecrets = CryptoSecrets.CreateRandom();
        }

        builder.Services.AddSingleton(cryptoSecrets);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        await app.StartAsync();

        return (app, cryptoSecrets);
    }
}
