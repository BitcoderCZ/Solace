using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Solace.ApiServer;

namespace Solace.ApiServer.Tests;

[NonController]
internal sealed class SolaceControllerBaseTestWrapper : SolaceControllerBase
{
    public bool InvokeTryGetAccountId(out Guid accountId)
        => TryGetAccountId(out accountId);

    public ContentHttpResult InvokeJsonCamelCase(object value)
        => JsonCamelCase(value);

    public ContentHttpResult InvokeJsonPascalCase(object value)
        => JsonPascalCase(value);
}

public class SolaceControllerBaseTests
{
    [Test]
    public async Task TryGetAccountId_ReturnsFalseWhenClaimMissing()
    {
        var controller = new SolaceControllerBaseTestWrapper
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = controller.InvokeTryGetAccountId(out var accountId);

        await Assert.That(result).IsFalse();
        await Assert.That(accountId).IsEqualTo(Guid.Empty);
    }

    [Test]
    public async Task TryGetAccountId_ReturnsTrueWhenClaimExists()
    {
        var expected = Guid.NewGuid();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, expected.ToString())
            ]))
        };

        var controller = new SolaceControllerBaseTestWrapper
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        var result = controller.InvokeTryGetAccountId(out var accountId);

        await Assert.That(result).IsTrue();
        await Assert.That(accountId).IsEqualTo(expected);
    }

    [Test]
    public async Task TryGetAccountId_ReturnsFalseWhenClaimContainsInvalidGuid()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "not-a-guid")
            ]))
        };

        var controller = new SolaceControllerBaseTestWrapper
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        var result = controller.InvokeTryGetAccountId(out var accountId);

        await Assert.That(result).IsFalse();
        await Assert.That(accountId).IsEqualTo(Guid.Empty);
    }

    [Test]
    public async Task JsonCamelCase_SerializesPropertyNamesInCamelCase()
    {
        var controller = new SolaceControllerBaseTestWrapper();
        var result = controller.InvokeJsonCamelCase(new { TestValue = 42 });

        await Assert.That(result.ResponseContent).Contains("\"testValue\"");
        await Assert.That(result.ContentType).IsEqualTo("application/json");
    }

    [Test]
    public async Task JsonPascalCase_SerializesPropertyNamesInPascalCase()
    {
        var controller = new SolaceControllerBaseTestWrapper();
        var result = controller.InvokeJsonPascalCase(new { TestValue = 42 });

        await Assert.That(result.ResponseContent).Contains("\"TestValue\"");
        await Assert.That(result.ContentType).IsEqualTo("application/json");
    }
}
