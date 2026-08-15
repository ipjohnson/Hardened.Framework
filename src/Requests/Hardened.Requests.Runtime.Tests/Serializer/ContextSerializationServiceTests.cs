using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Tests.Support;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// One decision, made once per response: error, output, nothing, or a value. Each arm sends the
/// response somewhere different, and their precedence is what decides whether a handler that threw
/// on a route with a view renders the view or an error.
/// </summary>
public class ContextSerializationServiceTests {

    /// <summary>An output that records what it was asked and what it wrote.</summary>
    private class RecordingOutput : IHardenedResponseOutput {
        private readonly bool _supports;

        public RecordingOutput(bool supports = true) {
            _supports = supports;
        }

        public string? AskedAbout { get; private set; }

        public int Writes { get; private set; }

        public bool SupportsContentType(string? accept, IExecutionContext context) {
            AskedAbout = accept;

            return _supports;
        }

        public Task WriteOutput(IExecutionContext context) {
            Writes++;

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A declared output writes the response, and the locator is never consulted.
    /// </summary>
    [Fact]
    public async Task ADeclaredOutputWritesTheResponse() {
        var fixture = new Fixture();
        var context = Pipeline.Context();
        var output = new RecordingOutput();

        context.Response.ResponseValue = new { Secret = "value" };
        context.Response.OutputFactory = _ => output;

        await fixture.Service.SerializeResponse(context);

        Assert.Equal(1, output.Writes);
        await fixture.ResponseSerializer.DidNotReceive().SerializeResponse(Arg.Any<IExecutionContext>());
    }

    /// <summary>
    /// An output the client will not take is a 406, never the model serialized instead.
    /// </summary>
    /// <remarks>
    /// This is a data-leak fix rather than a status-code preference. A view usually renders a subset
    /// of what its model holds - a page showing a customer's name, from a model carrying their
    /// address and every internal identifier attached to them. Falling back to JSON because the
    /// client asked for it would put all of it on the wire, from a route whose author wrote nothing
    /// but a view.
    /// </remarks>
    [Fact]
    public async Task AnOutputTheClientWillNotTakeIs406AndNothingElse() {
        var fixture = new Fixture();
        var context = Pipeline.Context(accept: "application/json");
        var output = new RecordingOutput(supports: false);

        context.Response.ResponseValue = new { Secret = "value" };
        context.Response.OutputFactory = _ => output;

        await fixture.Service.SerializeResponse(context);

        Assert.Equal(406, context.Response.Status);
        Assert.Equal(0, output.Writes);
        Assert.False(context.Response.ShouldSerialize);
        await fixture.ResponseSerializer.DidNotReceive().SerializeResponse(Arg.Any<IExecutionContext>());
    }

    /// <summary>The output is asked about the request's own Accept header.</summary>
    [Fact]
    public async Task TheOutputIsAskedAboutTheRequestsAcceptHeader() {
        var fixture = new Fixture();
        var context = Pipeline.Context(accept: "text/html, */*");
        var output = new RecordingOutput();

        context.Response.OutputFactory = _ => output;

        await fixture.Service.SerializeResponse(context);

        Assert.Equal("text/html, */*", output.AskedAbout);
    }

    /// <summary>
    /// Built once and kept, so a filter that read it back gets the same instance the response was
    /// written with.
    /// </summary>
    [Fact]
    public async Task TheOutputIsBuiltOnce() {
        var fixture = new Fixture();
        var context = Pipeline.Context();
        var built = 0;

        context.Response.OutputFactory = _ => {
            built++;

            return new RecordingOutput();
        };

        await fixture.Service.SerializeResponse(context);
        await fixture.Service.SerializeResponse(context);

        Assert.Equal(1, built);
        Assert.NotNull(context.Response.Output);
    }

    /// <summary>
    /// An exception outranks the output. A handler that threw has no model to render, and handing
    /// an exception to a view typed for something else would replace a legible error response with
    /// a cast failure inside the render.
    /// </summary>
    [Fact]
    public async Task AnExceptionOutranksTheOutput() {
        var fixture = new Fixture();
        var context = Pipeline.Context();
        var output = new RecordingOutput();

        context.Response.OutputFactory = _ => output;
        context.Response.ExceptionValue = new InvalidOperationException("boom");

        await fixture.Service.SerializeResponse(context);

        Assert.Equal(0, output.Writes);
        await fixture.Exceptions.Received(1).Handle(context, Arg.Any<Exception>());
    }

    private class Fixture {
        public ISerializationLocatorService Locator { get; } = Substitute.For<ISerializationLocatorService>();

        public INullValueResponseHandler NullValues { get; } = Substitute.For<INullValueResponseHandler>();

        public IExceptionResponseSerializer Exceptions { get; } =
            Substitute.For<IExceptionResponseSerializer>();

        public IResponseSerializer ResponseSerializer { get; } = Substitute.For<IResponseSerializer>();

        public IRequestDeserializer RequestDeserializer { get; } = Substitute.For<IRequestDeserializer>();

        public Fixture() {
            Locator.FindResponseSerializer(Arg.Any<IExecutionContext>()).Returns(ResponseSerializer);
            Locator.FindRequestDeserializer(Arg.Any<IExecutionContext>()).Returns(RequestDeserializer);
            NullValues.Handle(Arg.Any<IExecutionContext>()).Returns(Task.CompletedTask);
            Exceptions.Handle(Arg.Any<IExecutionContext>(), Arg.Any<Exception>()).Returns(Task.CompletedTask);
            ResponseSerializer.SerializeResponse(Arg.Any<IExecutionContext>()).Returns(Task.CompletedTask);
        }

        public ContextSerializationService Service => new(
            Pipeline.Logger<ContextSerializationService>(), Locator, NullValues, Exceptions);
    }

    [Fact]
    public async Task AResponseValueIsHandedToTheLocatedSerializer() {
        var fixture = new Fixture();
        var context = Pipeline.Context();

        context.Response.ResponseValue = new { Name = "value" };

        await fixture.Service.SerializeResponse(context);

        await fixture.ResponseSerializer.Received(1).SerializeResponse(context);
        await fixture.NullValues.DidNotReceive().Handle(Arg.Any<IExecutionContext>());
    }

    [Fact]
    public async Task ANullResponseValueGoesToTheNullValueHandler() {
        var fixture = new Fixture();
        var context = Pipeline.Context();

        await fixture.Service.SerializeResponse(context);

        await fixture.NullValues.Received(1).Handle(context);
        await fixture.ResponseSerializer.DidNotReceive().SerializeResponse(Arg.Any<IExecutionContext>());
    }

    /// <summary>
    /// An exception wins over a response value. A handler that produced a partial result and
    /// then failed must report the failure, not the partial result.
    /// </summary>
    [Fact]
    public async Task AnExceptionWinsOverAResponseValue() {
        var fixture = new Fixture();
        var context = Pipeline.Context();
        var failure = new InvalidOperationException("failed");

        context.Response.ResponseValue = "partial result";
        context.Response.ExceptionValue = failure;

        await fixture.Service.SerializeResponse(context);

        await fixture.Exceptions.Received(1).Handle(context, failure);
        await fixture.ResponseSerializer.DidNotReceive().SerializeResponse(Arg.Any<IExecutionContext>());
    }

    /// <summary>
    /// A default output function - what <c>[RawResponse]</c> installs -
    /// takes precedence over everything, including an exception. A templated route that throws
    /// renders through its template rather than emitting a JSON error document.
    /// </summary>
    [Fact]
    public async Task ADefaultOutputFunctionTakesPrecedenceOverEverythingElse() {
        var fixture = new Fixture();
        var context = Pipeline.Context();
        var invoked = 0;

        context.DefaultOutput = _ => {
            invoked++;

            return Task.CompletedTask;
        };

        context.Response.ExceptionValue = new Exception("failed");
        context.Response.ResponseValue = "value";

        await fixture.Service.SerializeResponse(context);

        Assert.Equal(1, invoked);
        await fixture.Exceptions.DidNotReceive().Handle(Arg.Any<IExecutionContext>(), Arg.Any<Exception>());
        await fixture.ResponseSerializer.DidNotReceive().SerializeResponse(Arg.Any<IExecutionContext>());
    }

    /// <summary>
    /// The default output function receives the context it is serializing, not a fresh one -
    /// a template renderer needs the response value it is rendering.
    /// </summary>
    [Fact]
    public async Task ADefaultOutputFunctionReceivesTheContextBeingSerialized() {
        var fixture = new Fixture();
        var context = Pipeline.Context();

        IExecutionContext? seen = null;
        context.DefaultOutput = c => {
            seen = c;

            return Task.CompletedTask;
        };

        await fixture.Service.SerializeResponse(context);

        Assert.Same(context, seen);
    }

    [Fact]
    public async Task DeserializationIsDelegatedToTheLocatedDeserializer() {
        var fixture = new Fixture();
        var context = Pipeline.Context();

        fixture.RequestDeserializer.DeserializeRequestBody<string>(context)
            .Returns(new ValueTask<string?>("body"));

        var result = await fixture.Service.DeserializeRequestBody<string>(context);

        Assert.Equal("body", result);
        fixture.Locator.Received(1).FindRequestDeserializer(context);
    }
}
