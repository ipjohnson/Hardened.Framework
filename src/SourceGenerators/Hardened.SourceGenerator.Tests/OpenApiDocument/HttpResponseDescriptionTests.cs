using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Xunit;

namespace Hardened.SourceGenerator.Tests.OpenApiDocument;

/// <summary>
/// The <c>description</c> every response object carries, and the equality that keeps a response set
/// out of the incremental cache when it changes.
/// </summary>
/// <remarks>
/// These are read by whoever reads the document, so the phrase is the status's registered name
/// rather than something invented. A table is only as good as the entry nobody checked, which is
/// why every one of them is asserted rather than a sample.
/// </remarks>
public class HttpResponseDescriptionTests {

    [Theory]
    [InlineData(200, "OK")]
    [InlineData(201, "Created")]
    [InlineData(202, "Accepted")]
    [InlineData(204, "No Content")]
    [InlineData(206, "Partial Content")]
    [InlineData(301, "Moved Permanently")]
    [InlineData(302, "Found")]
    [InlineData(303, "See Other")]
    [InlineData(304, "Not Modified")]
    [InlineData(307, "Temporary Redirect")]
    [InlineData(308, "Permanent Redirect")]
    [InlineData(400, "Bad Request")]
    [InlineData(401, "Unauthorized")]
    [InlineData(402, "Payment Required")]
    [InlineData(403, "Forbidden")]
    [InlineData(404, "Not Found")]
    [InlineData(405, "Method Not Allowed")]
    [InlineData(406, "Not Acceptable")]
    [InlineData(408, "Request Timeout")]
    [InlineData(409, "Conflict")]
    [InlineData(410, "Gone")]
    [InlineData(412, "Precondition Failed")]
    [InlineData(413, "Content Too Large")]
    [InlineData(415, "Unsupported Media Type")]
    [InlineData(422, "Unprocessable Content")]
    [InlineData(428, "Precondition Required")]
    [InlineData(429, "Too Many Requests")]
    [InlineData(500, "Internal Server Error")]
    [InlineData(501, "Not Implemented")]
    [InlineData(502, "Bad Gateway")]
    [InlineData(503, "Service Unavailable")]
    [InlineData(504, "Gateway Timeout")]
    public void EveryListedStatusHasItsRegisteredName(int status, string expected) {
        Assert.Equal(expected, HttpResponseDescription.For(status));
    }

    /// <summary>
    /// An unlisted status names itself rather than saying "Error", so a document declaring an
    /// unusual code still tells a reader which one - and never calls a 2xx a failure.
    /// </summary>
    [Theory]
    [InlineData(207)]
    [InlineData(418)]
    [InlineData(599)]
    public void AnUnlistedStatusNamesItself(int status) {
        var description = HttpResponseDescription.For(status);

        Assert.Contains(status.ToString(), description, StringComparison.Ordinal);
        Assert.DoesNotContain("Error", description, StringComparison.Ordinal);
    }

    #region response set equality

    private static HandlerSchema Schema(string json) =>
        new(json, new[] { new SchemaComponent("Todo", json) });

    /// <summary>
    /// This reaches RequestHandlerModel's equality, which is a Roslyn cache key. A reference
    /// comparison here would report two identical response sets as different on every edit and is
    /// one refactor from reporting two different ones as the same.
    /// </summary>
    [Fact]
    public void IdenticallyBuiltResponsesAreEqual() {
        var first = new ResponseSchemaModel(404, "Not Found", Schema("{}"));
        var second = new ResponseSchemaModel(404, "Not Found", Schema("{}"));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void ResponsesDifferingInAnyMemberAreNotEqual() {
        var baseline = new ResponseSchemaModel(404, "Not Found", Schema("{}"));

        Assert.NotEqual(baseline, new ResponseSchemaModel(409, "Not Found", Schema("{}")));
        Assert.NotEqual(baseline, new ResponseSchemaModel(404, "Gone", Schema("{}")));
        Assert.NotEqual(baseline, new ResponseSchemaModel(404, "Not Found", Schema("{\"a\":1}")));
    }

    /// <summary>
    /// A status that declares no body is not the same as one whose body happens to be empty - the
    /// first tells a generated client not to wait for one.
    /// </summary>
    [Fact]
    public void ABodylessResponseIsNotEqualToOneWithABody() {
        Assert.NotEqual(
            new ResponseSchemaModel(204, "No Content", null),
            new ResponseSchemaModel(204, "No Content", Schema("{}")));
    }

    [Fact]
    public void AResponseIsNotEqualToAnUnrelatedObject() {
        Assert.False(new ResponseSchemaModel(404, "Not Found", null).Equals("not a response"));
    }

    [Fact]
    public void TwoBodylessResponsesAreEqual() {
        var first = new ResponseSchemaModel(204, "No Content", null);
        var second = new ResponseSchemaModel(204, "No Content", null);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    #endregion
}
