using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Hardened.Shared.Runtime.Collections;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

/// <summary>
/// What <c>[Retry]</c> does to the request and the response between attempts.
///
/// <para>
/// The retry filter runs ahead of parameter binding, so a second attempt reads the body
/// again. A request body is usually a forward-only network stream that cannot be rewound,
/// which is why the filter copies it into a pooled buffer up front and hands the buffer to
/// each attempt. The buffer comes from <see cref="IMemoryStreamPool"/> and goes back to it;
/// leaking one leaks a buffer per retried request.
/// </para>
///
/// <para>
/// The attempt-counting behaviour is covered by <see cref="RetryFilterTests"/>; these are the
/// state-between-attempts cases.
/// </para>
/// </summary>
public class RetryFilterReplayTests {

    private static IExecutionChain ChainOver(IExecutionContext context, Func<Task> next) {
        var chain = Substitute.For<IExecutionChain>();

        chain.Context.Returns(context);
        chain.Next().Returns(_ => next());

        return chain;
    }

    /// <summary>
    /// The replay buffer is taken from the pool and returned to it, whether or not the request
    /// eventually succeeded.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheReplayBufferIsReturnedToThePool(bool eventuallySucceeds) {
        var reservation = Substitute.For<IPoolItemReservation<MemoryStream>>();
        reservation.Item.Returns(new MemoryStream());

        var pool = Substitute.For<IMemoryStreamPool>();
        pool.Get().Returns(reservation);

        var context = Pipeline.Context(body: "payload"u8.ToArray());
        var attempts = 0;

        var chain = ChainOver(context, () => {
            attempts++;

            if (!eventuallySucceeds || attempts < 2) {
                throw new InvalidOperationException("transient");
            }

            return Task.CompletedTask;
        });

        var filter = new RetryFilter(pool, retryCount: 2, retrySleepTime: 0);

        if (eventuallySucceeds) {
            await filter.Execute(chain);
        }
        else {
            await Assert.ThrowsAsync<InvalidOperationException>(() => filter.Execute(chain));
        }

