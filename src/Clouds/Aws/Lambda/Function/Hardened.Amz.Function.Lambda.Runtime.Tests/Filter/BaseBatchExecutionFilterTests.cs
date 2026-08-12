using System.Text;
using Hardened.Amz.Function.Lambda.Runtime.Filter;
using Hardened.Amz.Function.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Hardened.Amz.Function.Lambda.Runtime.Tests.Filter;

/// <summary>
/// <see cref="BaseBatchExecutionFilter{TEvent,TRecord}"/> is the engine underneath both the SQS
/// batch filter and the DynamoDB stream filter. Whether a record counts as a success is decided
/// here, once, and every partial-batch response in the repository is derived from that decision.
/// </summary>
public class BaseBatchExecutionFilterTests {

    /// <summary>
    /// The success flag each record carried out of the batch, keyed by record id — which is what a
    /// partial-batch response is built from. Asserting on the ids rather than the number of
    /// failures is the point: a filter that fails the wrong record produces a response of exactly
    /// the right length.
    /// </summary>
    private static async Task<IReadOnlyList<Result<TestRecord>>> RunBatch(
        IEnumerable<string> recordIds,
        Func<IExecutionContext, TestRecord, Task> perRecord,
        IBatchProcessorExceptionHandler? exceptionHandler = null) {

        var records = recordIds.Select(id => new TestRecord { Id = id, Payload = "payload-" + id }).ToList();

        using var requestBody = TestJson.ToStream(new TestEvent { Items = records });
        using var responseBody = new MemoryStream();

        var context = TestExecutionContext.Create(requestBody, responseBody);

        var filter = new TestBatchExecutionFilter(
            exceptionHandler ?? new BatchProcessorExceptionHandler());

        var chain = new TestExecutionChain(context, async forked => {
            var body = TestExecutionContext.ReadAll(forked.Request.Body);
            var record = TestJson.Serializer.Deserialize<TestRecord>(body);

            await perRecord(forked, record);
        });

        await filter.Execute(chain);

        return filter.Results!;
    }

    private static IReadOnlyList<string> FailedIds(IReadOnlyList<Result<TestRecord>> results) {
        return results.Where(r => !r.Success).Select(r => r.Value.Id).ToList();
    }

    [Fact]
    public async Task EveryRecordSucceedingProducesNoFailures() {
        var results = await RunBatch(new[] { "a", "b", "c" }, (_, _) => Task.CompletedTask);

        Assert.Equal(new[] { "a", "b", "c" }, results.Select(r => r.Value.Id));
        Assert.Empty(FailedIds(results));
    }

    [Fact]
    public async Task OnlyTheRecordThatThrewIsMarkedFailed() {
        var results = await RunBatch(new[] { "a", "b", "c" }, (_, record) => {
            if (record.Id == "b") {
                throw new InvalidOperationException("poison");
            }

            return Task.CompletedTask;
        });

        Assert.Equal("b", Assert.Single(FailedIds(results)));
    }

    [Fact]
    public async Task EveryRecordFailingMarksEveryRecord() {
        var results = await RunBatch(new[] { "a", "b", "c" },
            (_, _) => throw new InvalidOperationException("poison"));

        Assert.Equal(new[] { "a", "b", "c" }, FailedIds(results));
    }

    /// <summary>
    /// The forked chain's own response status decides the record's fate. Reading the status off the
    /// outer chain instead would give every record the outcome of whichever ran last.
    /// </summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData(200, true)]
    [InlineData(299, true)]
    [InlineData(300, false)]
    [InlineData(404, false)]
    [InlineData(500, false)]
    public async Task ResponseStatusBelowThreeHundredIsASuccess(int? status, bool expectedSuccess) {
        var results = await RunBatch(new[] { "only" }, (forked, _) => {
            forked.Response.Status = status;

            return Task.CompletedTask;
        });

        Assert.Equal(expectedSuccess, Assert.Single(results).Success);
    }

    [Fact]
    public async Task AFailingStatusOnOneRecordDoesNotLeakOntoTheNext() {
        var results = await RunBatch(new[] { "a", "b", "c" }, (forked, record) => {
            if (record.Id == "a") {
                forked.Response.Status = 500;
            }

            return Task.CompletedTask;
        });

        Assert.Equal("a", Assert.Single(FailedIds(results)));
    }

    /// <summary>
    /// The record body is written into a fresh stream per record and the handler reads it back.
    /// The batch engine hands the subclass the stream; nothing else does.
    /// </summary>
    [Fact]
    public async Task EachRecordReachesTheHandlerWithItsOwnBody() {
        var seen = new List<string>();

        await RunBatch(new[] { "a", "b" }, (_, record) => {
            seen.Add(record.Payload);

            return Task.CompletedTask;
        });

        Assert.Equal(new[] { "payload-a", "payload-b" }, seen);
    }

    [Fact]
    public async Task AnEmptyBatchProducesNoResults() {
        var results = await RunBatch(Array.Empty<string>(), (_, _) => Task.CompletedTask);

        Assert.Empty(results);
    }

