using Hardened.Generation.Models;
using Hardened.Smithy.BuildTask.Parsing;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// What <c>@httpPayload</c> binds, across the shapes it broke on.
/// </summary>
/// <remarks>
/// The trait is how a model says "the body is this, not a wrapper around it", which makes it the
/// path every bare-resource, bare-scalar and bare-list body takes - and it failed differently for
/// each: a prelude scalar became a reference to a type nothing declares, a named list became an
/// empty record with no diagnostic, and a payload beside an <c>@httpHeader</c> marked its headers
/// on a payload type that cannot carry them. All three were found by building the same
/// specification three ways and comparing what shipped.
/// </remarks>
public class SmithyPayloadTests {

    private static string Model(string outputShapes, string extraShapes = "") =>
        $$"""
          { "smithy": "2.0", "shapes": {
              "com.example#Svc": {
                "type": "service", "version": "1",
                "operations": [ { "target": "com.example#Op" } ] },
              "com.example#Op": {
                "type": "operation",
                "traits": { "smithy.api#http": { "method": "GET", "uri": "/x", "code": 200 } },
                "output": { "target": "com.example#Out" } },
              "com.example#Out": {
                "type": "structure",
                "members": { {{outputShapes}} } }
              {{extraShapes}} } }
          """;

    private static OperationModel Parse(string outputMembers, string extraShapes = "") {
        var diagnostics = new List<string>();
        var model = SmithySpecParser.Parse(Model(outputMembers, extraShapes), "payload", diagnostics);

        Assert.NotNull(model);

        return Assert.Single(Assert.Single(model!.Services).Operations);
    }

    /// <summary>
    /// The prelude scalar becomes the declared response type. It became
    /// <c>Models.String</c>, a reference to a type nothing declares - CS0234 in generated code.
    /// </summary>
    [Fact]
    public void APreludeScalarPayloadIsTheResponseType() {
        var operation = Parse(
            """
            "label": { "target": "smithy.api#String",
                       "traits": { "smithy.api#httpPayload": {} } }
            """);

        Assert.Null(operation.ResponseRef);
        Assert.Equal("string", operation.ResponseType);
    }

    /// <summary>
    /// A named list payload is an array response. It generated an empty record, the build stayed
    /// clean, and the endpoint answered <c>{}</c>.
    /// </summary>
    [Fact]
    public void ANamedListPayloadIsAnArrayResponse() {
        var operation = Parse(
            """
            "names": { "target": "com.example#Names",
                       "traits": { "smithy.api#httpPayload": {} } }
            """,
            """
            , "com.example#Names": {
                "type": "list", "member": { "target": "smithy.api#String" } }
            """);

        Assert.True(operation.ResponseIsArray);
        Assert.Equal("string", operation.ResponseArrayItemsType);
    }

    /// <summary>
    /// <c>@mediaType</c> on the payload target is the response's content type. It was accepted by
    /// the trait table and never read, so a Smithy service could answer nothing but JSON.
    /// </summary>
    [Fact]
    public void AMediaTypeOnThePayloadTargetIsTheContentType() {
        var operation = Parse(
            """
            "label": { "target": "com.example#LabelText",
                       "traits": { "smithy.api#httpPayload": {} } }
            """,
            """
            , "com.example#LabelText": {
                "type": "string",
                "traits": { "smithy.api#mediaType": "text/plain" } }
            """);

        Assert.Equal("text/plain", operation.ResponseContentType);
        Assert.Equal("string", operation.ResponseType);
    }

    /// <summary>
    /// A payload beside a bound header leaves the headers off the payload: they become case-type
    /// parameters, because the handler returns the payload target and the wrapper that carried
    /// the header member is discarded. Marking them on-payload emitted an <c>ApplyHeaders</c>
    /// call against a type that never had one.
    /// </summary>
    [Fact]
    public void APayloadBesideAHeaderKeepsTheHeadersOffThePayload() {
        var operation = Parse(
            """
            "body": { "target": "com.example#Pet",
                      "traits": { "smithy.api#httpPayload": {} } },
            "location": { "target": "smithy.api#String",
                          "traits": { "smithy.api#httpHeader": "Location" } }
            """,
            """
            , "com.example#Pet": {
                "type": "structure",
                "members": { "id": { "target": "smithy.api#String" } } }
            """);

        var success = Assert.Single(operation.SuccessResponses);

        Assert.False(success.HeadersOnPayload);
        Assert.Equal("Location", Assert.Single(success.Headers).Name);
    }

    /// <summary>
    /// The whole-output form keeps its headers on the payload - the returned structure is the
    /// type that carries them - which is the arrangement the fix above must not disturb.
    /// </summary>
    [Fact]
    public void AWholeOutputWithAHeaderKeepsItOnThePayload() {
        var operation = Parse(
            """
            "id": { "target": "smithy.api#String" },
            "etag": { "target": "smithy.api#String",
                      "traits": { "smithy.api#httpHeader": "ETag" } }
            """);

        var success = Assert.Single(operation.SuccessResponses);

        Assert.True(success.HeadersOnPayload);
        Assert.Equal("ETag", Assert.Single(success.Headers).Name);
    }

    /// <summary>
    /// <c>@default</c>'s value reaches the parameter, so the binder answers an absent value with
    /// it and the document declares it. It decided nullability and was otherwise dropped.
    /// </summary>
    [Fact]
    public void ADefaultTraitValueReachesTheParameter() {
        var diagnostics = new List<string>();
        var model = SmithySpecParser.Parse(
            """
            { "smithy": "2.0", "shapes": {
                "com.example#Svc": {
                  "type": "service", "version": "1",
                  "operations": [ { "target": "com.example#Op" } ] },
                "com.example#Op": {
                  "type": "operation",
                  "traits": { "smithy.api#http": { "method": "GET", "uri": "/x", "code": 200 } },
                  "input": { "target": "com.example#In" } },
                "com.example#In": {
                  "type": "structure",
                  "members": {
                    "limit": { "target": "smithy.api#Integer",
                               "traits": { "smithy.api#httpQuery": "limit",
                                           "smithy.api#default": 20 } } } } } }
            """,
            "defaults", diagnostics);

        Assert.NotNull(model);

        var operation = Assert.Single(Assert.Single(model!.Services).Operations);
        var limit = Assert.Single(operation.Parameters);

        Assert.Equal("20", limit.Default);
    }
}
