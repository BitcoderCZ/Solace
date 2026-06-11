using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Solace.LauncherUI.Controllers;

[ApiController]
[Authorize(Policy = Permissions.ManagePlayers)]
[Route("api/genoa-textures")]
internal sealed class GenoaTexturesController : ControllerBase
{
    [HttpGet("{category}/{name}.png")]
    [HttpHead("{category}/{name}.png")]
    public async Task<Results<PhysicalFileHttpResult, NotFound>> GeTexture(string category, string name)
    {
        var cachePath = await GenoaResourcepackCache.GetCachePath();

        if (cachePath is null)
        {
            return TypedResults.NotFound();
        }

        var path = Path.Combine(cachePath, "textures", category, name + ".png");

        if (!System.IO.File.Exists(path))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.PhysicalFile(path);
    }
}