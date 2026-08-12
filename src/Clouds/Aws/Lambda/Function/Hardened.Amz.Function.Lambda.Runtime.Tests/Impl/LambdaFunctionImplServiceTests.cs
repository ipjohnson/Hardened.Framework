using System.Text;
using Amazon.Lambda.Core;
using Hardened.Amz.Function.Lambda.Runtime.Impl;
using Hardened.Amz.Function.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Hardened.Amz.Function.Lambda.Runtime.Tests.Impl;

/// <summary>
/// The entry point every Hardened Lambda function is invoked through: a stream in, a stream out,
/// and an <see cref="ILambdaContext"/> that the rest of the request can reach.
/// </summary>
public class LambdaFunctionImplServiceTests {

    private sealed class Harness {
        public Harness() {
            var services = new ServiceCollection();
            RootProvider = services.BuildServiceProvider();

            Middleware = Substitute.For<IMiddlewareService>();
            Middleware.GetExecutionChain(Arg.Any<IExecutionContext>()).Returns(callInfo => {
                var context = callInfo.Arg<IExecutionContext>();

                return new TestExecutionChain(context, ctx => {
                    Contexts.Add(ctx);

                    return OnRequest(ctx);
                });
            });

            Service = new LambdaFunctionImplService(
                Middleware,
                new MemoryStreamPool(),
                RootProvider,
                Substitute.For<IKnownServices>(),
                Accessor);
        }

        public ServiceProvider RootProvider { get; }

        public IMiddlewareService Middleware { get; }

        public ILambdaContextAccessor Accessor { get; } = new LambdaContextAccessor();

        public LambdaFunctionImplService Service { get; }

        public List<IExecutionContext> Contexts { get; } = [];

        public Func<IExecutionContext, Task> OnRequest { get; set; } = _ => Task.CompletedTask;

        public IExecutionContext Single => Assert.Single(Contexts);
    }

    private static MemoryStream Payload(string content) {
        return new MemoryStream(Encoding.UTF8.GetBytes(content));
    }

    /// <summary>
    /// The context is published before the chain runs. Handler resolution reads the function name
    /// off the accessor, so a context published afterwards would leave every invocation unable to
    /// find its handler.
    /// </summary>
    [Fact]
    public async Task TheLambdaContextIsPublishedOnTheAccessorForTheRequest() {
        var harness = new Harness();
        var context = new FakeLambdaContext("Process");

        var seenDuringRequest = default(ILambdaContext);
        harness.OnRequest = _ => {
            seenDuringRequest = harness.Accessor.Context;

            return Task.CompletedTask;
        };

        await harness.Service.InvokeFunction(Payload("{}"), context);

        Assert.Same(context, seenDuringRequest);
    }

    /// <summary>
    /// The function name becomes the request path. That is what a generated handler package matches
    /// against, so an invocation of "Process" has to arrive on path "Process".
    /// </summary>
    [Fact]
    public async Task TheFunctionNameBecomesTheRequestPath() {
        var harness = new Harness();

        await harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("ProcessOrder"));

        Assert.Equal("ProcessOrder", harness.Single.Request.Path);
    }

    [Fact]
    public async Task TheRequestMethodIsInvoke() {
        var harness = new Harness();

        await harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process"));

        Assert.Equal("Invoke", harness.Single.Request.Method);
    }

    [Fact]
    public async Task TheIncomingStreamIsTheRequestBody() {
        var harness = new Harness();
        var payload = Payload("{\"value\":42}");

        await harness.Service.InvokeFunction(payload, new FakeLambdaContext("Process"));

        Assert.Same(payload, harness.Single.Request.Body);
    }

    /// <summary>
    /// Client-context custom values are the only caller-supplied metadata a direct Lambda invoke
    /// carries, so they are mapped onto request headers where filters can read them.
    /// </summary>
    [Fact]
    public async Task ClientContextCustomValuesArriveAsRequestHeaders() {
        var harness = new Harness();
        var context = new FakeLambdaContext("Process", new Dictionary<string, string> {
            { "tenant", "acme" },
            { "trace", "abc123" }
        });

        await harness.Service.InvokeFunction(Payload("{}"), context);

        Assert.Equal("acme", harness.Single.Request.Headers["tenant"].ToString());
        Assert.Equal("abc123", harness.Single.Request.Headers["trace"].ToString());
    }

    [Fact]
    public async Task AnInvokeWithoutAClientContextStillProducesAHeaderCollection() {
        var harness = new Harness();

        await harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process"));

        Assert.Empty(harness.Single.Request.Headers);
    }

    /// <summary>
    /// The returned stream is rewound. Returning it at its write position hands AWS an empty
    /// response body while every assertion about what was written still passes.
    /// </summary>
    [Fact]
    public async Task TheReturnedStreamIsRewoundToTheStartOfWhatTheHandlerWrote() {
        var harness = new Harness();
        harness.OnRequest = async ctx => {
            var bytes = Encoding.UTF8.GetBytes("{\"ok\":true}");

            await ctx.Response.Body.WriteAsync(bytes);
        };

        var result = await harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process"));

        Assert.Equal(0, result.Position);
        Assert.Equal("{\"ok\":true}", new StreamReader(result).ReadToEnd());
    }

    [Fact]
    public async Task TheRequestServicesAreAScopeSeparateFromTheRootProvider() {
        var harness = new Harness();

        await harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process"));

        Assert.Same(harness.RootProvider, harness.Single.RootServiceProvider);
        Assert.NotSame(harness.RootProvider, harness.Single.RequestServices);
    }

    [Fact]
    public async Task AnExceptionFromTheChainPropagatesToTheCaller() {
        var harness = new Harness();
        harness.OnRequest = _ => throw new InvalidOperationException("chain failed");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process")));
    }
}
