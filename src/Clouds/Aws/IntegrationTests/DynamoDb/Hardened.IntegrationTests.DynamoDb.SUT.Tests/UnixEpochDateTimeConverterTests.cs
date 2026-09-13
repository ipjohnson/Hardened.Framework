using System.Text.Json;
using Hardened.Aws.Lambda.DynamoDb;
using Xunit;

namespace Hardened.IntegrationTests.DynamoDb.SUT.Tests;

/// <summary>
/// The converter that stops every real invocation throwing.
/// </summary>
/// <remarks>
/// ApproximateCreationDateTime arrives as epoch seconds against a DateTime property, which
/// System.Text.Json refuses on its own. The adapter is only reached because this converter is
/// registered, so the number form is the case that matters; the string form exists for fixtures
/// written by hand.
/// </remarks>
public class UnixEpochDateTimeConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new UnixEpochDateTimeConverter() },
    };

    [Fact]
    public void EpochSeconds_ReadAsUtc()
    {
        var read = JsonSerializer.Deserialize<DateTime>("1767225600", Options);

        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), read);
    }

    /// <summary>
    /// Read through a double rather than a long, so a stream reporting sub-second creation times
    /// does not have the fraction truncated away.
    /// </summary>
    [Fact]
    public void FractionalSeconds_KeepTheirMilliseconds()
    {
        var read = JsonSerializer.Deserialize<DateTime>("1767225600.25", Options);

        Assert.Equal(250, read.Millisecond);
    }

    [Fact]
    public void AnIso8601String_IsAlsoAccepted()
    {
        var read = JsonSerializer.Deserialize<DateTime>("\"2026-01-01T00:00:00Z\"", Options);

        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), read.ToUniversalTime());
    }

    /// <summary>Written back as epoch seconds, so a round trip produces what AWS sent.</summary>
    [Fact]
    public void Writing_ProducesEpochSeconds()
    {
        var written = JsonSerializer.Serialize(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Options
        );

        Assert.Equal("1767225600", written);
    }
}
