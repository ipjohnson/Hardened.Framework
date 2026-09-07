using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Runtime.Tests.Support;
using Hardened.Requests.Testing;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

/// <summary>
/// How many times <see cref="BatchExecutionFilter"/> runs the rest of the chain, and what it does
/// with an item that failed.
///
/// <para>
/// Every one of these builds a real <c>ExecutionChain</c> through <c>Pipeline.Chain</c>, for the
/// reason <see cref="RetryFilterTests"/> records: a substituted <see cref="IExecutionChain"/> has a
/// re-runnable <c>Next</c> and the real one advances an index, so a fan-out tested against a
/// substitute would pass while running the handler once. The request is a double because the filter
/// is what is under test, not a transport.
/// </para>
/// </summary>
public class BatchExecutionFilterTests {

    /// <summary>
    /// A delivery of <c>n</c> items, each of which becomes a request carrying its own body.
    /// </summary>
    private sealed class Delivery : TestExecutionRequest, IBatchRequest {
        private readonly List<int> _failed = [];

        public Delivery(int count, bool reportsItemFailures = true)
            : base("QUEUE", "/orders", "application/json", new SimpleQueryStringCollection((IDictionary<string, string>?)null)) {
            Count = count;
            ReportsItemFailures = reportsItemFailures;
        }

        public int Count { get; }

        public bool ReportsItemFailures { get; }

        public IReadOnlyList<int> FailedItems => _failed;

        public void RecordFailure(int index, Exception failure) => _failed.Add(index);

        public IExecutionRequest ForItem(int index) =>
            new TestExecutionRequest(
                "QUEUE", "/orders", "application/json", new SimpleQueryStringCollection((IDictionary<string, string>?)null)) {
                Body = new MemoryStream(Encoding.UTF8.GetBytes($"item-{index}"))
            };
    }

    /// <summary>
    /// Fails the way the pipeline actually fails: the invoke filters catch what a handler raised
    /// and record it on the response rather than letting it propagate. A filter that only caught
    /// exceptions would see every failed item as handled and report nothing.
    /// </summary>
    private sealed class Handler : IExecutionFilter {
        private readonly Func<string, Exception?> _outcome;
        private readonly bool _throws;

        public readonly List<string> Saw = [];

        public Handler(Func<string, Exception?>? outcome = null, bool throws = false) {
            _outcome = outcome ?? (_ => null);
            _throws = throws;
        }

        public Task Execute(IExecutionChain chain) {
            var body = chain.Context.Request.Body;

            body.Position = 0;

            var text = new StreamReader(body).ReadToEnd();

            Saw.Add(text);

            var failure = _outcome(text);

            if (failure == null) {
                return Task.CompletedTask;
            }

            if (_throws) {
                throw failure;
            }

            chain.Context.Response.ExceptionValue = failure;

            return Task.CompletedTask;
        }
    }

    private static async Task<Handler> Run(
        IExecutionRequest request, Handler handler, params IExecutionFilter[] before) {
        var context = Pipeline.Context().Clone(request: request);

        var filters = before.Append(handler).Prepend(new BatchExecutionFilter()).ToArray();

        await Pipeline.Chain(context, filters).Next();

        return handler;
    }

    [Fact]
    public async Task EveryItemRunsTheRestOfTheChain() {
        var handler = await Run(new Delivery(3), new Handler());

        Assert.Equal(["item-0", "item-1", "item-2"], handler.Saw);
    }

    /// <summary>
    /// The filter is registered globally, so every request in an application meets it. One that is
    /// not a batch has to pass straight through.
    /// </summary>
    [Fact]
    public async Task ARequestThatIsNotABatchRunsTheChainOnce() {
        var request = new TestExecutionRequest(
            "GET", "/orders", "application/json", new SimpleQueryStringCollection((IDictionary<string, string>?)null)) {
            Body = new MemoryStream("plain"u8.ToArray())
        };

        var handler = await Run(request, new Handler());

        Assert.Equal(["plain"], handler.Saw);
    }

    /// <summary>
    /// Not an error. Nothing was delivered, so nothing is handled and nothing is reported.
    /// </summary>
    [Fact]
    public async Task AnEmptyDeliveryRunsTheChainOnceAndReportsNothing() {
        var delivery = new Delivery(0);

        await Run(delivery, new Handler());

        Assert.Empty(delivery.FailedItems);
    }

    /// <summary>
    /// The assertion the whole filter turns on. The handler records its failure on the response and
    /// returns normally, which is what the invoke filters do - a filter reading only exceptions
    /// would collect an empty list and SQS would mark every failed message handled.
    /// </summary>
    [Fact]
    public async Task AFailureRecordedOnTheResponseIsCollected() {
        var delivery = new Delivery(3);

        await Run(delivery, new Handler(text =>
            text == "item-1" ? new InvalidOperationException("no") : null));

        Assert.Equal([1], delivery.FailedItems);
    }

    [Fact]
    public async Task AFailureThrownIsCollected() {
        var delivery = new Delivery(3);

        await Run(delivery, new Handler(
            text => text == "item-2" ? new InvalidOperationException("no") : null, throws: true));

        Assert.Equal([2], delivery.FailedItems);
    }

    /// <summary>
    /// Stopping at the first failure would leave the rest unhandled and unreported, so the
    /// transport would treat them as delivered and drop them.
    /// </summary>
    [Fact]
    public async Task EveryItemIsAttemptedAfterOneFails() {
        var delivery = new Delivery(4);

        var handler = await Run(delivery, new Handler(text =>
            text == "item-0" ? new InvalidOperationException("no") : null));

        Assert.Equal(["item-0", "item-1", "item-2", "item-3"], handler.Saw);
        Assert.Equal([0], delivery.FailedItems);
    }

    /// <summary>
    /// A transport with no per-item report has to fail the invocation, because that is the only
    /// thing that makes it redeliver. Recording the failure and returning would lose the message.
    /// </summary>
    [Fact]
    public async Task AFailureIsRethrownWhenTheTransportCannotReportItemFailures() {
        var delivery = new Delivery(3, reportsItemFailures: false);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Run(delivery, new Handler(text =>
                text == "item-1" ? new InvalidOperationException("no") : null)));

        Assert.Equal("no", failure.Message);
        Assert.Empty(delivery.FailedItems);
    }

    /// <summary>
    /// One item's answer must not be readable as another's, which is why each fork gets its own
    /// response rather than sharing the delivery's.
    /// </summary>
    [Fact]
    public async Task AnItemDoesNotSeeTheFailureOfTheItemBeforeIt() {
        var seen = new List<Exception?>();

        var delivery = new Delivery(3);

        await Run(
            delivery,
            new Handler(text => text == "item-0" ? new InvalidOperationException("no") : null),
            new Pipeline.Inline(chain => {
                seen.Add(chain.Context.Response.ExceptionValue);

                return chain.Next();
            }));

        Assert.Equal([null, null, null], seen);
    }

    /// <summary>
    /// Every batched transport's module registers the filter, so an application handling both a
    /// queue and a topic gets two copies in the chain. The second sits inside the first one's forks,
    /// where the request is a single item rather than a batch, so it must pass through rather than
    /// fan out again.
    /// </summary>
    [Fact]
    public async Task ASecondCopyOfTheFilterDoesNotFanOutTwice() {
        var handler = await Run(new Delivery(2), new Handler(), new BatchExecutionFilter());

        Assert.Equal(["item-0", "item-1"], handler.Saw);
    }
}
