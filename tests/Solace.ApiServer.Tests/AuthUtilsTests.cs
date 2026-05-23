using System;
using System.Security.Cryptography;
using Solace.ApiServer.Models;
using Solace.ApiServer.Utils;

namespace Solace.ApiServer.Tests;

public class AuthUtilsTests
{
    [Test]
    public async Task XboxAuthorizationUtils_Parse_ReturnsNullWhenHeaderIsMissing()
    {
        var result = XboxAuthorizationUtils.Parse(null);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task XboxAuthorizationUtils_Parse_ReturnsNullForInvalidScheme()
    {
        var result = XboxAuthorizationUtils.Parse("Bearer abcdef");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task XboxAuthorizationUtils_Parse_ReturnsUserIdAndTokenForValidHeader()
    {
        var userId = Guid.NewGuid();
        var token = "token123";
        var header = $"XBL3.0 x={userId};{token}";

        var result = XboxAuthorizationUtils.Parse(header);

        await Assert.That(result).IsNotNull();
        await Assert.That(result?.UserId).IsEqualTo(userId);
        await Assert.That(result?.TokenString).IsEqualTo(token);
    }

    [Test]
    public async Task JwtUtils_SignAndVerify_ReturnsOriginalTokenPayload()
    {
        var secret = RandomNumberGenerator.GetBytes(64);
        var issued = DateTimeOffset.UtcNow;
        var expires = issued.AddMinutes(10);
        var data = new Tokens.Xbox.XapiToken(Guid.NewGuid(), "tester");
        var validity = new ValidityDatePair(issued, expires);

        var signed = JwtUtils.Sign(data, secret, validity);
        var verified = JwtUtils.Verify<Tokens.Xbox.XapiToken>(signed, secret);

        await Assert.That(verified).IsNotNull();
        await Assert.That(verified?.Data.UserId).IsEqualTo(data.UserId);
        await Assert.That(verified?.Data.Username).IsEqualTo(data.Username);
    }

    [Test]
    public async Task JwtUtils_Verify_ReturnsNullForInvalidSignature()
    {
        var secret = RandomNumberGenerator.GetBytes(64);
        var otherSecret = RandomNumberGenerator.GetBytes(64);
        var issued = DateTimeOffset.UtcNow;
        var expires = issued.AddMinutes(10);
        var data = new Tokens.Xbox.XapiToken(Guid.NewGuid(), "tester");
        var validity = new ValidityDatePair(issued, expires);

        var signed = JwtUtils.Sign(data, secret, validity);
        var verified = JwtUtils.Verify<Tokens.Xbox.XapiToken>(signed, otherSecret);

        await Assert.That(verified).IsNull();
    }
}
