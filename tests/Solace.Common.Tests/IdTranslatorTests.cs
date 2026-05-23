using System;
using Solace.Common;

namespace Solace.Common.Tests;

public class IdTranslatorTests
{
    [Test]
    public async Task ToGuid_ShouldReturnEmptyForEmptyString()
    {
        var result = IdTranslator.ToGuid("");

        await Assert.That(result).IsEqualTo(Guid.Empty);
    }

    [Test]
    public async Task ToGuid_ShouldParseGuidString()
    {
        var input = Guid.NewGuid().ToString();
        var result = IdTranslator.ToGuid(input);

        await Assert.That(result).IsEqualTo(Guid.Parse(input));
    }

    [Test]
    public async Task ToGuid_ShouldConvertShortHexString()
    {
        var input = "00112233445566778899AABBCCDDEEFF";
        var result = IdTranslator.ToGuid(input);

        await Assert.That(result).IsEqualTo(Guid.ParseExact(input, "N"));
    }

    [Test]
    public async Task ToGuid_ShouldHashLongStringConsistently()
    {
        var input = "this-is-a-longer-id-that-is-not-a-hex-guid";
        var first = IdTranslator.ToGuid(input);
        var second = IdTranslator.ToGuid(input);

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task ToGuid_ShouldHashInvalidShortHexString()
    {
        var input = "zz11zz11";
        var result = IdTranslator.ToGuid(input);

        await Assert.That(result).IsNotEqualTo(Guid.Empty);
    }
}
