using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
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
    /// A default output function - what <c>[Template]</c> and <c>[RawResponse]</c> install -
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
