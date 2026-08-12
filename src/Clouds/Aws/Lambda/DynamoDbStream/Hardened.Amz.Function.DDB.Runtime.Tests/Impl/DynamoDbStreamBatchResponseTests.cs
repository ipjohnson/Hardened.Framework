using Amazon.Lambda.DynamoDBEvents;
using Hardened.Amz.Function.DDB.Runtime.Tests.Infrastructure;
using Hardened.Amz.Function.Lambda.Runtime.Filter;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;
using NSubstitute;
using static Hardened.Amz.Function.DDB.Runtime.Tests.Infrastructure.DdbStreamHarness;

namespace Hardened.Amz.Function.DDB.Runtime.Tests.Impl;

/// <summary>
/// Failure identification for a DynamoDB stream. A stream shard is ordered: naming a record in
/// <see cref="StreamsEventResponse.BatchItemFailures"/> tells Lambda to retry the shard from that
/// record, so the wrong identifier either replays records that already succeeded or skips the one
/// that failed.
/// </summary>
public class DynamoDbStreamBatchResponseTests {

    /// <summary>
    /// Event ids that are not the record's position in the batch — a filter reporting the index
    /// produces a response of exactly the right length with every identifier wrong.
    /// </summary>
    private const string First = "4b1f2e";
    private const string Second = "8c0a77";
    private const string Third = "d3e991";

    private static IEnumerable<DynamoDBEvent.DynamodbStreamRecord> ThreeRecords() {
        return [
            Record(First, Insert, newImage: Image(("id", "1"))),
            Record(Second, Modify, newImage: Image(("id", "2")), oldImage: Image(("id", "2"))),
            Record(Third, Remove, oldImage: Image(("id", "3")))
        ];
    }

    /// <summary>
    /// No failures means the response carries no failure list at all, rather than an empty one.
    /// Lambda treats a null and an empty <c>batchItemFailures</c> alike — both are a complete
    /// success — so this is a deliberate difference from the SQS filter, which always writes a
    /// list. Pinned because a change either way is a change to what AWS is told.
    /// </summary>
    [Fact]
    public async Task AStreamWhereEveryRecordSucceedsReportsNoFailureList() {
        var response = await Run(ThreeRecords());

        Assert.Null(response.BatchItemFailures);
    }

    [Fact]
    public async Task ASingleFailingRecordNamesOnlyItsOwnEventId() {
        var response = await Run(ThreeRecords(), (_, recordContext) => {
            if (recordContext.CurrentRecord.EventID == Second) {
                throw new InvalidOperationException("handler failed");
            }

            return Task.CompletedTask;
        });

        Assert.Equal(Second, Assert.Single(response.BatchItemFailures).ItemIdentifier);
    }

    [Fact]
    public async Task EveryRecordFailingNamesEveryEventId() {
        var response = await Run(ThreeRecords(), (_, _) => throw new InvalidOperationException("handler failed"));

        Assert.Equal(new[] { First, Second, Third }, FailedIds(response));
    }

    /// <summary>
    /// The identifier is the record's <c>EventID</c>, not its position in the batch. Real event ids
    /// are opaque hex strings; a batch whose ids happened to run 0, 1, 2 would hide an
    /// index-for-id substitution entirely.
    /// </summary>
    [Fact]
    public async Task TheIdentifierIsTheEventIdAndNotThePositionInTheStream() {
        var response = await Run(ThreeRecords(), (_, recordContext) => {
            if (recordContext.CurrentRecord.EventID == Third) {
                throw new InvalidOperationException("handler failed");
            }

            return Task.CompletedTask;
        });

        var identifier = Assert.Single(response.BatchItemFailures).ItemIdentifier;

        Assert.Equal(Third, identifier);
        Assert.NotEqual("2", identifier);
    }

    [Fact]
    public async Task TwoFailuresAmongThreeNameBothAndOmitTheOneThatSucceeded() {
        var response = await Run(ThreeRecords(), (_, recordContext) => {
            if (recordContext.CurrentRecord.EventID != Second) {
                throw new InvalidOperationException("handler failed");
            }

            return Task.CompletedTask;
        });

        Assert.Equal(new[] { First, Third }, FailedIds(response));
    }

    /// <summary>
    /// Every stream event type reaches the handler. A filter that only forwarded MODIFY would
    /// silently drop every creation and deletion while reporting the batch a complete success.
    /// </summary>
    [Theory]
    [InlineData(Insert)]
    [InlineData(Modify)]
    [InlineData(Remove)]
    public async Task EveryStreamEventTypeReachesTheHandler(string eventName) {
        var seen = new List<string>();

        var response = await Run(
            [Record("e1", eventName, newImage: Image(("id", "1")), oldImage: Image(("id", "1")))],
            (_, recordContext) => {
                seen.Add(recordContext.CurrentRecord.EventName);

                return Task.CompletedTask;
            });

        Assert.Equal(eventName, Assert.Single(seen));
        Assert.Null(response.BatchItemFailures);
    }

