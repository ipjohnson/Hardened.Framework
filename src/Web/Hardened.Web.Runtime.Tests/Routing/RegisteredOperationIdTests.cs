using Hardened.Web.Runtime.Routing;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Routing;

/// <summary>
/// The <c>operationId</c> a registered route publishes.
/// </summary>
/// <remarks>
/// Derived at registration rather than at build, because one registration serves every path it is
/// registered at. Eighteen lambda registrations in the 0.36 trial published <c>funcInvoke</c>
/// eighteen times, and a declared handler registered at three paths published its own id three
/// times; both are one id on several operations, which OpenAPI forbids and which a client
/// generator resolves by numbering the duplicates.
/// </remarks>
public class RegisteredOperationIdTests
{
    [Theory]
    [InlineData("GET", "/summary", "summaryGet")]
    [InlineData("POST", "/orders", "ordersPost")]
    [InlineData("DELETE", "/orders/{id}", "ordersByIdDelete")]
    [InlineData("GET", "/api/north-yard/readings/{id}", "apiNorthYardReadingsByIdGet")]
    [InlineData("GET", "/files/{*path}", "filesByPathGet")]
    [InlineData("PATCH", "/", "patch")]
    public void ThePathAndTheVerbNameTheOperation(string method, string path, string expected) =>
        Assert.Equal(expected, RegisteredOperationId.For(method, path));

    /// <summary>
    /// A constraint is a routing concern the document does not carry, so it does not reach the
    /// name either. The registry writes the id from the template it already stripped, and this
    /// says the same thing again for a path handed over unstripped.
    /// </summary>
    [Fact]
    public void AConstraintOnATokenIsNotPartOfTheName() =>
        Assert.Equal(
            RegisteredOperationId.For("GET", "/orders/{id}"),
            RegisteredOperationId.For("GET", "/orders/{id:int}")
        );

    /// <summary>
    /// Only the first letter of a word moves. A segment written in camel case keeps the casing its
    /// author gave it.
    /// </summary>
    [Fact]
    public void ASegmentKeepsTheCasingItWasWrittenWith() =>
        Assert.Equal("apiNorthYardGet", RegisteredOperationId.For("GET", "/api/northYard"));

    /// <summary>
    /// The property the whole scheme rests on: two routes cannot register the same verb at the
    /// same path, so verb and path together are unique and no counter is needed.
    /// </summary>
    [Fact]
    public void RoutesDifferingOnlyByVerbOrPathGetDifferentNames()
    {
        var names = new[]
        {
            RegisteredOperationId.For("GET", "/acme/summary"),
            RegisteredOperationId.For("GET", "/globex/summary"),
            RegisteredOperationId.For("POST", "/acme/summary"),
        };

        Assert.Equal(names.Length, names.Distinct().Count());
    }
}
