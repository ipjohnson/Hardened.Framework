using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Middleware;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Middleware;

/// <summary>
/// What writes a response that was decided in the middleware chain.
///
/// <para>
/// A middleware refuses by setting the response and returning without calling <c>Next</c>. Nothing
/// in that chain turns what it set into bytes - the filter that does lives in a handler chain that
/// a refused request never enters - so a global rate limiter's 429 or a CORS rejection came back
/// with the right status and an empty body. These are the cases that says whether that still
/// happens.
/// </para>
/// </summary>
public class ResponseFinalizerFilterTests {

    private static IExecutionContext Context(
        IContextSerializationService? serialization = null, string method = "GET") =>
        Pipeline.Context(
            method: method,
            configureServices: services => {
                if (serialization != null) {
                    services.AddSingleton(serialization);
                }
            });

    /// <summary>Records what it was asked to write.</summary>
    private static IContextSerializationService Recording(List<object?> written) {
        var serialization = Substitute.For<IContextSerializationService>();

        serialization.SerializeResponse(Arg.Any<IExecutionContext>())
            .Returns(callInfo => {
                var context = callInfo.Arg<IExecutionContext>();

                written.Add(context.Response.ResponseValue);
                context.Response.ShouldSerialize = false;

                return Task.CompletedTask;
            });

        return serialization;
    }

    /// <summary>
    /// A middleware that answered with a value gets that value written. This is the whole point of
    /// the filter.
    /// </summary>
    [Fact]
    public async Task Execute_SerializesAValueSetByAShortCircuitingMiddleware() {
        var written = new List<object?>();
        var context = Context(Recording(written));

        await Pipeline.Chain(
            context,
            new ResponseFinalizerFilter(),
            new Pipeline.Inline(chain => {
                chain.Context.Response.Status = 429;
                chain.Context.Response.ResponseValue = "too-many-requests";

                return Task.CompletedTask;
            })).Next();

        Assert.Equal(new object?[] { "too-many-requests" }, written);
        Assert.Equal(429, context.Response.Status);
    }

    /// <summary>A recorded exception is written on the same terms.</summary>
    [Fact]
    public async Task Execute_SerializesAnExceptionSetByAShortCircuitingMiddleware() {
        var written = new List<object?>();
        var serialization = Recording(written);
        var context = Context(serialization);

        await Pipeline.Chain(
            context,
            new ResponseFinalizerFilter(),
            new Pipeline.Inline(chain => {
                chain.Context.Response.ExceptionValue = new InvalidOperationException("nope");

                return Task.CompletedTask;
            })).Next();

        await serialization.Received(1).SerializeResponse(Arg.Any<IExecutionContext>());
    }

    /// <summary>
    /// A response already written is not written again. The handler chain's own serializer clears
    /// <c>ShouldSerialize</c> on its way out, and that is what this reads.
    /// </summary>
    [Fact]
    public async Task Execute_DoesNotSerializeAResponseSomethingElseAlreadyWrote() {
        var written = new List<object?>();
        var serialization = Recording(written);
        var context = Context(serialization);

        await Pipeline.Chain(
            context,
            new ResponseFinalizerFilter(),
            new Pipeline.Inline(chain => {
                chain.Context.Response.ResponseValue = "already written";
                chain.Context.Response.ShouldSerialize = false;

                return Task.CompletedTask;
            })).Next();

        Assert.Empty(written);
    }

    /// <summary>
    /// Nothing on the response means nothing to write. Serializing here would reach the null-value
    /// handler, which assigns a status by verb - so an unmatched POST would come back 200, which is
    /// the defect this must not reintroduce one layer up.
    /// </summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task Execute_LeavesARequestNothingAnsweredAlone(string method) {
        var written = new List<object?>();
        var context = Context(Recording(written), method);

        await Pipeline.Chain(
            context,
            new ResponseFinalizerFilter(),
            new Pipeline.Inline(_ => Task.CompletedTask)).Next();

