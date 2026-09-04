using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

/// <summary>
/// How many times <see cref="RetryFilter"/> runs the rest of the chain, and what it does with the
/// failure when it stops.
///
/// <para>
/// Every one of these builds a real <see cref="Hardened.Requests.Runtime.Execution.ExecutionChain"/>
/// through <c>Pipeline.Chain</c>. The previous version of this file substituted
/// <see cref="IExecutionChain"/>, whose <c>Next</c> is re-runnable; the real one advances an index
/// and is not. Seventeen tests passed against a chain that does not exist while the filter ran the
/// handler once and swallowed its exception, so the substitute is the one thing that must not come
/// back here.
/// </para>
/// </summary>
public class RetryFilterTests {

    private static RetryFilter Filter(
        int attempts, int baseDelay = 0, int budget = 0, bool allowNonIdempotent = true,
        Func<Exception, bool>? shouldRetry = null) =>
        new(attempts, baseDelay, budget, allowNonIdempotent, shouldRetry);

    /// <summary>
    /// Fails the way the pipeline actually fails: the invoke filters catch whatever the handler
    /// raised and record it on the response rather than letting it propagate.
    /// </summary>
    private sealed class FailsViaResponse : IExecutionFilter {
        private readonly int _failuresBeforeSuccess;
        private readonly Func<int, Exception> _exception;

        public int Attempts;

        public FailsViaResponse(int failuresBeforeSuccess, Func<int, Exception>? exception = null) {
            _failuresBeforeSuccess = failuresBeforeSuccess;
            _exception = exception ?? (n => new InvalidOperationException($"attempt {n}"));
        }

        public Task Execute(IExecutionChain chain) {
            Attempts++;

            if (Attempts <= _failuresBeforeSuccess) {
                chain.Context.Response.ExceptionValue = _exception(Attempts);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>Fails by throwing, which some filters still do.</summary>
    private sealed class FailsByThrowing : IExecutionFilter {
        private readonly int _failuresBeforeSuccess;

        public int Attempts;

        public FailsByThrowing(int failuresBeforeSuccess) {
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public Task Execute(IExecutionChain chain) {
            Attempts++;

            if (Attempts <= _failuresBeforeSuccess) {
                throw new InvalidOperationException($"attempt {Attempts}");
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A failure recorded on the response is a failure. This is the case the previous
    /// implementation could not see at all, and the reason it never retried anything.
    /// </summary>
    [Fact]
    public async Task Execute_RetriesAFailureRecordedOnTheResponse() {
        var downstream = new FailsViaResponse(failuresBeforeSuccess: 2);
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter(attempts: 3), downstream).Next();

        Assert.Equal(3, downstream.Attempts);
        Assert.Null(context.Response.ExceptionValue);
    }

    /// <summary>A thrown failure is retried on the same terms.</summary>
    [Fact]
    public async Task Execute_RetriesAThrownFailure() {
        var downstream = new FailsByThrowing(failuresBeforeSuccess: 2);
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter(attempts: 3), downstream).Next();

        Assert.Equal(3, downstream.Attempts);
        Assert.Null(context.Response.ExceptionValue);
    }

    /// <summary>A handler that works is run once.</summary>
    [Fact]
    public async Task Execute_RunsOnceWhenTheFirstAttemptSucceeds() {
        var downstream = new FailsViaResponse(failuresBeforeSuccess: 0);
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter(attempts: 3), downstream).Next();

        Assert.Equal(1, downstream.Attempts);
    }

    /// <summary>
    /// Exhausting the attempts leaves the last failure on the response rather than throwing it.
    /// The filter at <c>FilterOrder.Serialization</c> is what turns it into a body, and it reads it
    /// from there - a rethrow would go past it to the transport.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task Execute_LeavesTheFinalFailureOnTheResponseWhenAttemptsRunOut(int attempts) {
        var downstream = new FailsViaResponse(failuresBeforeSuccess: int.MaxValue);
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter(attempts), downstream).Next();

        Assert.Equal(attempts, downstream.Attempts);
        Assert.Equal($"attempt {attempts}", context.Response.ExceptionValue?.Message);
    }

    /// <summary>
    /// One attempt means one attempt, not none. The previous implementation ran the chain zero
    /// times for a count of zero and reported success, which is the one configuration that
    /// produced no response at all.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Execute_RunsExactlyOnceWhenNoRetriesAreConfigured(int attempts) {
        var downstream = new FailsViaResponse(failuresBeforeSuccess: int.MaxValue);
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter(attempts), downstream).Next();

