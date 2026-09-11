using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Streaming;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Execution;

/// <summary>
/// What a handler's pipeline resolves its writer to, and what is left for a request to decide.
/// </summary>
/// <remarks>
/// <para>
/// The point of the whole design: an operation states what it produces at build, so a request
/// should cost the call and nothing else. Asserted by what reaches
/// <c>IContextSerializationService</c> - a bound serializer, or null meaning "locate one" - because
/// that is the seam where the work either happens or does not.
/// </para>
/// <para>
/// A request used to pay a container resolve, an <c>Accept</c> parse into a
/// <c>List&lt;string&gt;</c>, and a nested search over accepted types by declared types by
/// registered serializers, to arrive at an answer that had not changed since the application
/// started.
/// </para>
/// </remarks>
public class ResponseWriterBindingTests {

    private sealed class Csv : IResponseSerializer {
        public bool IsDefaultSerializer => false;

        public string ContentType => "text/csv";

        public Task SerializeResponse(IExecutionContext context) => Task.CompletedTask;
    }

    private static IOptions<IJsonSerializerConfiguration> JsonConfiguration() =>
        Options.Create<IJsonSerializerConfiguration>(new JsonSerializerConfiguration());

    /// <summary>
    /// The serializer the composed filter hands to the serialization service, or null where it
    /// left the decision to the request.
    /// </summary>
    private static async Task<IResponseSerializer?> BoundFor(
        IExecutionRequestHandlerInfo handlerInfo,
        ContentNegotiationMode mode = ContentNegotiationMode.Lenient,
        IResponseSerializer[]? serializers = null) {
        IResponseSerializer? bound = null;
        var located = false;

        var serialization = Substitute.For<IContextSerializationService>();

        serialization.SerializeResponse(Arg.Any<IExecutionContext>())
            .Returns(_ => {
                located = true;

                return Task.CompletedTask;
            });

        serialization.SerializeResponse(
                Arg.Any<IExecutionContext>(), Arg.Any<IResponseSerializer?>(), Arg.Any<string?>())
            .Returns(call => {
                bound = (IResponseSerializer?)call[1];

                return Task.CompletedTask;
            });

        var provider = new IOFilterProvider(
            serialization,
            Options.Create<IResponseHeaderConfiguration>(new ResponseHeaderConfiguration()),
            Options.Create<IStreamingConfiguration>(new StreamingConfiguration()),
            new SerializationLocatorService(
                [],
                serializers ?? [new SystemTextJsonResponseSerializer(JsonConfiguration(), []), new Csv()]),
            new RawResponseSerializer(),
            new StreamingJsonResponseSerializer(JsonConfiguration(), []),
            new ContentNegotiationPolicy(mode));

        var filter = provider.ProvideFilter(handlerInfo, _ => Task.FromResult(EmptyParameters.Instance));

        var context = Pipeline.Context();

        context.Response.ResponseValue = "written";

        await Pipeline.Chain(context, filter).Next();

        Assert.True(bound != null || located, "the filter serialized through neither path");

        return bound;
    }

    private static IExecutionRequestHandlerInfo Handler(
        IReadOnlyList<string>? produces = null, bool writesRawBytes = false) =>
        new ExecutionRequestHandlerInfo(
            "/orders",
            "GET",
            typeof(ResponseWriterBindingTests),
            "Read",
            producedContentTypes: produces,
            writesRawBytes: writesRawBytes);

    /// <summary>
    /// A handler returning <c>byte[]</c> or <c>Stream</c> takes the pass-through writer, and takes
    /// it from its return type rather than from any media type. Nothing is looked up.
    /// </summary>
    [Fact]
    public async Task AHandlerThatWritesItsOwnBytesTakesTheRawWriter() {
        Assert.IsType<RawResponseSerializer>(
            await BoundFor(Handler(["application/pdf"], writesRawBytes: true)));
    }

    /// <summary>
    /// And still does under Strict, because there is nothing to negotiate: no serializer could
    /// write those bytes as anything else.
    /// </summary>
    [Fact]
    public async Task TheRawWriterIsBoundUnderStrictToo() {
        Assert.IsType<RawResponseSerializer>(
            await BoundFor(
                Handler(["application/pdf"], writesRawBytes: true), ContentNegotiationMode.Strict));
    }

    /// <summary>
    /// An operation that declares nothing takes the service default. This is the common case and
    /// the one the allocation was measured on.
    /// </summary>
    [Fact]
    public async Task AnOperationThatDeclaresNothingTakesTheDefaultSerializer() {
        Assert.IsType<SystemTextJsonResponseSerializer>(await BoundFor(Handler()));
    }

    /// <summary>One declared media type resolves to the serializer that writes it.</summary>
    [Fact]
    public async Task OneDeclaredMediaTypeResolvesToItsSerializer() {
        Assert.IsType<Csv>(await BoundFor(Handler(["text/csv"])));
    }

    /// <summary>
    /// Under Strict a mismatch is a 406, and answering that needs the header - so those operations
    /// keep negotiating rather than binding.
    /// </summary>
    [Fact]
    public async Task OneDeclaredMediaTypeStillNegotiatesUnderStrict() {
        Assert.Null(await BoundFor(Handler(["text/csv"]), ContentNegotiationMode.Strict));
    }

    /// <summary>
    /// A set of two is a genuine choice, and is the one shape that has to read <c>Accept</c>.
    /// </summary>
    [Fact]
    public async Task ASetOfTwoNegotiates() {
        Assert.Null(await BoundFor(Handler(["text/csv", "application/json"])));
    }

    /// <summary>
    /// A declared type nothing writes is not bound to something else. It reaches the locator, which
    /// answers it the way it always did - a configuration fault, named.
    /// </summary>
    [Fact]
    public async Task ADeclaredTypeNothingProducesIsNotBound() {
        Assert.Null(await BoundFor(Handler(["application/vnd.msgpack"])));
    }
}
