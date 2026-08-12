using Hardened.Amz.Function.Lambda.Runtime.Filter;
using Hardened.Amz.Function.Lambda.Runtime.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Hardened.Amz.Function.Lambda.Runtime.Tests.Filter;

/// <summary>
/// The default batch exception handler. Its return value decides whether a record that threw
/// becomes a batch failure, and the default is that it does — an unhandled exception must leave
/// the message on the queue rather than quietly acknowledging it.
/// </summary>
public class BatchProcessorExceptionHandlerTests {

    [Fact]
    public async Task AnUnhandledExceptionLeavesTheRecordFailed() {
        var context = TestExecutionContext.Create(new MemoryStream(), new MemoryStream());

        var handled = await new BatchProcessorExceptionHandler().HandleException(
            context, Substitute.For<ILogger>(), new InvalidOperationException("boom"));

        Assert.False(handled);
    }

    [Fact]
    public async Task TheExceptionIsLoggedAtErrorLevel() {
        var logger = Substitute.For<ILogger>();
        var context = TestExecutionContext.Create(new MemoryStream(), new MemoryStream());
        var exception = new InvalidOperationException("boom");

        await new BatchProcessorExceptionHandler().HandleException(context, logger, exception);

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            exception,
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }
}
