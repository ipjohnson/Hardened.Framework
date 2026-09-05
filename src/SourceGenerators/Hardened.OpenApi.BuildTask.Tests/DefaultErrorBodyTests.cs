using Hardened.Generation;
using Hardened.Generation.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// What a handler's null return writes for a status the description declared a body for.
/// </summary>
/// <remarks>
/// The rule is that a required member with nothing to fill it from means no instance at all, so
/// the response carries no body rather than one with an invented value in it. The exceptions are
/// the members a specification gives a meaning to: 7807's and Smithy's <c>message</c>.
/// </remarks>
public class DefaultErrorBodyTests {

    private static SchemaModel Schema(
        string name, bool isErrorShape, params PropertyModel[] properties) {
        var schema = new SchemaModel { Name = name, Kind = SchemaKind.Object, IsErrorShape = isErrorShape };

        foreach (var property in properties) {
            schema.Properties.Add(property);

            if (property.IsRequired) {
                schema.Required.Add(property.Name);
            }
        }

        return schema;
    }

    private static PropertyModel String(string name, bool required) =>
        new() { Name = name, Type = "string", IsRequired = required };

    /// <summary>
    /// The defect a generated client made visible. A Smithy @error carries a required message, so
    /// nothing could fill it, so a null return wrote no body at all - while the served document
    /// promised the shape. A Kiota client registered that shape for the status and threw a bare
    /// ApiException saying the error failed to deserialize.
    /// </summary>
    [Fact]
    public void ASmithyErrorShapeFillsItsRequiredMessage() {
        var schemas = new[] { Schema("TodoNotFound", isErrorShape: true, String("message", required: true)) };

        var arguments = DefaultErrorBody.Arguments(schemas, "TodoNotFound", 404);

        Assert.NotNull(arguments);
        Assert.Equal(["\"Not Found\""], arguments);
    }

    /// <summary>
    /// Keyed on the trait rather than the member's name. An ordinary payload that happens to carry
    /// a required message is not an error, and writing a reason phrase into it would be inventing
    /// a domain value - which is the thing this whole rule exists to refuse.
    /// </summary>
    [Fact]
    public void AnOrdinaryPayloadWithARequiredMessageStillGetsNoInstance() {
        var schemas = new[] { Schema("ChatPost", isErrorShape: false, String("message", required: true)) };

        Assert.Null(DefaultErrorBody.Arguments(schemas, "ChatPost", 404));
    }

    /// <summary>
    /// An error shape's other required members are not messages, so the refusal stands for them
    /// and the whole instance is withheld. Filling one member and inventing another would be
    /// worse than writing nothing.
    /// </summary>
    [Fact]
    public void AnErrorShapeWithAnotherRequiredMemberStillGetsNoInstance() {
        var schemas = new[] {
            Schema("Throttled", isErrorShape: true,
                String("message", required: true),
                String("retryToken", required: true))
        };

        Assert.Null(DefaultErrorBody.Arguments(schemas, "Throttled", 404));
    }

    /// <summary>
    /// An optional member is left at its C# default whether the shape is an error or not, so an
    /// error shape whose message is optional is filled rather than withheld.
    /// </summary>
    [Fact]
    public void AnErrorShapeFillsAMessageAndDefaultsTheRest() {
        var schemas = new[] {
            Schema("PetMissing", isErrorShape: true,
                String("message", required: true),
                String("hint", required: false))
        };

        Assert.Equal(["\"Not Found\"", "default"], DefaultErrorBody.Arguments(schemas, "PetMissing", 404));
    }

    /// <summary>
    /// The status decides the phrase, so the same shape bound to a 409 says so.
    /// </summary>
    [Fact]
    public void ThePhraseIsTheStatusItIsFilledFor() {
        var schemas = new[] { Schema("TitleTaken", isErrorShape: true, String("message", required: true)) };

        Assert.Equal(["\"Conflict\""], DefaultErrorBody.Arguments(schemas, "TitleTaken", 409));
    }

    /// <summary>
    /// The document's own default still wins, because it is the one source here that is not an
    /// inference.
    /// </summary>
    [Fact]
    public void ADeclaredDefaultBeatsTheReasonPhrase() {
        var property = String("message", required: true);
        property.Default = "no such todo";

        var schemas = new[] { Schema("TodoNotFound", isErrorShape: true, property) };

        Assert.Equal(["\"no such todo\""], DefaultErrorBody.Arguments(schemas, "TodoNotFound", 404));
    }

    /// <summary>
    /// Nothing changes for the shape OpenAPI produces: 7807's members are filled from their
    /// specified meanings and an optional detail is left empty, which is the milder half of the
    /// same defect and is deliberate. A handler that wants to say why throws instead.
    /// </summary>
    [Fact]
    public void AProblemFillsItsSpecifiedMembersAndLeavesDetailEmpty() {
        var schemas = new[] {
            Schema("Problem", isErrorShape: false,
                String("type", required: false),
                String("title", required: false),
                new PropertyModel { Name = "status", Type = "integer", IsRequired = false },
                String("detail", required: false))
        };

        Assert.Equal(
            ["global::Hardened.Requests.Abstract.Responses.ProblemTypes.NotFound", "\"Not Found\"", "404", "default"],
            DefaultErrorBody.Arguments(schemas, "Problem", 404));
    }

    /// <summary>
    /// The <c>type</c> is the framework's own problem type for the status, which is what a
    /// code-first handler's record sends, so a null return and a returned record answer alike.
    /// Only a status the framework declares no kind for falls back to <c>about:blank</c>.
    /// </summary>
    [Fact]
    public void AStatusWithNoProblemKindWritesAboutBlank() {
        var schemas = new[] {
            Schema("Problem", isErrorShape: false,
                String("type", required: false),
                new PropertyModel { Name = "status", Type = "integer", IsRequired = false },
                String("title", required: false))
        };

        Assert.Equal(
            ["\"about:blank\"", "405", "\"Method Not Allowed\""],
            DefaultErrorBody.Arguments(schemas, "Problem", 405));
    }

    /// <summary>
    /// The same members read off a record instead - the body a returned <c>NotFound</c> converts
    /// into - so the detail is the handler's and the rest is what the record already knows.
    /// </summary>
    [Fact]
    public void AProblemIsFilledFromARecord() {
        var schemas = new[] {
            Schema("Problem", isErrorShape: false,
                String("type", required: false),
                String("title", required: false),
                new PropertyModel { Name = "status", Type = "integer", IsRequired = false },
                String("detail", required: false))
        };

        Assert.Equal(
            ["value.Type", "value.Title", "value.Status", "value.Detail"],
            DefaultErrorBody.ArgumentsFromRecord(schemas, "Problem", 404, "value"));
    }

    /// <summary>A Smithy message is the detail, or the title where the record carries none.</summary>
    [Fact]
    public void AnErrorShapesMessageIsFilledFromTheRecordsDetail() {
        var schemas = new[] { Schema("TodoNotFound", isErrorShape: true, String("message", required: true)) };

        Assert.Equal(
            ["(value.Detail ?? value.Title)"],
            DefaultErrorBody.ArgumentsFromRecord(schemas, "TodoNotFound", 404, "value"));
    }
}
