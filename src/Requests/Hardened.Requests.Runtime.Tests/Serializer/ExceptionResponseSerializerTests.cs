using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
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

        public Fixture() {
            Locator.FindResponseSerializer(Arg.Any<IExecutionContext>()).Returns(Serializer);
            Serializer.SerializeResponse(Arg.Any<IExecutionContext>()).Returns(Task.CompletedTask);
        }

        public ExceptionResponseSerializer Subject => new(
            Substitute.For<IRequestLogger>(), Locator, Converter);
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
}
