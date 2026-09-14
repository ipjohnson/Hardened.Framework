using Hardened.Web.Runtime.Routing;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Routing;

/// <summary>
/// Writing a path that only exists at run time into a document written at build time.
/// </summary>
/// <remarks>
/// Nothing is parsed. The build wrote the document in two halves and the operation objects that go
/// between them, so this writes the path keys and the separators and nothing else.
/// </remarks>
public class RegisteredRouteDocumentTests
{
    private const string Empty = "{\"openapi\":\"3.1.0\",\"paths\":{";

    private const string Populated = "{\"openapi\":\"3.1.0\",\"paths\":{\"/declared\":{\"get\":{}}";

    private const string Suffix = "},\"components\":{}}";

    [Fact]
    public void APathIsWrittenIntoAnEmptyDocument()
    {
        var document = RegisteredRouteDocument.Splice(
            Empty,
            Suffix,
            [("/acme/orders", "\"get\":{\"operationId\":\"a\"}")]
        );

        Assert.Equal(
            "{\"openapi\":\"3.1.0\",\"paths\":{\"/acme/orders\":{\"get\":{\"operationId\":\"a\"}}},\"components\":{}}",
            document
        );
    }

    /// <remarks>
    /// The prefix ends on the close of the last declared path item, so the first registered one
    /// needs a separator in front of it and the first one into an empty <c>paths</c> does not.
    /// </remarks>
    [Fact]
    public void APathIsSeparatedFromTheDeclaredOnes()
    {
        var document = RegisteredRouteDocument.Splice(
            Populated,
            Suffix,
            [("/acme/orders", "\"get\":{}")]
        );

        Assert.Contains("\"/declared\":{\"get\":{}},\"/acme/orders\":{\"get\":{}}", document);
    }

    /// <remarks>
    /// Two verbs at one path are one path item with two operations, which is what a document keys
    /// on and a router does not.
    /// </remarks>
    [Fact]
    public void TwoVerbsAtOnePathAreOnePathItem()
    {
        var document = RegisteredRouteDocument.Splice(
            Empty,
            Suffix,
            [("/acme/orders", "\"get\":{}"), ("/acme/orders", "\"delete\":{}")]
        );

        Assert.Contains("\"/acme/orders\":{\"get\":{},\"delete\":{}}", document);
    }

    /// <remarks>
    /// Ordered, so the document a process serves does not depend on the order a registration loop
    /// happened to run in.
    /// </remarks>
    [Fact]
    public void PathsAreOrdered()
    {
        var document = RegisteredRouteDocument.Splice(
            Empty,
            Suffix,
            [("/b", "\"get\":{}"), ("/a", "\"get\":{}")]
        );

        Assert.Contains("\"/a\":{\"get\":{}},\"/b\":{\"get\":{}}", document);
    }

    [Fact]
    public void AnApplicationServingNoDocumentSplicesNothing()
    {
        Assert.Null(RegisteredRouteDocument.Splice("", "", [("/acme", "\"get\":{}")]));
    }

    [Fact]
    public void ARouteThatPublishesNothingIsLeftOut()
    {
        Assert.Null(RegisteredRouteDocument.Splice(Empty, Suffix, [("/acme", "")]));
    }

    [Fact]
    public void NoRegistrationSplicesNothing()
    {
        Assert.Null(RegisteredRouteDocument.Splice(Empty, Suffix, []));
    }

    /// <remarks>
    /// A registered path is a value the application computed, and a document that is not JSON is
    /// worse than a path that reads oddly.
    /// </remarks>
    [Fact]
    public void APathIsEscaped()
    {
        var document = RegisteredRouteDocument.Splice(Empty, Suffix, [("/a\"b", "\"get\":{}")]);

        Assert.Contains("\"/a\\\"b\"", document);
    }

    [Theory]
    [InlineData("/orders/{id:int}", "/orders/{id}")]
    [InlineData("/files/{*path}", "/files/{path}")]
    [InlineData("/orders/{id:int:min(1)}", "/orders/{id}")]
    [InlineData("/orders", "/orders")]
    [InlineData("/", "/")]
    public void APathKeyCarriesNoRoutingSyntax(string template, string expected) =>
        Assert.Equal(expected, RouteTemplateParser.NamesOnly(template));
}
