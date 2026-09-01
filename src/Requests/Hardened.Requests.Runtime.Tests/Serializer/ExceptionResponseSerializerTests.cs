using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Tests.Support;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// Turning a thrown exception into a response. The converter decides the status and the model;
/// this serializer is what actually puts them on the response and sends them.
/// </summary>
public class ExceptionResponseSerializerTests {

    private class Fixture {
        public ISerializationLocatorService Locator { get; } = Substitute.For<ISerializationLocatorService>();

        public IExceptionToModelConverter Converter { get; } =
            Substitute.For<IExceptionToModelConverter>();

        public IResponseSerializer Serializer { get; } = Substitute.For<IResponseSerializer>();

        public IRequestLogger Logger { get; } = Substitute.For<IRequestLogger>();

        public Fixture() {
            Locator.FindResponseSerializer(Arg.Any<IExecutionContext>()).Returns(Serializer);
            Serializer.SerializeResponse(Arg.Any<IExecutionContext>()).Returns(Task.CompletedTask);
        }

        public ExceptionResponseSerializer Subject => new(Logger, Locator, Converter);
    }

    /// <summary>
    /// The status the converter chose is the status the caller sees.
    /// </summary>
    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(500)]
    public async Task TheConvertersStatusIsPutOnTheResponse(int status) {
        var fixture = new Fixture();
        var context = Pipeline.Context();

        fixture.Converter.ConvertExceptionToModel(context, Arg.Any<Exception>())
            .Returns((status, new ErrorModel()));

        await fixture.Subject.Handle(context, new Exception("failed"));

        Assert.Equal(status, context.Response.Status);
    }

    /// <summary>
    /// The error model replaces whatever the handler had produced, so a partial result is not
    /// serialized alongside the error that interrupted it.
    /// </summary>
    [Fact]
    public async Task TheErrorModelReplacesThePartialResponseValue() {
        var fixture = new Fixture();
        var context = Pipeline.Context();
        var model = new ErrorModel { Type = "InvalidOperationException", Message = "failed" };

        context.Response.ResponseValue = "half an answer";

        fixture.Converter.ConvertExceptionToModel(context, Arg.Any<Exception>()).Returns((500, model));

        await fixture.Subject.Handle(context, new InvalidOperationException("failed"));

        Assert.Same(model, context.Response.ResponseValue);
    }

    /// <summary>
    /// The error document goes out through the same serializer negotiation as a successful
    /// response, so a caller that asked for a particular representation gets its errors in it
    /// too.
    /// </summary>
    [Fact]
    public async Task TheErrorGoesOutThroughTheNegotiatedResponseSerializer() {
        var fixture = new Fixture();
        var context = Pipeline.Context();

        fixture.Converter.ConvertExceptionToModel(context, Arg.Any<Exception>())
            .Returns((500, new ErrorModel()));

        await fixture.Subject.Handle(context, new Exception("failed"));

        fixture.Locator.Received(1).FindResponseSerializer(context);
        await fixture.Serializer.Received(1).SerializeResponse(context);
    }

    /// <summary>
    /// The exception reaches the converter unwrapped - the converter classifies by type, so a
    /// wrapped exception would be classified as the wrapper.
    /// </summary>
    [Fact]
    public async Task TheExceptionReachesTheConverterUnwrapped() {
        var fixture = new Fixture();
        var context = Pipeline.Context();
        var failure = new BadRequestException("malformed");

        fixture.Converter.ConvertExceptionToModel(Arg.Any<IExecutionContext>(), Arg.Any<Exception>())
            .Returns((400, new ErrorModel()));

        await fixture.Subject.Handle(context, failure);

        fixture.Converter.Received(1).ConvertExceptionToModel(context, failure);
    }

    // ------------------------------------------------------------------- a committed content type

    /// <summary>
    /// A committed content type the error model cannot travel as is recommitted to JSON rather
    /// than escaping as the locator's configuration fault. This is <c>[RawResponse]</c> plus a
    /// thrown exception, which used to reach the caller as an empty 500.
    /// </summary>
    [Fact]
    public async Task ACommittedTypeThatCannotCarryTheErrorIsRecommittedToJson() {
        var fixture = new Fixture();
        var context = Pipeline.Context();

        context.Response.ContentType = "image/png";

        fixture.Converter.ConvertExceptionToModel(context, Arg.Any<Exception>())
            .Returns((500, new ErrorModel()));

        fixture.Locator.FindResponseSerializer(Arg.Any<IExecutionContext>())
            .Returns(_ => {
                if (context.Response.ContentType != KnownContentType.Json) {
                    throw new ContentTypeNotProducibleException(
                        "Response committed to content type 'image/png' but no registered " +
                        "serializer can produce it.");
                }

                return fixture.Serializer;
            });

        await fixture.Subject.Handle(context, new InvalidOperationException("failed"));

        Assert.Equal(KnownContentType.Json, context.Response.ContentType);
        await fixture.Serializer.Received(1).SerializeResponse(context);
    }

    /// <summary>
    /// A committed type the error can travel as is honoured. Recommitting is a rescue, not a
    /// default: a caller of an XML operation still gets its errors in XML.
    /// </summary>
    [Fact]
    public async Task AProducibleCommittedTypeKeepsTheError() {
        var fixture = new Fixture();
        var context = Pipeline.Context();

        context.Response.ContentType = "application/xml";

        fixture.Converter.ConvertExceptionToModel(context, Arg.Any<Exception>())
            .Returns((500, new ErrorModel()));

        await fixture.Subject.Handle(context, new InvalidOperationException("failed"));

        Assert.Equal("application/xml", context.Response.ContentType);
        await fixture.Serializer.Received(1).SerializeResponse(context);
    }

    /// <summary>
    /// A client asking for representations the operation does not have is a different refusal, and
    /// it keeps travelling to the 406 path rather than being flattened into a JSON error here.
    /// </summary>
    [Fact]
    public async Task ANotAcceptableRefusalIsNotFlattenedToJson() {
        var fixture = new Fixture();
        var context = Pipeline.Context();

        fixture.Converter.ConvertExceptionToModel(context, Arg.Any<Exception>())
            .Returns((400, new ErrorModel()));

        fixture.Locator.FindResponseSerializer(Arg.Any<IExecutionContext>())
            .Returns(_ => throw new NotAcceptableException(new[] { "text/plain" }));

        await Assert.ThrowsAsync<NotAcceptableException>(
            () => fixture.Subject.Handle(context, new Exception("failed")));
    }

    // ------------------------------------------------------------------------------ logging

    /// <summary>
    /// The failure is reported, whatever produced it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole of the defect it was written for. Four things put an exception on the
    /// response - a handler fault, a bind failure, an exception thrown by a filter, and a refusal
    /// recorded by <c>AuthorizationFilter</c> or <c>RateLimitFilter</c> - and only the first two
    /// logged. A request refused by the validation filter produced <c>started</c>, <c>mapped</c>
    /// and <c>finished status code '500'</c>, with nothing anywhere naming the exception.
    /// </para>
    /// <para>
    /// Asserted here rather than at each of the four, because the point of the fix is that none of
    /// them has to know about logging.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheFailureIsReportedToTheRequestLogger() {
        var fixture = new Fixture();
        var context = Pipeline.Context();
        var failure = new InvalidOperationException("thrown by a filter");

        fixture.Converter.ConvertExceptionToModel(context, Arg.Any<Exception>())
            .Returns((500, new ErrorModel()));

        await fixture.Subject.Handle(context, failure);

        fixture.Logger.Received(1).RequestFailed(context, failure);
    }

    /// <summary>
    /// Reported after the status is assigned, so the logger can read the status this failure
    /// answers with.
    /// </summary>
    /// <remarks>
    /// Ordering rather than decoration: severity follows the answer, and a logger that ran first
    /// would see whatever the response happened to carry before the converter decided.
    /// </remarks>
    [Fact]
    public async Task TheStatusIsOnTheResponseBeforeTheFailureIsReported() {
        var fixture = new Fixture();
        var context = Pipeline.Context();
        int? statusWhenLogged = null;

        fixture.Converter.ConvertExceptionToModel(context, Arg.Any<Exception>())
            .Returns((404, new ErrorModel()));

        fixture.Logger
            .When(logger => logger.RequestFailed(Arg.Any<IExecutionContext>(), Arg.Any<Exception>()))
            .Do(_ => statusWhenLogged = context.Response.Status);

        await fixture.Subject.Handle(context, new Exception("missing"));

        Assert.Equal(404, statusWhenLogged);
    }

    /// <summary>
    /// Reported once. The producers no longer log, so a handler fault that reaches here produces
    /// one line rather than two.
    /// </summary>
    [Fact]
    public async Task TheFailureIsReportedOnlyOnce() {
        var fixture = new Fixture();
        var context = Pipeline.Context();
        var failure = new InvalidOperationException("handler failed");

        fixture.Converter.ConvertExceptionToModel(context, Arg.Any<Exception>())
            .Returns((500, new ErrorModel()));

        await ControllerErrorHelper.HandleException(context, failure);
        await fixture.Subject.Handle(context, context.Response.ExceptionValue!);

        fixture.Logger.Received(1).RequestFailed(Arg.Any<IExecutionContext>(), Arg.Any<Exception>());
    }
}
