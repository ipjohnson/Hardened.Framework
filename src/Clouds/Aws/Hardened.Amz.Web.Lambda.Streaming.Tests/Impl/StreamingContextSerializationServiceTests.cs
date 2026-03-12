using Hardened.Amz.Web.Lambda.Streaming.Impl;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using NSubstitute;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Streaming.Tests.Impl;

public class StreamingContextSerializationServiceTests {
    private readonly ISerializationLocatorService _locatorService;
    private readonly IExceptionResponseSerializer _exceptionSerializer;
    private readonly INullValueResponseHandler _nullValueResponse;
    private readonly StreamingContextSerializationService _service;

    public StreamingContextSerializationServiceTests() {
        _locatorService = Substitute.For<ISerializationLocatorService>();
        _exceptionSerializer = Substitute.For<IExceptionResponseSerializer>();
        _nullValueResponse = Substitute.For<INullValueResponseHandler>();
        _service = new StreamingContextSerializationService(
            _locatorService, _exceptionSerializer, _nullValueResponse);
    }

    private static IExecutionContext CreateContext(
        object? responseValue = null,
        Exception? exception = null,
        DefaultOutputFunc? defaultOutput = null,
        Stream? body = null) {
        var context = Substitute.For<IExecutionContext>();
        var response = Substitute.For<IExecutionResponse>();

        response.ResponseValue.Returns(responseValue);
        response.ExceptionValue.Returns(exception);
        response.Body.Returns(body ?? new MemoryStream());
        response.ContentType.Returns((string?)null);
        response.ShouldSerialize.Returns(true);
        context.Response.Returns(response);
        context.DefaultOutput.Returns(defaultOutput);
        context.CancellationToken.Returns(CancellationToken.None);

        return context;
    }

    [Fact]
    public async Task SerializeResponse_UsesDefaultOutput_WhenPresent() {
        var called = false;
        DefaultOutputFunc defaultOutput = _ => {
            called = true;
            return Task.CompletedTask;
        };

        var context = CreateContext(defaultOutput: defaultOutput, responseValue: "some value");

        await _service.SerializeResponse(context);

        Assert.True(called);
        await _exceptionSerializer.DidNotReceiveWithAnyArgs().Handle(default!, default!);
    }

    [Fact]
    public async Task SerializeResponse_UsesExceptionSerializer_WhenExceptionPresent() {
        var ex = new InvalidOperationException("test error");
        var context = CreateContext(exception: ex, responseValue: "ignored");

        await _service.SerializeResponse(context);

        await _exceptionSerializer.Received(1).Handle(context, ex);
    }

    [Fact]
    public async Task SerializeResponse_UsesNullHandler_WhenResponseValueNull() {
        var context = CreateContext(responseValue: null);

        await _service.SerializeResponse(context);

        await _nullValueResponse.Received(1).Handle(context);
    }

    [Fact]
    public async Task SerializeResponse_UsesLocatorService_ForStandardValues() {
        var context = CreateContext(responseValue: "hello");
        var serializer = Substitute.For<IResponseSerializer>();
        _locatorService.FindResponseSerializer(context).Returns(serializer);

        await _service.SerializeResponse(context);

        await serializer.Received(1).SerializeResponse(context);
    }

    [Fact]
    public async Task DeserializeRequestBody_DelegatesToLocatorService() {
        var context = CreateContext();
        var deserializer = Substitute.For<IRequestDeserializer>();
        deserializer.DeserializeRequestBody<string>(context)
            .Returns(new ValueTask<string?>("result"));
        _locatorService.FindRequestDeserializer(context).Returns(deserializer);

        var result = await _service.DeserializeRequestBody<string>(context);

        Assert.Equal("result", result);
    }

}
