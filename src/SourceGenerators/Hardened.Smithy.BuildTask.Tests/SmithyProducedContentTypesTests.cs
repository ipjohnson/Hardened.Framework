using Hardened.Generation.Models;
using Hardened.Smithy.BuildTask.Parsing;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// The set a REST operation's responses are negotiated against, mirroring what the OpenAPI parser
/// collects from a content map.
/// </summary>
/// <remarks>
/// This front end used to declare no set at all, which left every response negotiated against
/// every registered serializer - so an <c>@httpPayload</c> string under
/// <c>@mediaType("text/plain")</c> went out as a quoted JSON string for a client that stated no
/// preference. The error entry is always <c>application/json</c>, after the success entry because
/// the set is negotiated first-match; it is what lets a declared error answer on an operation
/// whose success is not JSON.
/// </remarks>
public class SmithyProducedContentTypesTests {

    private const string Model =
        """
        { "smithy": "2.0", "shapes": {
            "com.example#Svc": {
              "type": "service", "version": "1",
              "operations": [
                { "target": "com.example#GetLabel" },
                { "target": "com.example#GetStatus" } ] },
            "com.example#GetLabel": {
              "type": "operation",
              "traits": { "smithy.api#http": { "method": "GET", "uri": "/labels/{id}", "code": 200 } },
              "input": { "target": "com.example#GetLabelInput" },
              "output": { "target": "com.example#GetLabelOutput" },
              "errors": [ { "target": "com.example#NotFoundError" } ] },
            "com.example#GetLabelInput": {
              "type": "structure",
              "members": {
                "id": { "target": "smithy.api#String",
                        "traits": { "smithy.api#httpLabel": {}, "smithy.api#required": {} } } } },
            "com.example#GetLabelOutput": {
              "type": "structure",
              "members": {
                "label": { "target": "com.example#LabelText",
                           "traits": { "smithy.api#httpPayload": {} } } } },
            "com.example#LabelText": {
              "type": "string",
              "traits": { "smithy.api#mediaType": "text/plain" } },
            "com.example#GetStatus": {
              "type": "operation",
              "traits": { "smithy.api#http": { "method": "GET", "uri": "/status", "code": 200 } },
              "output": { "target": "com.example#GetStatusOutput" },
              "errors": [ { "target": "com.example#NotFoundError" } ] },
            "com.example#GetStatusOutput": {
              "type": "structure",
              "members": {
                "state": { "target": "smithy.api#String" } } },
            "com.example#NotFoundError": {
              "type": "structure",
              "traits": { "smithy.api#error": "client", "smithy.api#httpError": 404 },
              "members": {
                "message": { "target": "smithy.api#String" } } } } }
        """;

    private static OperationModel Operation(string operationId) {
        var diagnostics = new List<string>();
        var model = SmithySpecParser.Parse(Model, "labels", diagnostics);

        Assert.NotNull(model);

        return Assert.Single(
            Assert.Single(model!.Services).Operations, o => o.OperationId == operationId);
    }

    [Fact]
    public void ThePayloadMediaTypeLeadsAndTheErrorRepresentationFollows() {
        Assert.Equal(
            new[] { "text/plain", "application/json" },
            Operation("GetLabel").ProducedContentTypes);
    }

    [Fact]
    public void AJsonOperationWithJsonErrorsDeclaresItOnce() {
        Assert.Equal(
            new[] { "application/json" },
            Operation("GetStatus").ProducedContentTypes);
    }

    /// <summary>
    /// A dispatch protocol names one content type for everything and negotiation has no say, so
    /// its operations declare no set.
    /// </summary>
    [Fact]
    public void ADispatchOperationDeclaresNoSet() {
        var diagnostics = new List<string>();
        var model = SmithySpecParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bank-awsjson.json")),
            "bank",
            diagnostics);

        Assert.NotNull(model);

        Assert.All(
            model!.Services[0].Operations,
            operation => Assert.Empty(operation.ProducedContentTypes));
    }
}