    /// <summary>
    /// An exception handler returning <c>true</c> claims the record — it does not become a batch
    /// failure, so SQS deletes the message. Returning <c>false</c> leaves it for redelivery.
    /// </summary>
    [Fact]
    public async Task AnExceptionHandlerReturningTrueSwallowsTheFailure() {
        var handler = Substitute.For<IBatchProcessorExceptionHandler>();
        handler.HandleException(Arg.Any<IExecutionContext>(), Arg.Any<ILogger>(), Arg.Any<Exception>())
            .Returns(Task.FromResult(true));

        var results = await RunBatch(new[] { "a", "b" },
            (_, _) => throw new InvalidOperationException("poison"),
            handler);

        Assert.Empty(FailedIds(results));
    }

    [Fact]
    public async Task AnExceptionHandlerReturningFalseKeepsTheFailure() {
        var handler = Substitute.For<IBatchProcessorExceptionHandler>();
        handler.HandleException(Arg.Any<IExecutionContext>(), Arg.Any<ILogger>(), Arg.Any<Exception>())
            .Returns(Task.FromResult(false));

        var results = await RunBatch(new[] { "a", "b" }, (_, record) => {
            if (record.Id == "b") {
                throw new InvalidOperationException("poison");
            }

            return Task.CompletedTask;
        }, handler);

        Assert.Equal("b", Assert.Single(FailedIds(results)));
    }

    /// <summary>
    /// The handler is given the thrown exception, not a wrapper. A handler that routes by exception
    /// type cannot work otherwise.
    /// </summary>
    [Fact]
    public async Task TheExceptionHandlerSeesTheOriginalException() {
        var thrown = new InvalidOperationException("poison");
        var handler = Substitute.For<IBatchProcessorExceptionHandler>();
        handler.HandleException(Arg.Any<IExecutionContext>(), Arg.Any<ILogger>(), Arg.Any<Exception>())
            .Returns(Task.FromResult(false));

        await RunBatch(new[] { "a" }, (_, _) => throw thrown, handler);

        await handler.Received(1).HandleException(Arg.Any<IExecutionContext>(), Arg.Any<ILogger>(), thrown);
    }

    /// <summary>
    /// The forked context carries a request and response of its own. Sharing the outer response
    /// would mean the batch's own output stream collected every record's body.
    /// </summary>
    [Fact]
    public async Task EachRecordRunsAgainstItsOwnRequestAndResponse() {
        using var requestBody = TestJson.ToStream(new TestEvent {
            Items = [new TestRecord { Id = "a", Payload = "one" }, new TestRecord { Id = "b", Payload = "two" }]
        });
        using var responseBody = new MemoryStream();

        var context = TestExecutionContext.Create(requestBody, responseBody);
        var filter = new TestBatchExecutionFilter(new BatchProcessorExceptionHandler());

        var requests = new List<IExecutionRequest>();
        var responses = new List<IExecutionResponse>();

        var chain = new TestExecutionChain(context, forked => {
            requests.Add(forked.Request);
            responses.Add(forked.Response);

            return Task.CompletedTask;
        });

        await filter.Execute(chain);

        Assert.Equal(2, requests.Distinct().Count());
        Assert.Equal(2, responses.Distinct().Count());
        Assert.DoesNotContain(context.Response, responses);
    }

    /// <summary>
    /// The batch response is written to the outer context's response body, which is the stream the
    /// Lambda runtime returns to AWS.
    /// </summary>
    [Fact]
    public async Task TheBatchResultIsWrittenToTheOuterResponseBody() {
        using var requestBody = TestJson.ToStream(new TestEvent {
            Items = [new TestRecord { Id = "a", Payload = "one" }]
        });
        using var responseBody = new MemoryStream();

        var context = TestExecutionContext.Create(requestBody, responseBody);
        var filter = new TestBatchExecutionFilter(new BatchProcessorExceptionHandler());

        await filter.Execute(new TestExecutionChain(context));

        Assert.Contains("\"a\"", TestExecutionContext.ReadAll(responseBody));
    }

    public class TestEvent {
        public List<TestRecord> Items { get; set; } = [];
    }

    public class TestRecord {
        public string Id { get; set; } = "";

        public string Payload { get; set; } = "";
    }

    /// <summary>
    /// The smallest possible subclass: it only tells the engine how to read records out of an event
    /// and how to write a record into a stream, which is all the engine asks of SQS and DynamoDB.
    /// </summary>
    private sealed class TestBatchExecutionFilter : BaseBatchExecutionFilter<TestEvent, TestRecord> {
        public TestBatchExecutionFilter(IBatchProcessorExceptionHandler exceptionHandler)
            : base(TestJson.Serializer, TestJson.Pool, exceptionHandler, NullLogger.Instance) { }

        public IReadOnlyList<Result<TestRecord>>? Results { get; private set; }

        protected override Task WriteRequestRecord(IExecutionChain chain, TestRecord record, Stream inputStream) {
            var bytes = Encoding.UTF8.GetBytes(TestJson.Serializer.Serialize(record));

            inputStream.Write(bytes, 0, bytes.Length);
            inputStream.Position = 0;

            return Task.CompletedTask;
        }

        protected override async Task WriteResultsAsync(
            IExecutionContext context, IReadOnlyList<Result<TestRecord>> records) {
            Results = records;

            await JsonSerializer.SerializeAsync(
                context.Response.Body, records.Select(r => r.Value.Id).ToList());
        }

        protected override IEnumerable<TestRecord> ReadRecords(TestEvent tEvent) {
            return tEvent.Items;
        }
    }
}