        pool.Received(1).Get();
        reservation.Received(1).Dispose();
    }

    /// <summary>
    /// The request body every attempt sees is the pooled replay buffer, not the original
    /// stream - a forward-only transport stream would be empty by the second attempt.
    /// </summary>
    [Fact]
    public async Task EveryAttemptReadsTheReplayBufferRatherThanTheOriginalStream() {
        var original = new MemoryStream("payload"u8.ToArray());

        var context = Pipeline.Context();
        context.Request.Body = original;

        var bodies = new List<Stream>();
        var attempts = 0;

        var chain = ChainOver(context, () => {
            bodies.Add(context.Request.Body);

            if (++attempts < 3) {
                throw new InvalidOperationException("transient");
            }

            return Task.CompletedTask;
        });

        await new RetryFilter(new MemoryStreamPool(), 3, 0).Execute(chain);

        Assert.Equal(3, bodies.Count);
        Assert.DoesNotContain(original, bodies);
        Assert.Single(bodies.Distinct());
    }

    /// <summary>
    /// The replay buffer is rewound before each attempt, so the second attempt reads the whole
    /// body rather than whatever the first attempt left unread.
    /// </summary>
    [Fact]
    public async Task TheReplayBufferIsRewoundBeforeEachAttempt() {
        var context = Pipeline.Context(body: "the whole body"u8.ToArray());

        var reads = new List<string>();
        var attempts = 0;

        var chain = ChainOver(context, async () => {
            using var reader = new StreamReader(
                context.Request.Body, Encoding.UTF8, false, 1024, leaveOpen: true);

            reads.Add(await reader.ReadToEndAsync());

            if (++attempts < 3) {
                throw new InvalidOperationException("transient");
            }
        });

        await new RetryFilter(new MemoryStreamPool(), 3, 0).Execute(chain);

        Assert.Equal(new[] { "the whole body", "the whole body", "the whole body" }, reads);
    }

    /// <summary>
    /// An empty body replays as an empty body rather than throwing on the copy.
    /// </summary>
    [Fact]
    public async Task AnEmptyRequestBodyReplaysAsAnEmptyBody() {
        var context = Pipeline.Context();
        var lengths = new List<long>();
        var attempts = 0;

        var chain = ChainOver(context, () => {
            lengths.Add(context.Request.Body.Length);

            if (++attempts < 2) {
                throw new InvalidOperationException("transient");
            }

            return Task.CompletedTask;
        });

        await new RetryFilter(new MemoryStreamPool(), 2, 0).Execute(chain);

        Assert.Equal(new long[] { 0, 0 }, lengths);
    }

    /// <summary>
    /// The filter does not reset the response between attempts, so a handler that wrote part
    /// of a body and then failed leaves those bytes in place and the retry appends to them.
    ///
    /// <para>
    /// Pinned as the current behaviour rather than endorsed: a retry that produces
    /// <c>{"partial"{"complete"}</c> is not a good response. A handler on a retried route must
    /// not write the body until it can write all of it, and this test is what will notice if
    /// that stops being the case.
    /// </para>
    /// </summary>
    [Fact]
    public async Task APartiallyWrittenResponseIsNotClearedBeforeTheRetry() {
        var context = Pipeline.Context();
        var attempts = 0;

        var chain = ChainOver(context, async () => {
            attempts++;

            var partial = Encoding.UTF8.GetBytes($"attempt-{attempts}:");

            await context.Response.Body.WriteAsync(partial);

            if (attempts < 2) {
                throw new InvalidOperationException("failed after writing");
            }
        });

        await new RetryFilter(new MemoryStreamPool(), 3, 0).Execute(chain);

        Assert.Equal("attempt-1:attempt-2:",
            Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }

    /// <summary>
    /// State the failed attempt left on the response - a status, an exception - survives into
    /// the next attempt for the same reason. A retried handler that succeeds without clearing
    /// the exception still reports the failure.
    /// </summary>
    [Fact]
    public async Task AnExceptionRecordedByAFailedAttemptSurvivesIntoTheSuccessfulOne() {
        var context = Pipeline.Context();
        var attempts = 0;

        var chain = ChainOver(context, () => {
            if (++attempts < 2) {
                context.Response.Status = 500;
                context.Response.ExceptionValue = new InvalidOperationException("first attempt");

                throw new InvalidOperationException("transient");
            }

            return Task.CompletedTask;
        });

        await new RetryFilter(new MemoryStreamPool(), 3, 0).Execute(chain);

        Assert.Equal(500, context.Response.Status);
        Assert.NotNull(context.Response.ExceptionValue);
    }

    /// <summary>
    /// A retry count of zero runs nothing at all - not one attempt - and reports no failure.
    /// Worth pinning because it is the one configuration where a request silently produces no
    /// response rather than an error.
    /// </summary>
    [Fact]
    public async Task ARetryCountOfZeroRunsNoAttemptsAndThrowsNothing() {
        var context = Pipeline.Context();
        var attempts = 0;

        var chain = ChainOver(context, () => {
            attempts++;

            return Task.CompletedTask;
        });

        await new RetryFilter(new MemoryStreamPool(), 0, 0).Execute(chain);

        Assert.Equal(0, attempts);
    }

    /// <summary>
    /// The exception the caller sees is the last attempt's, not the first's - the useful one
    /// when a transient failure has become a permanent one.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task ExhaustingTheRetriesThrowsTheFinalAttemptsException(int retries) {
        var context = Pipeline.Context();
        var attempts = 0;

        var chain = ChainOver(context, () => throw new InvalidOperationException($"attempt {++attempts}"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new RetryFilter(new MemoryStreamPool(), retries, 0).Execute(chain));

        Assert.Equal(retries, attempts);
        Assert.Equal($"attempt {retries}", exception.Message);
    }
}
