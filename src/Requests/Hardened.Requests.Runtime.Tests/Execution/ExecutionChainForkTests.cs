using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Runtime.Tests.Support;
using Hardened.Requests.Testing;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Execution;

/// <summary>
/// <see cref="IExecutionChain.Fork"/> is how one request runs a handler more than once -
/// a template that renders a partial, a middleware that retries against a rewritten path,
/// a batch adapter that fans one invocation out over many messages.
///
/// <para>
/// A fork copies the chain's position, not its progress: the filters that have already run
/// stay run, and everything from the current position onward runs again against whichever
/// context the caller supplies. The two chains must not interfere.
/// </para>
/// </summary>
public class ExecutionChainForkTests {

    /// <summary>
    /// The fork picks up where the forking filter is, so filters that already ran are not
    /// replayed. Re-running them would double any side effect they had - a metrics filter
    /// would record the request twice.
    /// </summary>
    [Fact]
    public async Task AForkResumesAtTheForkingFiltersPositionRatherThanTheStart() {
        var log = new List<string>();
        var context = Pipeline.Context();

        var chain = Pipeline.Chain(context,
            new Pipeline.Recording(log, "first"),
            new Pipeline.Inline(async c => {
                log.Add("forking");
                await c.Fork(c.Context).Next();
                await c.Next();
            }),
            new Pipeline.Recording(log, "downstream"));

        await chain.Next();

        Assert.Equal(new[] { "first", "forking", "downstream", "downstream" }, log);
    }

    /// <summary>
    /// The handler runs once per fork. This is the behaviour every re-invocation depends on.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public async Task EachForkRunsTheRemainderOfTheChainAgain(int forks) {
        var runs = 0;
        var context = Pipeline.Context();

        var chain = Pipeline.Chain(context,
            new Pipeline.Inline(async c => {
                for (var i = 0; i < forks; i++) {
                    await c.Fork(c.Context).Next();
                }
            }),
            new Pipeline.Inline(_ => {
                runs++;

                return Task.CompletedTask;
            }));

        await chain.Next();

