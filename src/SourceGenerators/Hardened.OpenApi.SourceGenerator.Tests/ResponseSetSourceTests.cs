using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// The response-set helpers, through <c>Hardened.Idl.SourceGenerator</c>'s copy of them.
/// </summary>
/// <remarks>
/// <para>
/// <c>Hardened.Idl.SourceGenerator</c> compiles <c>../Hardened.SourceGenerator/Requests/**</c> and
/// <c>OpenApiDocument/**</c> in as source rather than referencing them, so every one of these types
/// exists twice - once in each generator assembly - and a test binds to whichever copy its project
/// references. The equivalents in <c>Hardened.SourceGenerator.Tests</c> exercise the other one.
/// </para>
/// <para>
/// That is not a coverage technicality. Each assembly is compiled separately, under its own
/// <c>LangVersion</c> and its own set of linked files, so "it works over there" is a claim about a
/// different compilation. This project is where the specification-first generator is tested, and
/// this is the copy it ships.
/// </para>
/// </remarks>
public class ResponseSetSourceTests {

    #region the encoded case list

    [Fact]
    public void RoundTripKeepsEveryFieldOfEveryCase() {
        var cases = new[] {
            new UnionCaseModel("global::App.Todo", 200, appliesHeaders: false, hasBody: true),
            new UnionCaseModel("global::App.RateLimited", 429, appliesHeaders: true, hasBody: true),
            new UnionCaseModel("global::App.NoContent", 204, appliesHeaders: false, hasBody: false)
        };

        var decoded = UnionResponseSelector.Decode(UnionResponseSelector.Encode(cases));

        Assert.Equal(cases.Length, decoded.Count);

        for (var i = 0; i < cases.Length; i++) {
            Assert.Equal(cases[i].TypeName, decoded[i].TypeName);
            Assert.Equal(cases[i].Status, decoded[i].Status);
            Assert.Equal(cases[i].AppliesHeaders, decoded[i].AppliesHeaders);
            Assert.Equal(cases[i].HasBody, decoded[i].HasBody);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NothingDecodesToNoCases(string? encoded) {
        Assert.Empty(UnionResponseSelector.Decode(encoded));
    }

    /// <summary>
    /// A malformed entry is skipped rather than throwing. This crosses an incremental cache as a
    /// string, and a generator that threw while decoding one would fail the build with a message
    /// about nothing the author wrote.
    /// </summary>
    [Theory]
    [InlineData("global::App.Todo")]
    [InlineData("global::App.Todo|notanumber|01")]
    [InlineData("global::App.Todo|200|1")]
    public void AMalformedEntryIsSkipped(string encoded) {
        Assert.Empty(UnionResponseSelector.Decode(encoded));
    }

    [Fact]
    public void AMalformedEntryDoesNotDiscardTheGoodOnesBesideIt() {
        var decoded = UnionResponseSelector.Decode(
            "global::App.Todo|200|01;broken;global::App.Gone|410|01");

        Assert.Equal(2, decoded.Count);
        Assert.Equal("global::App.Todo", decoded[0].TypeName);
        Assert.Equal("global::App.Gone", decoded[1].TypeName);
    }

    #endregion

    #region descriptions

    [Theory]
    [InlineData(200, "OK")]
    [InlineData(201, "Created")]
    [InlineData(204, "No Content")]
    [InlineData(404, "Not Found")]
    [InlineData(409, "Conflict")]
    [InlineData(503, "Service Unavailable")]
    public void AStatusIsDescribedByItsRegisteredName(int status, string expected) {
        Assert.Equal(expected, HttpResponseDescription.For(status));
    }

    [Fact]
    public void AnUnlistedStatusNamesItself() {
        Assert.Contains("418", HttpResponseDescription.For(418), StringComparison.Ordinal);
    }

    #endregion

    #region response equality

    [Fact]
    public void IdenticallyBuiltResponsesAreEqual() {
        var schema = new HandlerSchema("{}", new[] { new SchemaComponent("Todo", "{}") });

        Assert.Equal(
            new ResponseSchemaModel(404, "Not Found", schema),
            new ResponseSchemaModel(404, "Not Found", schema));
    }

    [Fact]
    public void ResponsesDifferingInTheirStatusAreNotEqual() {
        Assert.NotEqual(
            new ResponseSchemaModel(404, "Not Found", null),
            new ResponseSchemaModel(410, "Not Found", null));
    }

    #endregion

    #region the mode selector

    [Fact]
    public void AnEntryPointWithNoAttributeIsStandard() {
        Assert.Equal(
            ResponseModelValue.Standard,
            ResponseModelSelector.Read(new EntryPointSelector.Model {
                EntryPointType = CSharpAuthor.TypeDefinition.Get("MyApp", "Application"),
                AttributeModels = System.Array.Empty<AttributeModel>()
            }));
    }

    [Theory]
    [InlineData("Hardened.Requests.Abstract.Responses.ResponseModel.Union", ResponseModelValue.Union)]
    [InlineData("ResponseModel.Response", ResponseModelValue.Response)]
    [InlineData("Union", ResponseModelValue.Union)]
    [InlineData("ResponseModel.Standard", ResponseModelValue.Standard)]
    [InlineData("ResponseModel.Whatever", ResponseModelValue.Standard)]
    public void TheDeclaredModeIsRead(string arguments, ResponseModelValue expected) {
        var model = new EntryPointSelector.Model {
            EntryPointType = CSharpAuthor.TypeDefinition.Get("MyApp", "Application"),
            AttributeModels = new[] {
                new AttributeModel(
                    CSharpAuthor.TypeDefinition.Get("MyApp", "ResponseModelAttribute"), arguments, "")
            }
        };

        Assert.Equal(expected, ResponseModelSelector.Read(model));
    }

    #endregion
}
