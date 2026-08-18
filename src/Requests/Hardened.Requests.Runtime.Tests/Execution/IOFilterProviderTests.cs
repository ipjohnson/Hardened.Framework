using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Execution;

/// <summary>
/// What builds the filter that reads a request body and writes a response.
/// </summary>
/// <remarks>
/// <para>
/// The provider was at 31% line / 25% branch, and the untaken branches are all in
/// <c>SetupHeaderActions</c> — which collapses four configurations into one delegate, or none.
/// Only the "nothing configured" shape was ever exercised, so the composition every application
/// setting a security header depends on was never run.
/// </para>
/// <para>
/// Asserted through a real filter rather than by reflecting on the returned delegate: the
/// observable behaviour is which headers are on the response after the filter has run, and the
/// collapsing is an optimisation underneath that.
/// </para>
/// </remarks>
public class IOFilterProviderTests {

    private static IOFilterProvider Provider(Action<ResponseHeaderConfiguration>? configure = null) {
        var configuration = new ResponseHeaderConfiguration();

        configure?.Invoke(configuration);

        var serialization = Substitute.For<IContextSerializationService>();

        serialization.SerializeResponse(Arg.Any<IExecutionContext>()).Returns(Task.CompletedTask);

        return new IOFilterProvider(
            serialization, Options.Create<IResponseHeaderConfiguration>(configuration));
    }

    private static IExecutionRequestHandlerInfo HandlerInfo() =>
        Substitute.For<IExecutionRequestHandlerInfo>();

    private static Task<IExecutionRequestParameters> NoParameters(IExecutionContext _) =>
        Task.FromResult<IExecutionRequestParameters>(EmptyParameters.Instance);

    /// <summary>Runs the provided filter over a real context and hands back that context.</summary>
    private static async Task<IExecutionContext> Run(IExecutionFilter filter) {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, filter).Next();

        return context;
    }

    [Fact]
    public async Task NoConfiguredHeadersLeavesTheResponseHeadersAlone() {
        var context = await Run(Provider().ProvideFilter(HandlerInfo(), NoParameters));

        Assert.Empty(context.Response.Headers);
    }

    [Fact]
    public async Task ACommonHeaderReachesTheResponse() {
        var filter = Provider(configuration => configuration.Add("X-Frame-Options", "DENY"))
            .ProvideFilter(HandlerInfo(), NoParameters);

        var context = await Run(filter);

        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
    }

    [Fact]
    public async Task EveryCommonHeaderReachesTheResponse() {
        var filter = Provider(configuration => {
                configuration.Add("X-Frame-Options", "DENY");
                configuration.Add("X-Content-Type-Options", "nosniff");
            })
            .ProvideFilter(HandlerInfo(), NoParameters);

        var context = await Run(filter);

        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
    }

    [Fact]
    public async Task AConfiguredActionRunsAgainstTheResponse() {
        var filter = Provider(configuration =>
                configuration.Add(context => context.Response.Headers["X-Trace"] = "on"))
            .ProvideFilter(HandlerInfo(), NoParameters);

        var context = await Run(filter);

        Assert.Equal("on", context.Response.Headers["X-Trace"].ToString());
    }

    /// <summary>
    /// A single action is returned unwrapped rather than inside a loop. Behaviourally identical,
    /// which is the point — the optimisation must not change what runs.
    /// </summary>
    [Fact]
    public async Task ASingleActionRunsExactlyOnce() {
        var calls = 0;

        var filter = Provider(configuration => configuration.Add(_ => calls++))
            .ProvideFilter(HandlerInfo(), NoParameters);

        await Run(filter);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SeveralActionsAllRunInOrder() {
        var log = new List<string>();

        var filter = Provider(configuration => {
                configuration.Add(_ => log.Add("first"));
                configuration.Add(_ => log.Add("second"));
            })
            .ProvideFilter(HandlerInfo(), NoParameters);

        await Run(filter);

        Assert.Equal(["first", "second"], log);
    }

    /// <summary>
    /// Both families configured. The common headers are appended after the actions, so an action
    /// that sets the same name is overwritten by the configured value.
    /// </summary>
    [Fact]
    public async Task ActionsAndCommonHeadersBothApply() {
        var filter = Provider(configuration => {
                configuration.Add(context => context.Response.Headers["X-Trace"] = "on");
                configuration.Add("X-Frame-Options", "DENY");
            })
            .ProvideFilter(HandlerInfo(), NoParameters);

        var context = await Run(filter);

        Assert.Equal("on", context.Response.Headers["X-Trace"].ToString());
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
    }

    [Fact]
    public async Task AMultiValueCommonHeaderKeepsEveryValue() {
        var filter = Provider(configuration =>
                configuration.Add("Vary", new StringValues(["Accept", "Accept-Encoding"])))
            .ProvideFilter(HandlerInfo(), NoParameters);

        var context = await Run(filter);

        var vary = context.Response.Headers["Vary"];

        Assert.Equal(2, vary.Count);
        Assert.Equal("Accept", vary[0]);
        Assert.Equal("Accept-Encoding", vary[1]);
    }

    #region streamed filters

    [Fact]
    public void TheStreamedFilterIsBuiltForTheItemType() {
        var filter = Provider()
            .ProvideAsyncEnumerableFilter<string>(HandlerInfo(), NoParameters);

        Assert.IsType<AsyncEnumerableIoFilter<string>>(filter);
    }

    /// <summary>
    /// The two-argument overload exists for generated code that predates framings, and must keep
    /// answering newline-delimited JSON.
    /// </summary>
    [Fact]
    public async Task TheTwoArgumentStreamedOverloadFramesAsNdjson() {
        var filter = Provider()
            .ProvideAsyncEnumerableFilter<string>(HandlerInfo(), NoParameters);

        var context = Pipeline.Context();

        context.Response.ResponseValue = Values();

        await Pipeline.Chain(context, filter).Next();

        Assert.Equal(NdjsonFraming.Instance.ContentType, context.Response.ContentType);
    }

    [Fact]
    public async Task ANamedFramingIsUsedInsteadOfTheDefault() {
        var filter = Provider()
            .ProvideAsyncEnumerableFilter<string>(HandlerInfo(), NoParameters, SseFraming.Instance);

        var context = Pipeline.Context();

        context.Response.ResponseValue = Values();

        await Pipeline.Chain(context, filter).Next();

        Assert.Equal(SseFraming.Instance.ContentType, context.Response.ContentType);
    }

    [Fact]
    public async Task ConfiguredHeadersReachAStreamedResponseToo() {
        var filter = Provider(configuration => configuration.Add("X-Frame-Options", "DENY"))
            .ProvideAsyncEnumerableFilter<string>(HandlerInfo(), NoParameters);

        var context = Pipeline.Context();

        context.Response.ResponseValue = Values();

        await Pipeline.Chain(context, filter).Next();

        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
    }

    private static async IAsyncEnumerable<string> Values() {
        yield return "alpha";

        await Task.CompletedTask;
    }

    #endregion
}