        Assert.Equal(forks, runs);
    }

    /// <summary>
    /// Advancing a fork must not advance the chain it came from - otherwise a filter that
    /// forked and then called <c>Next</c> would skip its own successor.
    /// </summary>
    [Fact]
    public async Task AdvancingAForkDoesNotAdvanceTheOriginalChain() {
        var log = new List<string>();
        var context = Pipeline.Context();

        var chain = Pipeline.Chain(context,
            new Pipeline.Inline(async c => {
                var fork = c.Fork(c.Context);

                await fork.Next();

                Assert.True(fork.IsLastFilter);
                Assert.False(c.IsLastFilter);

                await c.Next();
            }),
            new Pipeline.Recording(log, "tail"));

        await chain.Next();

        Assert.Equal(new[] { "tail", "tail" }, log);
    }

    /// <summary>
    /// A fork carries the context it was handed, not the forking chain's. Every re-invocation
    /// use passes a cloned context precisely so the second run writes somewhere else.
    /// </summary>
    [Fact]
    public async Task AForkCarriesTheSuppliedContextRatherThanTheOriginalOne() {
        var original = Pipeline.Context(path: "/original");
        var replacement = original.Clone(request: original.Request.Clone(path: "/replacement"));

        var seen = new List<string>();

        var chain = Pipeline.Chain(original,
            new Pipeline.Inline(async c => {
                seen.Add(c.Context.Request.Path);

                await c.Fork(replacement).Next();
            }),
            new Pipeline.Inline(c => {
                seen.Add(c.Context.Request.Path);

                return Task.CompletedTask;
            }));

        await chain.Next();

        Assert.Equal(new[] { "/original", "/replacement" }, seen);
    }

    /// <summary>
    /// A cloned response owns its own state. Without this, a fork that produced a 404 would
    /// leave the status behind on the response the caller is going to send.
    /// </summary>
    [Fact]
    public async Task WritingToAForksClonedResponseLeavesTheOriginalResponseAlone() {
        var original = Pipeline.Context();
        original.Response.Status = 200;
        original.Response.ResponseValue = "original";

        var forkResponse = new TestExecutionResponse(new MemoryStream());
        var forkContext = original.Clone(response: forkResponse);

        var chain = Pipeline.Chain(original,
            new Pipeline.Inline(c => c.Fork(forkContext).Next()),
            new Pipeline.Inline(c => {
                c.Context.Response.Status = 404;
                c.Context.Response.ResponseValue = "fork";

                return Task.CompletedTask;
            }));

        await chain.Next();

        Assert.Equal(200, original.Response.Status);
        Assert.Equal("original", original.Response.ResponseValue);
        Assert.Equal(404, forkResponse.Status);
        Assert.Equal("fork", forkResponse.ResponseValue);
    }

    /// <summary>
    /// The same for the request. A fork that rewrites the path is the whole point of cloning
    /// one, and the original request has to survive it.
    /// </summary>
    [Fact]
    public async Task AForksClonedRequestDoesNotMutateTheOriginalRequest() {
        var original = Pipeline.Context(method: "GET", path: "/orders");

        var forkContext = original.Clone(
            request: original.Request.Clone(method: "POST", path: "/orders/audit"));

        var chain = Pipeline.Chain(original,
            new Pipeline.Inline(c => c.Fork(forkContext).Next()),
            new Pipeline.Inline(c => {
                c.Context.Request.Body = new MemoryStream("fork wrote here"u8.ToArray());

                return Task.CompletedTask;
            }));

        await chain.Next();

        Assert.Equal("GET", original.Request.Method);
        Assert.Equal("/orders", original.Request.Path);
        Assert.Same(Stream.Null, original.Request.Body);
    }

    /// <summary>
    /// Two forks taken from the same position write to their own responses without seeing each
    /// other's, which is what makes a fan-out over one request safe.
    /// </summary>
    [Fact]
    public async Task SiblingForksDoNotSeeEachOthersResponses() {
        var original = Pipeline.Context();

        var first = new TestExecutionResponse(new MemoryStream());
        var second = new TestExecutionResponse(new MemoryStream());

        var chain = Pipeline.Chain(original,
            new Pipeline.Inline(async c => {
                await c.Fork(original.Clone(response: first)).Next();
                await c.Fork(original.Clone(response: second)).Next();
            }),
            new Pipeline.Inline(async c => {
                var payload = Encoding.UTF8.GetBytes(
                    ReferenceEquals(c.Context.Response, first) ? "first" : "second");

                await c.Context.Response.Body.WriteAsync(payload);
            }));

        await chain.Next();

        Assert.Equal("first", Read(first));
        Assert.Equal("second", Read(second));
    }

    /// <summary>
    /// Forking from inside a fork resumes from the inner fork's position, not the outer one's,
    /// so a nested re-invocation does not restart the enclosing one.
    /// </summary>
    [Fact]
    public async Task ANestedForkResumesFromTheInnerForksPosition() {
        var log = new List<string>();
        var context = Pipeline.Context();

        var chain = Pipeline.Chain(context,
            new Pipeline.Inline(c => c.Fork(c.Context).Next()),
            new Pipeline.Inline(async c => {
                log.Add("middle");

                if (log.Count(entry => entry == "middle") == 1) {
                    await c.Fork(c.Context).Next();
                }
            }),
            new Pipeline.Recording(log, "innermost"));

        await chain.Next();

        Assert.Equal(new[] { "middle", "innermost" }, log);
    }

    /// <summary>
    /// A chain that has run to the end reports itself as the last filter, and forking it
    /// yields a chain with nothing left to do rather than one that replays the pipeline.
    /// </summary>
    [Fact]
    public async Task ForkingAnExhaustedChainYieldsAChainWithNothingLeftToRun() {
        var log = new List<string>();
        var context = Pipeline.Context();

        var chain = Pipeline.Chain(context, new Pipeline.Recording(log, "only"));

        await chain.Next();

        Assert.True(chain.IsLastFilter);

        var fork = chain.Fork(context);

        Assert.True(fork.IsLastFilter);

        await fork.Next();

        Assert.Equal(new[] { "only" }, log);
    }

    /// <summary>
    /// An empty chain is already at its end; <c>Next</c> is a completed task rather than an
    /// index error. Transports call <c>Next</c> on chains they did not build.
    /// </summary>
    [Fact]
    public async Task AnEmptyChainIsAlreadyOnItsLastFilter() {
        var chain = Pipeline.Chain(Pipeline.Context());

        Assert.True(chain.IsLastFilter);

        await chain.Next();

        Assert.True(chain.Fork(Pipeline.Context()).IsLastFilter);
    }

    /// <summary>
    /// <c>IsLastFilter</c> is false until the chain has handed out its final filter. The
    /// streaming transports read it to decide whether they still own the response body.
    /// </summary>
    [Fact]
    public async Task IsLastFilterBecomesTrueOnlyOnceTheFinalFilterHasBeenHandedOut() {
        var context = Pipeline.Context();
        var observed = new List<bool>();

        var chain = Pipeline.Chain(context,
            new Pipeline.Inline(c => {
                observed.Add(c.IsLastFilter);

                return c.Next();
            }),
            new Pipeline.Inline(c => {
                observed.Add(c.IsLastFilter);

                return c.Next();
            }));

        await chain.Next();

        Assert.Equal(new[] { false, true }, observed);
    }

    /// <summary>
    /// The context reached through the fork is the one the fork was created with, including
    /// when the caller replaces the service provider - a forked request that resolves its own
    /// scope depends on it.
    /// </summary>
    [Fact]
    public async Task AForkedContextKeepsAReplacedServiceProvider() {
        var original = Pipeline.Context();

        var replacementServices = Pipeline.Context().RequestServices;
        var forkContext = original.Clone(serviceProvider: replacementServices);

        IServiceProvider? seen = null;

        var chain = Pipeline.Chain(original,
            new Pipeline.Inline(c => c.Fork(forkContext).Next()),
            new Pipeline.Inline(c => {
                seen = c.Context.RequestServices;

                return Task.CompletedTask;
            }));

        await chain.Next();

        Assert.Same(replacementServices, seen);
        Assert.NotSame(original.RequestServices, seen);
    }

    /// <summary>
    /// A request cloned for a fork carries the parameters that were bound for the original,
    /// so a re-invocation does not have to deserialize the body a second time.
    /// </summary>
    [Fact]
    public void ACloneCarriesTheBoundParametersForward() {
        IExecutionRequest request = new TestExecutionRequest(
            "POST", "/orders", "application/json",
            new SimpleQueryStringCollection(new Dictionary<string, string>())) {
            Parameters = EmptyParameters.Instance
        };

        var clone = request.Clone(path: "/orders/audit");

        Assert.Same(EmptyParameters.Instance, clone.Parameters);
    }

    private static string Read(TestExecutionResponse response) =>
        Encoding.UTF8.GetString(((MemoryStream)response.Body).ToArray());
}
