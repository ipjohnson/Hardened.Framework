using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// <c>application/problem+json</c>: which responses are written as it, and how a failure is
/// negotiated into it.
/// </summary>
/// <remarks>
/// <para>
/// The 0.41 trial's B-01 and B-02. A contract declaring a failure only as
/// <c>application/problem+json</c> answered 500, because nothing could produce it. One declaring it
/// beside an <c>application/json</c> success answered the failure as <c>application/json</c>,
/// because a failure was negotiated within the whole produced set.
/// </para>
/// <para>
/// Driven through the real locator and the real JSON serializer, because the label is decided
/// between the two: the locator commits a media type, and the serializer writes the one it was
/// committed to or the one its body calls for.
/// </para>
/// </remarks>
public class ProblemJsonTests
{
    private sealed record Missing(string? Detail = null) : IProblemDetails
    {
        public string Type => "urn:test:missing";

        public string Title => "Missing";

        public int Status => 404;
    }

    private sealed record ApiError(string Code);

    private sealed record Product(string Sku);

    private static SystemTextJsonResponseSerializer Json() =>
        new(
            Options.Create<IJsonSerializerConfiguration>(
                new JsonSerializerConfiguration
                {
                    SerializeOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web),
                }
            ),
            Array.Empty<IJsonTypeInfoResolver>()
        );

    private static SerializationLocatorService Locator(
        IResponseSerializer serializer,
        ContentNegotiationMode mode = ContentNegotiationMode.Strict
    ) => new(Array.Empty<IRequestDeserializer>(), [serializer], new ContentNegotiationPolicy(mode));

    private static IExecutionContext Context(
        string? accept,
        int status,
        object value,
        string[]? produced = null,
        string[]? errors = null
    )
    {
        var context = Pipeline.Context(accept: accept);

        context.Response.Status = status;
        context.Response.ResponseValue = value;

        if (produced != null)
        {
            context.HandlerInfo = new ExecutionRequestHandlerInfo(
                "/products/{sku}",
                "GET",
                typeof(ProblemJsonTests),
                "Get",
                producedContentTypes: produced,
                errorContentTypes: errors
            );
        }

        return context;
    }

    private static async Task<string?> Written(IExecutionContext context)
    {
        var serializer = Locator(Json()).FindResponseSerializer(context);

        await serializer.SerializeResponse(context);

        return context.Response.ContentType;
    }

    // ---------------------------------------------------------------- what is a problem

    [Fact]
    public void AFailureWhoseBodyIsAProblemIsLabelledAsOne()
    {
        var context = Context("*/*", 404, new Missing("No product."));

        Assert.Equal(KnownContentType.ProblemJson, ProblemJson.ContentTypeFor(context));
    }

    [Fact]
    public void AFailureWhoseBodyIsNotAProblemIsJson()
    {
        var context = Context("*/*", 404, new ApiError("missing"));

        Assert.Equal(KnownContentType.Json, ProblemJson.ContentTypeFor(context));
    }

    /// <summary>
    /// A problem only ever labels a failure. A success whose body happens to implement the interface
    /// is the representation the client asked for.
    /// </summary>
    [Fact]
    public void ASuccessIsNeverLabelledAsAProblem()
    {
        var context = Context("*/*", 200, new Missing());

        Assert.Equal(KnownContentType.Json, ProblemJson.ContentTypeFor(context));
    }

    [Fact]
    public void AFailureCommittedToProblemJsonStaysThere()
    {
        var context = Context("*/*", 404, new ApiError("missing"));

        context.Response.ContentType = "application/problem+json; charset=utf-8";

        Assert.Equal(KnownContentType.ProblemJson, ProblemJson.ContentTypeFor(context));
    }

    [Fact]
    public void TheJsonSerializerProducesProblemJsonForAFailureAndNothingElse()
    {
        var serializer = Json();

        Assert.True(
            serializer.CanProduce(
                KnownContentType.ProblemJson,
                Context("*/*", 404, new ApiError("x"))
            )
        );
        Assert.False(
            serializer.CanProduce(
                KnownContentType.ProblemJson,
                Context("*/*", 200, new Product("1"))
            )
        );
    }

    // ---------------------------------------------------------------- negotiation

    /// <summary>
    /// B-01: a failure declared only as <c>application/problem+json</c> answered 500, because no
    /// registered serializer produced it.
    /// </summary>
    [Fact]
    public async Task AFailureDeclaredOnlyAsProblemJsonIsWrittenAsIt()
    {
        var context = Context(
            "*/*",
            404,
            new ApiError("missing"),
            produced: [KnownContentType.ProblemJson]
        );

        Assert.Equal(KnownContentType.ProblemJson, await Written(context));
        Assert.Equal(
            """{"code":"missing"}""",
            Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray())
        );
    }

    /// <summary>
    /// B-02: with JSON declared for the success, a failure declared as a problem went out as JSON
    /// for <c>*/*</c>, which is what curl and most clients send.
    /// </summary>
    [Theory]
    [InlineData("*/*")]
    [InlineData(null)]
    [InlineData("application/problem+json")]
    [InlineData("application/json")]
    [InlineData("application/json, application/problem+json")]
    public async Task AFailureDeclaredApartIsNegotiatedWithinItsOwnTypes(string? accept)
    {
        var context = Context(
            accept,
            404,
            new ApiError("missing"),
            produced: [KnownContentType.Json, KnownContentType.ProblemJson],
            errors: [KnownContentType.ProblemJson]
        );

        Assert.Equal(KnownContentType.ProblemJson, await Written(context));
    }

    /// <summary>
    /// A client naming <c>application/json</c> gets it where the failure is declared that way too.
    /// The problem document is a fallback for a client asking for JSON, not a preference over what
    /// it named.
    /// </summary>
    [Fact]
    public async Task AClientNamingJsonGetsJsonWhereTheFailureDeclaresBoth()
    {
        var context = Context(
            "application/json",
            409,
            new ApiError("taken"),
            produced: [KnownContentType.Json, KnownContentType.ProblemJson],
            errors: [KnownContentType.ProblemJson, KnownContentType.Json]
        );

        Assert.Equal(KnownContentType.Json, await Written(context));
    }

    /// <summary>
    /// The success is negotiated within the whole set, and a client asking for a problem is not
    /// handed the product labelled as one. That was the trap in the trial's hand-written
    /// serializer: without the failure check, <c>Accept: application/problem+json</c> got a 200
    /// labelled as a problem.
    /// </summary>
    [Fact]
    public void ASuccessIsNotNegotiatedIntoProblemJson()
    {
        var context = Context(
            "application/problem+json",
            200,
            new Product("1"),
            produced: [KnownContentType.Json, KnownContentType.ProblemJson],
            errors: [KnownContentType.ProblemJson]
        );

        Assert.Throws<NotAcceptableException>(() =>
            Locator(Json()).FindResponseSerializer(context)
        );
    }

    /// <summary>
    /// A code-first failure whose body is a problem record: nothing declared the media type, the
    /// JSON serializer writes it, and the body decides the label.
    /// </summary>
    [Fact]
    public async Task AProblemRecordFromAnOperationDeclaringNothingIsLabelledAsOne()
    {
        var context = Context("application/json", 404, new Missing("No product."));

        Assert.Equal(KnownContentType.ProblemJson, await Written(context));
    }

    [Fact]
    public async Task ASuccessFromAnOperationDeclaringNothingIsJson()
    {
        var context = Context("*/*", 200, new Product("1"));

        Assert.Equal(KnownContentType.Json, await Written(context));
    }
}
