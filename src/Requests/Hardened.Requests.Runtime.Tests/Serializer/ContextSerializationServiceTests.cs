using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Abstract.Templates;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Tests.Support;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// One decision, made once per response: template, error, nothing, or a value. Each arm sends
/// the response somewhere different, and their precedence is what decides whether a handler
/// that threw an exception on a templated route renders a template or an error.
/// </summary>
public class ContextSerializationServiceTests {

    private class Fixture {
        public ISerializationLocatorService Locator { get; } = Substitute.For<ISerializationLocatorService>();

        public INullValueResponseHandler NullValues { get; } = Substitute.For<INullValueResponseHandler>();

        public IExceptionResponseSerializer Exceptions { get; } =
            Substitute.For<IExceptionResponseSerializer>();

        public IResponseSerializer ResponseSerializer { get; } = Substitute.For<IResponseSerializer>();

        public IRequestDeserializer RequestDeserializer { get; } = Substitute.For<IRequestDeserializer>();

        public ITemplateResponseSerializer Templates { get; } = Substitute.For<ITemplateResponseSerializer>();

        public Fixture() {
            Locator.FindResponseSerializer(Arg.Any<IExecutionContext>()).Returns(ResponseSerializer);
            Locator.FindRequestDeserializer(Arg.Any<IExecutionContext>()).Returns(RequestDeserializer);
            NullValues.Handle(Arg.Any<IExecutionContext>()).Returns(Task.CompletedTask);
            Exceptions.Handle(Arg.Any<IExecutionContext>(), Arg.Any<Exception>()).Returns(Task.CompletedTask);
            ResponseSerializer.SerializeResponse(Arg.Any<IExecutionContext>()).Returns(Task.CompletedTask);
            Templates.CanProcessContext(Arg.Any<IExecutionContext>()).Returns(false);
            Templates.SerializeResponse(Arg.Any<IExecutionContext>()).Returns(Task.CompletedTask);
        }

        public ContextSerializationService Service => new(
            Pipeline.Logger<ContextSerializationService>(), Locator, NullValues, Exceptions, Templates);
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

    /// <summary>
    /// A template response is written by the template serializer and the locator is never consulted.
    /// </summary>
    /// <remarks>
    /// The ordering here is the whole reason the template serializer is not resolved through the
    /// locator. The locator returns the first registered serializer that claims the context, and a
    /// request carrying <c>Accept: application/json</c> alongside a template name satisfies both the
    /// JSON serializer and the template one - so the answer came down to which was registered later,
    /// and both are registered by one module. Routing templates through it made <c>/fortunes</c>
    /// return a JSON-serialized model with a content type of application/json.
    /// </remarks>
    [Fact]
    public async Task ATemplateResponseIsWrittenByTheTemplateSerializerAheadOfTheLocator() {
        var fixture = new Fixture();
        var context = Pipeline.Context();

        context.Response.ResponseValue = new { Fortunes = 3 };
        fixture.Templates.CanProcessContext(context).Returns(true);

        await fixture.Service.SerializeResponse(context);

        await fixture.Templates.Received(1).SerializeResponse(context);
        fixture.Locator.DidNotReceive().FindResponseSerializer(Arg.Any<IExecutionContext>());
    }

    /// <summary>
    /// A response the template serializer does not claim still reaches the locator, so asking first
    /// costs nothing for the ordinary case.
    /// </summary>
    [Fact]
    public async Task AResponseTheTemplateSerializerDeclinesFallsThroughToTheLocator() {
        var fixture = new Fixture();
        var context = Pipeline.Context();

        context.Response.ResponseValue = "value";
        fixture.Templates.CanProcessContext(context).Returns(false);

        await fixture.Service.SerializeResponse(context);

        await fixture.ResponseSerializer.Received(1).SerializeResponse(context);
        await fixture.Templates.DidNotReceive().SerializeResponse(Arg.Any<IExecutionContext>());
    }

    /// <summary>
    /// DefaultOutput still wins, so a raw or attribute-driven response is unaffected by any of this.
    /// </summary>
    [Fact]
    public async Task DefaultOutputStillTakesPrecedenceOverATemplate() {
        var fixture = new Fixture();
        var context = Pipeline.Context();
        var ranDefaultOutput = false;

        context.Response.ResponseValue = "value";
        context.DefaultOutput = _ => {
            ranDefaultOutput = true;

            return Task.CompletedTask;
        };
        fixture.Templates.CanProcessContext(context).Returns(true);

        await fixture.Service.SerializeResponse(context);

        Assert.True(ranDefaultOutput);
        await fixture.Templates.DidNotReceive().SerializeResponse(Arg.Any<IExecutionContext>());
    }
}
