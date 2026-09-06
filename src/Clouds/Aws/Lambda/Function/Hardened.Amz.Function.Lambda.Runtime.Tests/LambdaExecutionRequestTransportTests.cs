using Hardened.Amz.Function.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Amz.Function.Lambda.Runtime.Tests;

/// <summary>
/// A direct invocation has no connection, and says so.
/// </summary>
/// <remarks>
/// Worth asserting rather than assuming: the alternative to an empty transport is a null one, and
/// then every caller checks before asking. The framework's contract is that the property is always
/// answerable and the values may not be.
/// </remarks>
public class LambdaExecutionRequestTransportTests {

    private static LambdaExecutionRequest Request() =>
        new("POST", "/consume", Stream.Null, new Dictionary<string, StringValues>());

    [Fact]
    public void TheTransportIsEmptyRatherThanNull() {
        var request = Request();

        Assert.NotNull(request.Transport);
        Assert.Empty(request.Transport.Keys);
    }

    /// <summary>
    /// And every key answers null, including the ones a web transport would fill.
    /// </summary>
    /// <remarks>
    /// A function invoked through the Lambda API - by the SDK, by an event source, by a console -
    /// has no client address, no protocol version and no scheme. Deriving one from the invoking
    /// identity would put something in a field callers read as the caller's network address.
    /// </remarks>
    [Theory]
    [InlineData(KnownTransportKeys.ClientAddress)]
    [InlineData(KnownTransportKeys.NetworkPeerAddress)]
    [InlineData(KnownTransportKeys.ServerAddress)]
    [InlineData(KnownTransportKeys.UrlScheme)]
    public void EveryKeyAnswersNull(string key) {
        Assert.Null(Request().Transport.Get(key));
    }

    [Fact]
    public void ACloneKeepsAnEmptyTransport() {
        var clone = Request().Clone(method: "DELETE");

        Assert.NotNull(clone.Transport);
        Assert.Empty(clone.Transport.Keys);
    }
}