    [Theory]
    [InlineData(Insert)]
    [InlineData(Modify)]
    [InlineData(Remove)]
    public async Task AFailureIsNamedWhateverTheStreamEventTypeWas(string eventName) {
        var response = await Run(
            [Record("evt-" + eventName, eventName)],
            (_, _) => throw new InvalidOperationException("handler failed"));

        Assert.Equal("evt-" + eventName, Assert.Single(response.BatchItemFailures).ItemIdentifier);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task ARecordWhoseHandlerSetAnErrorStatusIsNamedInTheResponse(int status) {
        var response = await Run(ThreeRecords(), (forked, recordContext) => {
            if (recordContext.CurrentRecord.EventID == First) {
                forked.Response.Status = status;
            }

            return Task.CompletedTask;
        });

        Assert.Equal(First, Assert.Single(response.BatchItemFailures).ItemIdentifier);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(204)]
    [InlineData(299)]
    public async Task ARecordWhoseHandlerSetASuccessStatusIsNotNamed(int status) {
        var response = await Run(ThreeRecords(), (forked, _) => {
            forked.Response.Status = status;

            return Task.CompletedTask;
        });

        Assert.Null(response.BatchItemFailures);
    }

    [Fact]
    public async Task AnExceptionHandlerThatClaimsTheFailureLeavesTheStreamReportedClean() {
        var handler = Substitute.For<IBatchProcessorExceptionHandler>();
        handler.HandleException(Arg.Any<IExecutionContext>(), Arg.Any<ILogger>(), Arg.Any<Exception>())
            .Returns(Task.FromResult(true));

        var response = await Run(
            ThreeRecords(),
            (_, _) => throw new InvalidOperationException("handler failed"),
            handler);

        Assert.Null(response.BatchItemFailures);
    }

    [Fact]
    public async Task AnExceptionHandlerThatDeclinesLeavesTheRecordNamedForRetry() {
        var handler = Substitute.For<IBatchProcessorExceptionHandler>();
        handler.HandleException(Arg.Any<IExecutionContext>(), Arg.Any<ILogger>(), Arg.Any<Exception>())
            .Returns(Task.FromResult(false));

        var response = await Run(ThreeRecords(), (_, recordContext) => {
            if (recordContext.CurrentRecord.EventID == First) {
                throw new InvalidOperationException("handler failed");
            }

            return Task.CompletedTask;
        }, handler);

        Assert.Equal(First, Assert.Single(response.BatchItemFailures).ItemIdentifier);
    }

    /// <summary>
    /// Binding failures are ordinary failures. A REMOVE record reaching a handler that asks for
    /// <c>[NewImage]</c> throws <see cref="InvalidCastException"/>, and that record — and only that
    /// record — is named.
    /// </summary>
    [Fact]
    public async Task ARecordThatFailsToBindIsNamedLikeAnyOtherFailure() {
        var response = await Run(ThreeRecords(), (_, recordContext) => {
            if (recordContext.CurrentRecord.Dynamodb.NewImage == null) {
                throw new InvalidCastException("NewImage can only be used on Dictionary<string,AttributeValue>.");
            }

            return Task.CompletedTask;
        });

        Assert.Equal(Third, Assert.Single(response.BatchItemFailures).ItemIdentifier);
    }

    [Fact]
    public async Task AnEmptyStreamReportsNoFailures() {
        var response = await Run(Array.Empty<DynamoDBEvent.DynamodbStreamRecord>());

        Assert.Null(response.BatchItemFailures);
    }

    [Fact]
    public async Task ASingleRecordStreamThatFailsNamesThatRecord() {
        var response = await Run(
            [Record("only-one", Insert, newImage: Image(("id", "1")))],
            (_, _) => throw new InvalidOperationException("handler failed"));

        Assert.Equal("only-one", Assert.Single(response.BatchItemFailures).ItemIdentifier);
    }

    [Fact]
    public async Task FailuresAreNamedInStreamOrder() {
        var response = await Run(ThreeRecords(), (_, recordContext) => {
            if (recordContext.CurrentRecord.EventID != Second) {
                throw new InvalidOperationException("handler failed");
            }

            return Task.CompletedTask;
        });

        Assert.Equal(new[] { First, Third }, FailedIds(response));
    }
}
