using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hardened.Generation.Models;
using Hardened.Smithy.BuildTask.Parsing;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// <c>@httpHeader</c> on an output member, which used to become a body property instead.
/// </summary>
/// <remarks>
/// <para>
/// <c>ParseOutput</c> read <c>@httpPayload</c> and nothing else, so a header-bound member stayed in
/// the output structure and the structure became the response schema entire. A model declaring
/// <c>@httpHeader("ETag")</c> sent <c>{"etag": "..."}</c> in the body and no header at all.
/// </para>
/// <para>
/// It was silent for a specific reason worth keeping in a test name: <c>smithy.api#httpHeader</c>
/// is in <see cref="SmithyTraits"/>'s <c>Mapped</c> set, which suppresses the unhandled-trait
/// report. The trait was classified as handled and then not handled - the one outcome that list
/// exists to prevent.
/// </para>
/// </remarks>
public class SmithyResponseHeaderTests {

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "response-headers.json"));

    private static OperationModel Operation(string operationId) {
        var model = SmithySpecParser.Parse(Fixture(), "response-headers", new List<string>());

        Assert.NotNull(model);

        return model!.Services
            .SelectMany(service => service.Operations)
            .Single(operation => operation.OperationId == operationId);
    }

    private static SuccessResponseModel Success(string operationId) =>
        Assert.Single(Operation(operationId).SuccessResponses);

    [Fact]
    public void AHeaderBoundMemberBecomesADeclaredResponseHeader() {
        var headers = Success("CreateJob").Headers;

        Assert.Equal(2, headers.Count);

        Assert.Equal("Location", headers[0].Name);
        Assert.Equal("Location", headers[0].ParameterName);
        Assert.Equal("Where the created job can be read.", headers[0].Description);

        Assert.Equal("ETag", headers[1].Name);
    }

    /// <summary>
    /// The trait's value is the name on the wire, and the member name is not.
    /// </summary>
    /// <remarks>
    /// <c>etag</c> is what the member is called and <c>ETag</c> is what the header is called. Taking
    /// the member name would send a header no client is looking for, which is the same failure as
    /// sending none, only harder to see.
    /// </remarks>
    [Fact]
    public void TheHeaderTakesItsNameFromTheTraitRatherThanTheMember() {
        var etag = Success("CreateJob").Headers.Single(header => header.ParameterName == "ETag");

        Assert.Equal("ETag", etag.Name);
    }

    /// <summary>
    /// The member stays on the output structure and stops being serialized.
    /// </summary>
    /// <remarks>
    /// The structure is not split. It already holds the body members and the header members both,
    /// so the type the handler returns is the type that carries the header - marking the member is
    /// all that is needed, and no second type has to be invented to hold what is left.
    /// </remarks>
    [Fact]
    public void AHeaderBoundMemberStopsBeingSerialized() {
        var model = SmithySpecParser.Parse(Fixture(), "response-headers", new List<string>());

        var success = Success("CreateJob");

        Assert.Equal("#/components/schemas/CreateJobOutput", success.Ref);

        var output = model!.Schemas.Single(schema => schema.Name == "CreateJobOutput");

        // Every member is still there.
        Assert.Equal(
            new[] { "id", "title", "location", "etag" },
            output.Properties.Select(property => property.Name).ToArray());

        // Two of them leave as headers instead of in the body.
        Assert.Equal("Location", output.Properties.Single(p => p.Name == "location").HeaderName);
        Assert.Equal("ETag", output.Properties.Single(p => p.Name == "etag").HeaderName);

        Assert.False(output.Properties.Single(p => p.Name == "id").IsHeaderBound);
        Assert.False(output.Properties.Single(p => p.Name == "title").IsHeaderBound);
    }

    /// <summary>
    /// The output structure is still emitted whole where a model uses it whole.
    /// </summary>
    /// <remarks>
    /// The derived schema is keyed on a synthetic id so that deriving one cannot consume the entry
    /// the real shape would have taken. <c>Job</c> binds nothing and must be untouched.
    /// </remarks>
    [Fact]
    public void AnOutputBindingNothingKeepsTheSchemaItAlreadyHad() {
        var model = SmithySpecParser.Parse(Fixture(), "response-headers", new List<string>());

        var success = Success("GetJob");

        Assert.Empty(success.Headers);
        Assert.Equal("#/components/schemas/Job", success.Ref);

        Assert.DoesNotContain(model!.Schemas, schema => schema.Name == "JobBody");
    }

    /// <summary>
    /// An output whose every member is a header still names a type, because the handler has to
    /// return one to supply the value.
    /// </summary>
    /// <remarks>
    /// <b>Known gap, narrower than what it replaces.</b> Every member of <c>TouchJobOutput</c> is
    /// header-bound, so the serialized body is <c>{}</c> on a 204. That is wrong - a 204 carries no
    /// body - but it is not this change's to fix: nothing in the pipeline suppresses a body by
    /// status, and dropping the type here would leave the handler no way to set the header at all.
    /// Before the trait was read the same response sent <c>{"etag": "..."}</c> on its 204, so this
    /// is strictly less wrong and the remaining half is the bodyless-response question.
    /// </remarks>
    [Fact]
    public void AnOutputThatIsAllHeadersStillNamesItsType() {
        var success = Success("TouchJob");

        Assert.Equal(204, success.StatusCode);
        Assert.Equal("ETag", Assert.Single(success.Headers).Name);
        Assert.Equal("#/components/schemas/TouchJobOutput", success.Ref);
    }

    /// <summary>
    /// A header binding does not change the shape of the handler's signature.
    /// </summary>
    /// <remarks>
    /// The payload carries the header itself, so there is nothing to wrap and no response set to
    /// force. This is the difference between the two front ends: OpenAPI declares headers beside
    /// the body schema, where no type holds both and a case type has to be generated; Smithy binds
    /// them to members of the output, where one already does.
    /// </remarks>
    [Fact]
    public void AHeaderBindingDoesNotForceAResponseSet() {
        var operation = Operation("CreateJob");

        Assert.True(ResponseSetPlan.PrimarySuccessCarriesHeaders(operation));
        Assert.True(ResponseSetPlan.PrimarySuccessIsBarePayload(operation));

        Assert.False(ResponseSetPlan.DeclaresResponseHeaders(operation));
        Assert.False(ResponseSetPlan.RequiresResponseSet(operation, SpecResponseModel.Standard));
    }
}
