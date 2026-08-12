using Amazon.Lambda.SQSEvents;
using Hardened.Amz.Function.Sqs.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Hardened.Amz.Function.Sqs.Runtime.Tests;

/// <summary>
/// <see cref="ISqsExceptionHandler"/> is registered as a singleton and is public API, but as of
/// 2026-08-11 nothing in the repository resolves it: <c>SqsBatchFilter</c> inherits its exception
/// path from <c>BaseBatchExecutionFilter</c>, which asks
/// <c>IBatchProcessorExceptionHandler</c> instead. A consumer implementing
/// <see cref="ISqsExceptionHandler"/> and registering it would see it never called.
///
/// <para>
/// Raised as a contradiction rather than asserted as behaviour — see TESTING-PLAN.md §2.3 and
/// testing-conventions.md §6. These tests pin what the type does on its own; they deliberately do
/// not claim it participates in batch processing, because it does not.
/// </para>
/// </summary>
public class SqsExceptionHandlerTests {

    private static IExecutionChain Chain() {
        return new TestExecutionChain(TestExecutionContext.Create(new MemoryStream(), new MemoryStream()));
    }

    [Fact]
    public async Task TheDefaultHandlerDeclinesTheExceptionSoTheMessageWouldBeRedelivered() {
        var handler = new SqsExceptionHandler(Substitute.For<ILogger<SqsExceptionHandler>>());

        var handled = await handler.HandleException(
            Chain(), new SQSEvent.SQSMessage { MessageId = "m1" }, new InvalidOperationException("boom"));

        Assert.False(handled);
    }

    [Fact]
    public async Task TheExceptionIsLoggedAtErrorLevel() {
        var logger = Substitute.For<ILogger<SqsExceptionHandler>>();
        var exception = new InvalidOperationException("boom");

        await new SqsExceptionHandler(logger).HandleException(
            Chain(), new SQSEvent.SQSMessage { MessageId = "m1" }, exception);

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            exception,
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }

    /// <summary>
    /// The default result is a cached <c>ValueTask</c> field. Two calls must still each complete —
    /// awaiting one <c>ValueTask</c> twice is undefined, and a shared instance handed to two
    /// concurrent records is the shape of that bug.
    /// </summary>
    [Fact]
    public async Task TheHandlerCanBeCalledMoreThanOnce() {
        var handler = new SqsExceptionHandler(Substitute.For<ILogger<SqsExceptionHandler>>());
        var message = new SQSEvent.SQSMessage { MessageId = "m1" };

        Assert.False(await handler.HandleException(Chain(), message, new Exception("first")));
        Assert.False(await handler.HandleException(Chain(), message, new Exception("second")));
    }
}
