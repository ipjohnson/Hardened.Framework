using Hardened.Amz.Function.Lambda.Runtime.Filter;
using Hardened.Amz.Function.Sqs.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;
using NSubstitute;
using static Hardened.Amz.Function.Sqs.Runtime.Tests.Infrastructure.SqsBatchHarness;

namespace Hardened.Amz.Function.Sqs.Runtime.Tests.Impl;

/// <summary>
/// Partial batch responses. When a Lambda reports <c>ReportBatchItemFailures</c>, SQS deletes every
/// message the response did not name and redelivers the rest — so the failure list is not a count,
/// it is the exact set of messages that must come back.
///
/// <para>
/// Naming the wrong id is worse than naming none: the poison message is deleted and every message
/// that succeeded is redelivered. That is why every assertion here is on an identifier.
/// </para>
/// </summary>
public class SqsPartialBatchResponseTests {

    /// <summary>
    /// Ids that are not the message's position in the batch. A filter reporting the index instead
    /// of the id produces a response of exactly the right length, with every identifier wrong.
    /// </summary>
    private const string First = "a7f31c";
    private const string Second = "b2c948";
    private const string Third = "c9d015";

    private static IEnumerable<Amazon.Lambda.SQSEvents.SQSEvent.SQSMessage> ThreeMessages() {
        return [
            Message(First, "{\"value\":1}"),
            Message(Second, "{\"value\":2}"),
            Message(Third, "{\"value\":3}")
        ];
    }

    [Fact]
    public async Task ABatchWhereEveryMessageSucceedsNamesNoFailures() {
        var response = await Run(ThreeMessages());

        Assert.Empty(response.BatchItemFailures);
    }

    /// <summary>
    /// The one test this whole project exists for: one poison message in a batch of three, and only
    /// that message's id comes back.
    /// </summary>
    [Fact]
    public async Task ASingleFailingMessageNamesOnlyItsOwnMessageId() {
        var response = await Run(ThreeMessages(), (_, body) => {
            if (body.Contains("\"value\":2")) {
                throw new InvalidOperationException("poison");
            }

            return Task.CompletedTask;
        });

        Assert.Equal(Second, Assert.Single(response.BatchItemFailures).ItemIdentifier);
    }

    [Fact]
    public async Task ABatchWhereEveryMessageFailsNamesEveryMessageId() {
        var response = await Run(ThreeMessages(), (_, _) => throw new InvalidOperationException("poison"));

        Assert.Equal(new[] { First, Second, Third }, FailedIds(response));
    }

    /// <summary>
    /// The identifier is the message id the event carried, not the position in the batch. Message
    /// ids are opaque GUIDs in production; a batch whose ids happen to run 0, 1, 2 would hide an
    /// index-for-id substitution entirely.
    /// </summary>
    [Fact]
    public async Task TheIdentifierIsTheMessageIdAndNotThePositionInTheBatch() {
        var response = await Run(ThreeMessages(), (_, body) => {
            if (body.Contains("\"value\":3")) {
                throw new InvalidOperationException("poison");
            }

            return Task.CompletedTask;
        });

        var identifier = Assert.Single(response.BatchItemFailures).ItemIdentifier;

        Assert.Equal(Third, identifier);
        Assert.NotEqual("2", identifier);
    }

    [Fact]
    public async Task TwoFailuresAmongThreeNameBothAndOmitTheOneThatSucceeded() {
        var response = await Run(ThreeMessages(), (_, body) => {
            if (!body.Contains("\"value\":2")) {
                throw new InvalidOperationException("poison");
            }

            return Task.CompletedTask;
        });

        Assert.Equal(new[] { First, Third }, FailedIds(response));
        Assert.DoesNotContain(Second, FailedIds(response));
    }

    /// <summary>
    /// A handler that sets an error status rather than throwing fails its message just the same.
    /// Both are how a Hardened handler reports failure, and both have to reach the batch response.
    /// </summary>
    [Theory]
    [InlineData(300)]
    [InlineData(400)]
    [InlineData(500)]
    public async Task AMessageWhoseHandlerSetAnErrorStatusIsNamedInTheResponse(int status) {
        var response = await Run(ThreeMessages(), (forked, body) => {
            if (body.Contains("\"value\":2")) {
                forked.Response.Status = status;
            }

            return Task.CompletedTask;
        });

        Assert.Equal(Second, Assert.Single(response.BatchItemFailures).ItemIdentifier);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(204)]
    [InlineData(299)]
    public async Task AMessageWhoseHandlerSetASuccessStatusIsNotNamed(int status) {
        var response = await Run(ThreeMessages(), (forked, _) => {
            forked.Response.Status = status;

            return Task.CompletedTask;
        });

        Assert.Empty(response.BatchItemFailures);
    }

    /// <summary>
    /// <see cref="ISqsExceptionHandler"/> is declared and registered but never consulted — see the
    /// report on <c>SqsExceptionHandlerTests</c>. The handler the SQS filter actually asks is
    /// <see cref="IBatchProcessorExceptionHandler"/>, and returning true from it claims the message:
    /// SQS deletes it rather than redelivering.
    /// </summary>
    [Fact]
    public async Task AnExceptionHandlerThatClaimsTheFailureKeepsTheMessageOutOfTheResponse() {
        var handler = Substitute.For<IBatchProcessorExceptionHandler>();
        handler.HandleException(Arg.Any<IExecutionContext>(), Arg.Any<ILogger>(), Arg.Any<Exception>())
            .Returns(Task.FromResult(true));

        var response = await Run(
            ThreeMessages(),
            (_, _) => throw new InvalidOperationException("poison"),
            handler);

        Assert.Empty(response.BatchItemFailures);
    }

    [Fact]
    public async Task AnExceptionHandlerThatDeclinesLeavesTheMessageNamedForRedelivery() {
        var handler = Substitute.For<IBatchProcessorExceptionHandler>();
        handler.HandleException(Arg.Any<IExecutionContext>(), Arg.Any<ILogger>(), Arg.Any<Exception>())
            .Returns(Task.FromResult(false));

        var response = await Run(ThreeMessages(), (_, body) => {
            if (body.Contains("\"value\":1")) {
                throw new InvalidOperationException("poison");
            }

            return Task.CompletedTask;
        }, handler);

        Assert.Equal(First, Assert.Single(response.BatchItemFailures).ItemIdentifier);
    }

    [Fact]
    public async Task AnEmptyBatchProducesAnEmptyFailureListRatherThanNull() {
        var response = await Run([]);

        Assert.NotNull(response.BatchItemFailures);
        Assert.Empty(response.BatchItemFailures);
    }

    [Fact]
    public async Task ASingleMessageBatchThatFailsNamesThatMessage() {
        var response = await Run(
            [Message("only-one", "{}")],
            (_, _) => throw new InvalidOperationException("poison"));

        Assert.Equal("only-one", Assert.Single(response.BatchItemFailures).ItemIdentifier);
    }

    /// <summary>
    /// Failures come back in the order the messages arrived. SQS does not require it, but a
    /// response ordered by anything else is a sign the results were collected from somewhere other
    /// than the per-record loop.
    /// </summary>
    [Fact]
    public async Task FailuresAreNamedInTheOrderTheMessagesArrived() {
        var response = await Run(ThreeMessages(), (_, body) => {
            if (!body.Contains("\"value\":2")) {
                throw new InvalidOperationException("poison");
            }

            return Task.CompletedTask;
        });

        Assert.Equal(new[] { First, Third }, FailedIds(response));
    }
}
