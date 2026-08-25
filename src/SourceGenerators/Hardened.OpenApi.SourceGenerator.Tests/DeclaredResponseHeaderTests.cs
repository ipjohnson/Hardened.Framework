using System.Linq;
using System.Threading;
using Hardened.Generation;
using Hardened.Generation.Models;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// A header the description declares, from the document through to the emitted case type.
/// </summary>
/// <remarks>
/// <para>
/// There was no test here, and that is the whole reason this shipped. The parser never read
/// <c>responses.*.headers</c>, the model had nowhere to put it, and
/// <c>RequestModelBuilder.BuildUnionCases</c> answered <c>appliesHeaders</c> with the literal
/// <c>false</c> at all three of its call sites - three independent breaks, none of which any test
/// disagreed with. An unread property raises no diagnostic and takes no branch, so it was invisible
/// to coverage as well.
/// </para>
/// <para>
/// The assertions run the length of the pipe on purpose: parse, round-trip, plan, emit. A header
/// that survives the parser and dies in the serializer is the same silence from a consumer's side.
/// </para>
/// </remarks>
public class DeclaredResponseHeaderTests {

    private static ServiceSpecModel Parse() =>
        OpenApiSpecParser.Parse(Specs.DeclaredResponseHeaders, "test", CancellationToken.None)!;

    private static OperationModel CreatePet(ServiceSpecModel model) =>
        model.Services.SelectMany(service => service.Operations)
            .Single(operation => operation.OperationId == "createPet");

    [Fact]
    public void TheParserReadsAHeaderDeclaredOnASuccess() {
        var operation = CreatePet(Parse());

        var created = operation.SuccessResponses.Single(response => response.StatusCode == 201);

        var header = Assert.Single(created.Headers);

        Assert.Equal("Location", header.Name);
        Assert.Equal("Location", header.ParameterName);
        Assert.Equal("Where the new pet can be read.", header.Description);
    }

    [Fact]
    public void TheParserReadsAHeaderDeclaredOnAnError() {
        var operation = CreatePet(Parse());

        var throttled = operation.ErrorResponses.Single(response => response.StatusCode == 429);

        var header = Assert.Single(throttled.Headers);

        // The wire name keeps its hyphen and the parameter takes the only spelling C# allows.
        Assert.Equal("Retry-After", header.Name);
        Assert.Equal("RetryAfter", header.ParameterName);
    }

    [Fact]
    public void HeadersSurviveTheModelFileTheBuildTaskWritesForTheGenerator() {
        var written = SpecModelSerializer.Write(Parse());
        var read = SpecModelSerializer.Read(written);

        var operation = CreatePet(read);

        Assert.Equal("Location", operation.SuccessResponses.Single(r => r.StatusCode == 201).Headers.Single().Name);
        Assert.Equal("ETag", operation.SuccessResponses.Single(r => r.StatusCode == 202).Headers.Single().Name);
        Assert.Equal("Retry-After", operation.ErrorResponses.Single(r => r.StatusCode == 429).Headers.Single().Name);
    }

    [Fact]
    public void AHeaderOnAResponseIsPartOfWhatMakesTwoModelsDiffer() {
        var withHeader = Parse();
        var withoutHeader = Parse();

        CreatePet(withoutHeader).SuccessResponses.Single(r => r.StatusCode == 201).Headers.Clear();

        // Equality drives incremental generation. A model that compares equal to the previous one
        // does not regenerate, so a header the author just added would not arrive until something
        // else changed.
        Assert.NotEqual(
            CreatePet(withHeader).SuccessResponses.Single(r => r.StatusCode == 201),
            CreatePet(withoutHeader).SuccessResponses.Single(r => r.StatusCode == 201));
    }

    [Fact]
    public void APrimarySuccessDeclaringAHeaderIsWrappedRatherThanLeftAsTheBarePayload() {
        var operation = CreatePet(Parse());

        // Pet is the type a 200 with no Location answers with too, so the header cannot live on it.
        Assert.False(ResponseSetPlan.PrimarySuccessIsBarePayload(operation));

        Assert.True(ResponseSetPlan.NeedsSuccessCaseType(
            operation, operation.SuccessResponses.Single(r => r.StatusCode == 201)));
    }

    [Fact]
    public void APrimarySuccessDeclaringNoHeaderKeepsTheSignatureItAlreadyHad() {
        var model = Parse();
        var operation = CreatePet(model);

        operation.SuccessResponses.Single(r => r.StatusCode == 201).Headers.Clear();

        Assert.True(ResponseSetPlan.PrimarySuccessIsBarePayload(operation));

        Assert.False(ResponseSetPlan.NeedsSuccessCaseType(
            operation, operation.SuccessResponses.Single(r => r.StatusCode == 201)));
    }

    [Fact]
    public void TheEmittedCaseTypeTakesTheHeaderAndWritesIt() {
        var generated = OpenApiGenerator.Run(Specs.DeclaredResponseHeaders).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        // The wrapper the header forced into existence, carrying the payload and the header value.
        Assert.Contains("CreatePetCreated(global::TestNamespace.Models.Pet Body, string Location)", generated);

        Assert.Contains(
            "global::Hardened.Requests.Abstract.Responses.IProvidesResponseHeaders", generated);

        Assert.Contains("headers[\"Location\"] = Location", generated);
        Assert.Contains("headers[\"ETag\"] = ETag", generated);

        // The hyphen survives to the wire and never reaches the signature.
        Assert.Contains("headers[\"Retry-After\"] = RetryAfter", generated);
        Assert.Contains("string RetryAfter", generated);
    }

    [Fact]
    public void TheEmittedCaseTypesArePartialSoAnApplicationCanExtendThem() {
        var generated = OpenApiGenerator.Run(Specs.DeclaredResponseHeaders).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        // Sealed keeps the union's match order unambiguous; partial is a different question, and
        // refusing extension was never what sealing was for.
        Assert.Contains("public sealed partial record CreatePetCreated", generated);
    }

    [Fact]
    public void ADeclaredHeaderTurnsOnTheDispatchThatAppliesIt() {
        // Across every generated file, because the two halves live in different ones: the build
        // task writes the case type into the models file and the Idl generator writes the switch
        // that calls it into the controller.
        var all = string.Join(
            "\n",
            OpenApiGenerator.Run(Specs.DeclaredResponseHeaders).AssertNoErrors()
                .GeneratedSources.Values);

        // The switch arm binds the case rather than discarding it, and calls through the interface.
        // This is the assertion that would have failed against the hard-coded `appliesHeaders:
        // false`, while every other assertion in this file still passed.
        Assert.Contains("ApplyHeaders(", all);
    }
}