        Assert.Equal(1, downstream.Attempts);
        Assert.NotNull(context.Response.ExceptionValue);
    }

    /// <summary>
    /// A client error is not retried. A 400 does not become a 200 by being asked again, and the
    /// caller would pay the whole backoff to be told what the first attempt already knew.
    /// </summary>
    [Theory]
    [InlineData(typeof(BadRequestException))]
    [InlineData(typeof(FormatException))]
    [InlineData(typeof(OperationCanceledException))]
    public async Task Execute_DoesNotRetryAClientError(Type exceptionType) {
        var downstream = new FailsViaResponse(
            failuresBeforeSuccess: int.MaxValue,
            exception: _ => exceptionType == typeof(BadRequestException)
                ? new BadRequestException("bad")
                : (Exception)Activator.CreateInstance(exceptionType)!);

        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter(attempts: 3), downstream).Next();

        Assert.Equal(1, downstream.Attempts);
    }

    /// <summary>
    /// An exception naming a 4xx is declined by status rather than by type, so an application's own
    /// exception is covered without deriving from anything in particular. A 5xx is retried.
    /// </summary>
    [Theory]
    [InlineData(404, 1)]
    [InlineData(409, 1)]
    [InlineData(503, 3)]
    public async Task Execute_DecidesOnStatusCodeExceptionsByTheirStatus(int status, int expected) {
        var downstream = new FailsViaResponse(
            failuresBeforeSuccess: int.MaxValue, exception: _ => new StatusCodeException(status));

        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter(attempts: 3), downstream).Next();

        Assert.Equal(expected, downstream.Attempts);
    }

    /// <summary>A supplied predicate replaces the default entirely.</summary>
    [Fact]
    public async Task Execute_UsesTheSuppliedPredicateInsteadOfTheDefault() {
        var downstream = new FailsViaResponse(
            failuresBeforeSuccess: int.MaxValue, exception: _ => new BadRequestException("bad"));

        var context = Pipeline.Context();

        await Pipeline.Chain(
            context, Filter(attempts: 3, shouldRetry: _ => true), downstream).Next();

        Assert.Equal(3, downstream.Attempts);
    }

    /// <summary>
    /// A non-idempotent verb is not retried unless the handler author said it may be. A retried
    /// POST is a second write, and nothing here can know whether that is acceptable.
    /// </summary>
    [Theory]
    [InlineData("POST", 1)]
    [InlineData("PATCH", 1)]
    [InlineData("GET", 3)]
    [InlineData("PUT", 3)]
    [InlineData("DELETE", 3)]
    [InlineData("HEAD", 3)]
    public async Task Execute_OnlyRetriesIdempotentVerbsByDefault(string method, int expected) {
        var downstream = new FailsViaResponse(failuresBeforeSuccess: int.MaxValue);
        var context = Pipeline.Context(method: method);

        await Pipeline.Chain(
            context,
            new RetryFilter(3, 0, 0, allowNonIdempotent: false),
            downstream).Next();

        Assert.Equal(expected, downstream.Attempts);
    }

    /// <summary>Opting in retries the write verbs too.</summary>
    [Fact]
    public async Task Execute_RetriesANonIdempotentVerbWhenExplicitlyAllowed() {
        var downstream = new FailsViaResponse(failuresBeforeSuccess: int.MaxValue);
        var context = Pipeline.Context(method: "POST");

        await Pipeline.Chain(
            context,
            new RetryFilter(3, 0, 0, allowNonIdempotent: true),
            downstream).Next();

        Assert.Equal(3, downstream.Attempts);
    }

    /// <summary>
    /// The failure from an attempt does not survive into the next one. A handler that succeeds on
    /// its second try must not report the first try's exception.
    /// </summary>
    [Fact]
    public async Task Execute_ClearsAFailedAttemptsExceptionBeforeTheNextAttempt() {
        var seen = new List<Exception?>();

        var downstream = new Pipeline.Inline(chain => {
            seen.Add(chain.Context.Response.ExceptionValue);

            if (seen.Count == 1) {
                chain.Context.Response.ExceptionValue = new InvalidOperationException("first");
            }

            return Task.CompletedTask;
        });

        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter(attempts: 3), downstream).Next();

        Assert.Equal(new Exception?[] { null, null }, seen);
        Assert.Null(context.Response.ExceptionValue);
    }

    /// <summary>
    /// A partial answer from a failed attempt is rolled back, so the successful attempt is not
    /// reporting the failed one's status. The previous implementation left both in place and its
    /// tests pinned that as intended.
    /// </summary>
    [Fact]
    public async Task Execute_RollsBackStatusAndResponseValueBetweenAttempts() {
        var observed = new List<(int? Status, object? Value)>();

        var downstream = new Pipeline.Inline(chain => {
            var response = chain.Context.Response;

            observed.Add((response.Status, response.ResponseValue));

            if (observed.Count == 1) {
                response.Status = 500;
                response.ResponseValue = "partial";
                response.ExceptionValue = new InvalidOperationException("first");
            }

            return Task.CompletedTask;
        });

        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter(attempts: 3), downstream).Next();

        Assert.Equal(2, observed.Count);
        Assert.Equal((null, null), observed[1]);
    }

    /// <summary>
    /// A seekable body is rewound between attempts, so a handler reading the stream directly sees
    /// the whole request rather than whatever the failed attempt left unread.
    /// </summary>
    [Fact]
    public async Task Execute_RewindsASeekableBodyBetweenAttempts() {
        var reads = new List<string>();
        var context = Pipeline.Context(body: "the whole body"u8.ToArray());

        var downstream = new Pipeline.Inline(async chain => {
            using var reader = new StreamReader(
                chain.Context.Request.Body, leaveOpen: true);

            reads.Add(await reader.ReadToEndAsync());

            if (reads.Count < 3) {
                chain.Context.Response.ExceptionValue = new InvalidOperationException("transient");
            }
        });

        await Pipeline.Chain(context, Filter(attempts: 3), downstream).Next();

        Assert.Equal(new[] { "the whole body", "the whole body", "the whole body" }, reads);
    }

    /// <summary>
    /// A response with bytes on the wire is not run again. A fork onto a body that already holds
    /// part of an answer would write a second answer after it, and nothing can take the first
    /// back - so a started response is treated like an exhausted budget, whatever the failure was.
    /// The guard is for a handler or a filter that writes to the body itself; a streaming handler
    /// never trips it, because its enumeration runs outside the fork.
    /// </summary>
    [Fact]
    public async Task Execute_DoesNotAttemptAgainOnceTheResponseHasStarted() {
        var attempts = 0;
        var context = Pipeline.Context();

        var downstream = new Pipeline.Inline(chain => {
            attempts++;

            chain.Context.Response.Body.WriteByte((byte)'x');
            chain.Context.Response.ExceptionValue =
                new InvalidOperationException("after the first byte");

            return Task.CompletedTask;
        });

        await Pipeline.Chain(context, Filter(attempts: 3), downstream).Next();

        Assert.Equal(1, attempts);
        Assert.Equal(1, context.Response.Body.Length);
        Assert.Equal("after the first byte", context.Response.ExceptionValue?.Message);
    }

    /// <summary>
    /// The budget stops the attempts even when the count has not been reached.
    /// </summary>
    [Fact]
    public async Task Execute_StopsAttemptingOnceTheTotalBudgetIsSpent() {
        var downstream = new FailsViaResponse(failuresBeforeSuccess: int.MaxValue);
        var context = Pipeline.Context();

        await Pipeline.Chain(
            context,
            Filter(attempts: 50, baseDelay: 20, budget: 60),
            downstream).Next();

        Assert.InRange(downstream.Attempts, 1, 20);
        Assert.NotNull(context.Response.ExceptionValue);
    }

    /// <summary>
    /// A cancelled request stops attempting rather than working through the remaining budget for a
    /// caller who is no longer there.
    /// </summary>
    [Fact]
    public async Task Execute_StopsAttemptingWhenTheRequestIsCancelled() {
        using var cancellation = new CancellationTokenSource();

        var context = Pipeline.Cancellable(cancellation.Token);
        var attempts = 0;

        var downstream = new Pipeline.Inline(chain => {
            attempts++;
            chain.Context.Response.ExceptionValue = new InvalidOperationException("transient");
            cancellation.Cancel();

            return Task.CompletedTask;
        });

        await Pipeline.Chain(context, Filter(attempts: 5), downstream).Next();

        Assert.Equal(1, attempts);
    }
}
