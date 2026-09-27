using System.Collections.Generic;
using System.Linq;
using Hardened.Generation.Models;
using Hardened.Smithy.BuildTask.Parsing;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// What an error declares beyond its body: headers bound to its members, and the service it is
/// bound through.
/// </summary>
/// <remarks>
/// The 0.41 trial's C-02 and C-15. A <c>Retry-After</c> member with <c>@httpHeader</c> went out in
/// the JSON body and never as the header, and an error bound on the service reached no operation.
/// Both were silent.
/// </remarks>
public class SmithyErrorBindingTests
{
    private static string Model(string protocolTrait = "", string serviceErrors = "") =>
        $$"""
            { "smithy": "2.0", "shapes": {
                "com.example#Svc": {
                  "type": "service", "version": "1",
                  "operations": [ { "target": "com.example#GetVan" }, { "target": "com.example#ListVans" } ],
                  "traits": { {{protocolTrait}} }
                  {{serviceErrors}} },
                "com.example#GetVan": {
                  "type": "operation",
                  "traits": { "smithy.api#http": { "method": "GET", "uri": "/vans/{vin}", "code": 200 } },
                  "input": { "target": "com.example#GetVanInput" },
                  "errors": [ { "target": "com.example#Throttled" } ] },
                "com.example#GetVanInput": {
                  "type": "structure",
                  "members": {
                    "vin": { "target": "smithy.api#String",
                             "traits": { "smithy.api#httpLabel": {}, "smithy.api#required": {} } } } },
                "com.example#ListVans": {
                  "type": "operation",
                  "traits": { "smithy.api#http": { "method": "GET", "uri": "/vans", "code": 200 } } },
                "com.example#Throttled": {
                  "type": "structure",
                  "traits": { "smithy.api#error": "client", "smithy.api#httpError": 429 },
                  "members": {
                    "message": { "target": "smithy.api#String" },
                    "retryAfter": { "target": "smithy.api#String",
                                    "traits": { "smithy.api#httpHeader": "Retry-After" } } } },
                "com.example#Unavailable": {
                  "type": "structure",
                  "traits": { "smithy.api#error": "server", "smithy.api#httpError": 503 },
                  "members": { "message": { "target": "smithy.api#String" } } } } }
            """;

    private static ServiceSpecModel Parse(string model)
    {
        var diagnostics = new List<string>();
        var parsed = SmithySpecParser.Parse(model, "fleet", diagnostics);

        Assert.NotNull(parsed);

        return parsed!;
    }

    private static OperationModel Operation(ServiceSpecModel model, string operationId) =>
        model
            .Services.SelectMany(service => service.Operations)
            .Single(operation => operation.OperationId == operationId);

    /// <summary>
    /// An error member with <c>@httpHeader</c> is the error response's header, carried by the
    /// payload record.
    /// </summary>
    [Fact]
    public void AnErrorsHeaderMemberIsAResponseHeaderOnThePayload()
    {
        var model = Parse(Model());
        var throttled = Assert.Single(Operation(model, "GetVan").ErrorResponses);

        var header = Assert.Single(throttled.Headers);

        Assert.Equal("Retry-After", header.Name);
        Assert.True(throttled.HeadersOnPayload);

        var schema = model.Schemas.Single(candidate => candidate.Name == "Throttled");

        Assert.Equal(
            "Retry-After",
            schema.Properties.Single(property => property.Name == "retryAfter").HeaderName
        );
        Assert.False(
            schema.Properties.Single(property => property.Name == "message").IsHeaderBound
        );
    }

    /// <summary>A dispatch protocol ignores HTTP binding traits, on an error as on an output.</summary>
    [Fact]
    public void ADispatchProtocolBindsNoErrorHeader()
    {
        var model = Parse(Model(protocolTrait: "\"aws.protocols#awsJson1_0\": {}"));
        var throttled = Assert.Single(Operation(model, "GetVan").ErrorResponses);

        Assert.Empty(throttled.Headers);
        Assert.False(throttled.HeadersOnPayload);
    }

    /// <summary>A service's errors are bound to every operation in it.</summary>
    [Fact]
    public void AServicesErrorsReachEveryOperation()
    {
        var model = Parse(
            Model(serviceErrors: ", \"errors\": [ { \"target\": \"com.example#Unavailable\" } ]")
        );

        Assert.Equal(
            new[] { 429, 503 },
            Operation(model, "GetVan").ErrorResponses.Select(error => error.StatusCode)
        );
        Assert.Equal(
            new[] { 503 },
            Operation(model, "ListVans").ErrorResponses.Select(error => error.StatusCode)
        );
    }

    /// <summary>An error bound on both the operation and the service is declared once.</summary>
    [Fact]
    public void AnErrorBoundTwiceIsDeclaredOnce()
    {
        var model = Parse(
            Model(serviceErrors: ", \"errors\": [ { \"target\": \"com.example#Throttled\" } ]")
        );

        Assert.Single(Operation(model, "GetVan").ErrorResponses);
        Assert.Single(Operation(model, "ListVans").ErrorResponses);
    }
}