        Assert.Empty(written);
        Assert.Null(context.Response.Status);
    }

    /// <summary>
    /// A status with no body - a 204, a 405, a redirect - is left alone. Those responses are the
    /// status and their headers, and there is nothing to negotiate a representation for.
    /// </summary>
    [Fact]
    public async Task Execute_LeavesAStatusOnlyResponseAlone() {
        var written = new List<object?>();
        var context = Context(Recording(written));

        await Pipeline.Chain(
            context,
            new ResponseFinalizerFilter(),
            new Pipeline.Inline(chain => {
                chain.Context.Response.Status = 204;

                return Task.CompletedTask;
            })).Next();

        Assert.Empty(written);
        Assert.Equal(204, context.Response.Status);
    }

    /// <summary>
    /// A pipeline assembled without the serialization stack still runs. Some hosts and most bare
    /// test pipelines have no <see cref="IContextSerializationService"/> registered at all, and
    /// throwing there would break them for a response they were never going to write.
    /// </summary>
    [Fact]
    public async Task Execute_DoesNotThrowWhenNoSerializationServiceIsRegistered() {
        var context = Context(serialization: null);

        await Pipeline.Chain(
            context,
            new ResponseFinalizerFilter(),
            new Pipeline.Inline(chain => {
                chain.Context.Response.ResponseValue = "value";

                return Task.CompletedTask;
            })).Next();

        Assert.Equal("value", context.Response.ResponseValue);
    }

    /// <summary>
    /// The filter runs the rest of the chain before doing anything, so a middleware ordered after
    /// it still gets to answer.
    /// </summary>
    [Fact]
    public async Task Execute_RunsTheRestOfTheChainFirst() {
        var log = new List<string>();
        var context = Context(Recording(new List<object?>()));

        await Pipeline.Chain(
            context,
            new ResponseFinalizerFilter(),
            new Pipeline.Recording(log, "first"),
            new Pipeline.Recording(log, "second")).Next();

        Assert.Equal(new[] { "first", "second" }, log);
    }

    /// <summary>
    /// The finalizer is seeded into every middleware chain rather than added by each host, because
    /// there are five hosts and one that forgot would answer refusals with an empty body.
    /// </summary>
    [Fact]
    public void GetExecutionChain_IncludesTheFinalizerWithoutAnyHostRegisteringIt() {
        var log = new List<string>();
        var service = new MiddlewareService();

        service.Use(_ => new Pipeline.Recording(log, "host"));

        var context = Context(Recording(new List<object?>()));
        var chain = service.GetExecutionChain(context);

        // Two filters: the seeded finalizer, then the one registered above.
        Assert.False(chain.IsLastFilter);

        chain.Next().GetAwaiter().GetResult();

        Assert.Equal(new[] { "host" }, log);
    }

    /// <summary>
    /// End to end through a real middleware service: a short-circuiting middleware produces a body,
    /// where before it produced a status and nothing else.
    /// </summary>
    [Fact]
    public async Task GetExecutionChain_AShortCircuitingMiddlewareNowProducesABody() {
        var serialization = Substitute.For<IContextSerializationService>();

        serialization.SerializeResponse(Arg.Any<IExecutionContext>())
            .Returns(async callInfo => {
                var context = callInfo.Arg<IExecutionContext>();
                var bytes = Encoding.UTF8.GetBytes(context.Response.ResponseValue!.ToString()!);

                await context.Response.Body.WriteAsync(bytes);

                context.Response.ShouldSerialize = false;
            });

        var context = Context(serialization);
        var service = new MiddlewareService();

        service.Use(_ => new Pipeline.Inline(chain => {
            chain.Context.Response.Status = 429;
            chain.Context.Response.ResponseValue = "rate limited";

            return Task.CompletedTask;
        }));

        await service.GetExecutionChain(context).Next();

        Assert.Equal(429, context.Response.Status);
        Assert.Equal(
            "rate limited",
            Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }
}
