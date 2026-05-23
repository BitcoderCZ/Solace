using System.Net;
using Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Solace.ApiServer.Controllers.EarthApi;
using Solace.ApiServer.Utils;

namespace Solace.ApiServer.Tests;

public class ApiIntegrationTests
{
    [Test]
    public async Task LocatorController_Get_ReturnsProductionEnvironmentJson()
    {
        await using var app = await BuildTestHostAsync(typeof(LocatorController));
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
        await using var app = await BuildTestHostAsync(typeof(MojankLocatorController));
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/v1.1/player/environment");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");

        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("\"production\"");
    }

    private static async Task<WebApplication> BuildTestHostAsync(Type controllerType)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers()
            .ConfigureApplicationPartManager(manager =>
            {
                manager.ApplicationParts.Clear();
                manager.ApplicationParts.Add(new AssemblyPart(controllerType.Assembly));
                manager.FeatureProviders.Add(new InternalControllerFeatureProvider());
            });

        builder.Services.AddApiVersioning(config =>
        {
            config.DefaultApiVersion = new ApiVersion(1, 1);
            config.AssumeDefaultVersionWhenUnspecified = true;
            config.ReportApiVersions = true;
        });

        var app = builder.Build();
        app.MapControllers();

        await app.StartAsync();
        return app;
    }
}
