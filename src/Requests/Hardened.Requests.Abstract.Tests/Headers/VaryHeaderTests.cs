using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Requests.Abstract.Tests.Headers;

/// <summary>
/// <c>Vary</c> is merged, never assigned. Three filters write it, and while each assigned it the
/// last one to run erased what the others said - which for a cross-origin cached response is the
/// difference between a shared cache serving one origin's response to another or not.
/// </summary>
public class VaryHeaderTests {

    private static Dictionary<string, StringValues> Headers(StringValues? vary = null) {
        var headers = new Dictionary<string, StringValues>();

        if (vary.HasValue) {
            headers[KnownHeaders.Vary] = vary.Value;
        }

        return headers;
    }

    [Fact]
    public void AnAbsentVaryIsSetToTheName() {
        var headers = Headers();

        VaryHeader.Add(headers, KnownHeaders.AcceptEncoding);

        Assert.Equal("Accept-Encoding", headers[KnownHeaders.Vary].ToString());
    }

    [Fact]
    public void AnEmptyVaryIsSetToTheName() {
        var headers = Headers(StringValues.Empty);

        VaryHeader.Add(headers, KnownHeaders.Origin);

        Assert.Equal("Origin", headers[KnownHeaders.Vary].ToString());
    }

    [Fact]
    public void AnExistingVaryKeepsWhatItSaid() {
        var headers = Headers("Origin");

        VaryHeader.Add(headers, KnownHeaders.AcceptEncoding);

        Assert.Equal("Origin, Accept-Encoding", headers[KnownHeaders.Vary].ToString());
    }

    [Theory]
    [InlineData("Accept-Encoding")]
    [InlineData("accept-encoding")]
    [InlineData("Origin, Accept-Encoding")]
    [InlineData("Origin,Accept-Encoding")]
    public void ANameAlreadyListedIsNotListedTwice(string existing) {
        var headers = Headers(existing);

        VaryHeader.Add(headers, KnownHeaders.AcceptEncoding);

        var tokens = headers[KnownHeaders.Vary].ToString().Split(',', StringSplitOptions.TrimEntries);

        Assert.Single(tokens, token => string.Equals(token, "Accept-Encoding", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A star already covers every request header, so adding one to it would only confuse whoever
    /// reads it.
    /// </summary>
    [Fact]
    public void AStarIsLeftAlone() {
        var headers = Headers("*");

        VaryHeader.Add(headers, KnownHeaders.AcceptEncoding);

        Assert.Equal("*", headers[KnownHeaders.Vary].ToString());
    }

    /// <summary>
    /// A header that arrived as two values leaves as one, which is how it reads on the wire and
    /// how a cache compares it.
    /// </summary>
    [Fact]
    public void SeveralValuesAreMergedIntoOne() {
        var headers = Headers(new StringValues(["Origin", "Accept-Language"]));

        VaryHeader.Add(headers, KnownHeaders.AcceptEncoding);

        Assert.Single(headers[KnownHeaders.Vary].ToArray()!);
        Assert.Equal("Origin, Accept-Language, Accept-Encoding", headers[KnownHeaders.Vary].ToString());
    }
}
