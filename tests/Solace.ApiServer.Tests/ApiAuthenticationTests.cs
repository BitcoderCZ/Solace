using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Solace.ApiServer.Authentication;
using Solace.ApiServer.Utils;

namespace Solace.ApiServer.Tests;

[ApiController]
[Authorize]
[Route("test/auth")]
internal sealed class AuthTestController : ControllerBase
{
    [HttpGet]
    public IResult Get() => TypedResults.Ok(new { authenticated = true });
}

public class ApiAuthenticationTests
{
    [Test]
    public async Task AuthenticatedEndpoint_ReturnsUnauthorizedWithoutHeader()
    {
        await using var app = await BuildAuthHostAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/test/auth");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task AuthenticatedEndpoint_ReturnsOkWithGenoaAuthorizationHeader()
    {
        await using var app = await BuildAuthHostAsync();
        var client = app.GetTestClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Genoa", "test-user-id");
        var response = await client.GetAsync("/test/auth");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("\"authenticated\":true");
    }

    private static async Task<WebApplication> BuildAuthHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddControllers()
            .ConfigureApplicationPartManager(manager =>
            {
                manager.ApplicationParts.Clear();
                manager.ApplicationParts.Add(new AssemblyPart(typeof(AuthTestController).Assembly));
                manager.FeatureProviders.Add(new InternalControllerFeatureProvider());
            });

        builder.Services.AddAuthentication("GenoaAuth")
            .AddScheme<AuthenticationSchemeOptions, GenoaAuthenticationHandler>("GenoaAuth", null);

        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        await app.StartAsync();
        return app;
    }
}
