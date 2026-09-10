using Hardened.Generation.Models;
using Hardened.Smithy.BuildTask.Parsing;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// A <c>blob</c> bound as <c>@httpPayload</c>, in both directions.
/// </summary>
/// <remarks>
/// The request side dropped the format and always said <c>application/json</c>, so an operation
/// whose whole point is a binary body took a <c>string</c> parameter, published no
/// <c>requestBody</c> at all, and refused a real body when one was sent. The response side kept the
/// format and so answered <c>byte[]</c> correctly, but read the content type from
/// <c>@mediaType</c> alone - so a blob that named none went out quoted as JSON.
///
/// The framework's own Smithy subject binds <c>@httpPayload</c> on outputs only, which is how a
/// whole direction of this stayed unexercised.
/// </remarks>
public class SmithyBlobPayloadTests {

    private static string Model(string payloadTarget, string extraShapes = "") =>
        $$"""
          { "smithy": "2.0", "shapes": {
              "com.example#Svc": {
                "type": "service", "version": "1",
                "operations": [
                  { "target": "com.example#Put" },
                  { "target": "com.example#Get" } ] },
              "com.example#Put": {
                "type": "operation",
                "traits": { "smithy.api#http": { "method": "PUT", "uri": "/blobs/{id}", "code": 200 } },
                "input": { "target": "com.example#PutInput" } },
              "com.example#PutInput": {
                "type": "structure",
                "members": {
                  "id": { "target": "smithy.api#String",
                          "traits": { "smithy.api#httpLabel": {}, "smithy.api#required": {} } },
                  "content": { "target": "{{payloadTarget}}",
                               "traits": { "smithy.api#httpPayload": {} } } } },
              "com.example#Get": {
                "type": "operation",
                "traits": { "smithy.api#http": { "method": "GET", "uri": "/blobs/{id}", "code": 200 } },
                "input": { "target": "com.example#GetInput" },
                "output": { "target": "com.example#GetOutput" } },
              "com.example#GetInput": {
                "type": "structure",
                "members": {
                  "id": { "target": "smithy.api#String",
                          "traits": { "smithy.api#httpLabel": {}, "smithy.api#required": {} } } } },
              "com.example#GetOutput": {
                "type": "structure",
                "members": {
                  "content": { "target": "{{payloadTarget}}",
                               "traits": { "smithy.api#httpPayload": {} } } } }
              {{extraShapes}} } }
          """;

    private static (OperationModel Put, OperationModel Get) Parse(
        string payloadTarget = "smithy.api#Blob", string extraShapes = "") {
        var diagnostics = new List<string>();
        var model = SmithySpecParser.Parse(Model(payloadTarget, extraShapes), "blob", diagnostics);

        Assert.NotNull(model);
        Assert.Empty(diagnostics);

        var operations = Assert.Single(model!.Services).Operations;

        return (
            Assert.Single(operations, operation => operation.OperationId == "Put"),
            Assert.Single(operations, operation => operation.OperationId == "Get"));
    }

    /// <summary>
    /// The pair that has to agree, and did not.
    /// </summary>
    /// <remarks>
    /// <c>OperationModel</c> had a <c>ResponseFormat</c> and no request equivalent, so the same
    /// shape came out ("string", "byte") on the way back and ("string", null) on the way in -
    /// <c>byte[]</c> and <c>string</c> once mapped.
    /// </remarks>
    [Fact]
    public void ABlobPayloadCarriesItsFormatInBothDirections() {
        var (put, get) = Parse();

        Assert.Equal("string", put.RequestBodyType);
        Assert.Equal("byte", put.RequestBodyFormat);

        Assert.Equal("string", get.ResponseType);
        Assert.Equal("byte", get.ResponseFormat);
    }

    /// <summary>
    /// Bytes are not JSON, in either direction.
    /// </summary>
    /// <remarks>
    /// The request side had no rule and always said <c>application/json</c>, so a binary body was
    /// negotiated as JSON and refused. The response side read <c>@mediaType</c> and nothing else.
    /// </remarks>
    [Fact]
    public void ABlobPayloadIsOctetStreamInBothDirections() {
        var (put, get) = Parse();

        Assert.Equal("application/octet-stream", put.RequestBodyContentType);
        Assert.Equal("application/octet-stream", get.ResponseContentType);
    }

    /// <summary>A declared media type wins over the shape's default, as it always did on the way out.</summary>
    [Fact]
    public void ADeclaredMediaTypeWinsOverTheDefault() {
        var (put, get) = Parse(
            "com.example#Pdf",
            """
            , "com.example#Pdf": {
                "type": "blob",
                "traits": { "smithy.api#mediaType": "application/pdf" } }
            """);

        Assert.Equal("application/pdf", put.RequestBodyContentType);
        Assert.Equal("application/pdf", get.ResponseContentType);
        Assert.Equal("byte", put.RequestBodyFormat);
    }

    /// <summary>
    /// A payload that is not a blob keeps the JSON default it always had.
    /// </summary>
    /// <remarks>
    /// The control. Without it the octet-stream arm could pass its own tests by answering
    /// octet-stream for every payload there is.
    /// </remarks>
    [Fact]
    public void AStructurePayloadIsStillJson() {
        var (put, get) = Parse(
            "com.example#Pet",
            """
            , "com.example#Pet": {
                "type": "structure",
                "members": { "id": { "target": "smithy.api#String" } } }
            """);

        Assert.Equal("application/json", put.RequestBodyContentType);
        Assert.Null(put.RequestBodyFormat);
        Assert.NotNull(put.RequestBodyRef);

        Assert.Equal("application/json", get.ResponseContentType);
    }

    /// <summary>A string payload keeps the JSON default too, unless it names a media type.</summary>
    [Fact]
    public void AStringPayloadIsStillJson() {
        var (put, _) = Parse("smithy.api#String");

        Assert.Equal("application/json", put.RequestBodyContentType);
        Assert.Equal("string", put.RequestBodyType);
        Assert.Null(put.RequestBodyFormat);
    }
}
