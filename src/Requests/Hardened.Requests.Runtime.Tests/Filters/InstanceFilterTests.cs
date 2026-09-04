using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

/// <summary>
/// The filter at <c>FilterOrder.HandlerCreation</c>, and what it does when the container cannot
/// build the handler.
/// </summary>
/// <remarks>
/// H-13 from the 0.19.0-rc1000 trial: a handler with a constructor parameter no registration
/// satisfied answered a 500 with <c>Content-Length: 0</c>, because the throw here unwound past the
/// filter that writes a response. The message naming the service reached only the log.
/// </remarks>
public class InstanceFilterTests {

    public interface IClock { }

    public sealed class NeedsAClock {
        public NeedsAClock(IClock clock) {
            _ = clock;
        }
    }

    public sealed class NeedsNothing { }

    [Fact]
    public async Task ASatisfiableHandlerIsConstructedOntoTheContext() {
        var context = Pipeline.Context(configureServices: services => services.AddTransient<NeedsNothing>());

        await Pipeline.Chain(context, new InstanceFilter<NeedsNothing>()).Next();

        Assert.IsType<NeedsNothing>(context.HandlerInstance);
        Assert.Null(context.Response.ExceptionValue);
    }

    [Fact]
    public async Task AMissingDependencyIsRecordedNamingTheHandlerAndTheService() {
        var context = Pipeline.Context(configureServices: services => services.AddTransient<NeedsAClock>());

        context.HandlerInfo = Handler("GET", "/clock");

        await Pipeline.Chain(context, new InstanceFilter<NeedsAClock>()).Next();

        var exception = Assert.IsType<HandlerCreationException>(context.Response.ExceptionValue);

        Assert.Equal("GET /clock", exception.Handler);
        Assert.Equal(typeof(NeedsAClock), exception.HandlerType);
        Assert.Contains(nameof(IClock), exception.Message);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Null(context.HandlerInstance);
    }

    /// <summary>
    /// And the chain continues, which is what lets the serialization filter write the failure as
    /// the framework's error envelope instead of a bodyless 500.
    /// </summary>
    [Fact]
    public async Task AMissingDependencyStillReachesTheFilterThatWritesIt() {
        var context = Pipeline.Context(configureServices: services => services.AddTransient<NeedsAClock>());
        var reached = false;

        await Pipeline.Chain(context, new InstanceFilter<NeedsAClock>(), new Pipeline.Inline(chain => {
            reached = chain.Context.Response.Refused;

            return Task.CompletedTask;
        })).Next();

        Assert.True(reached);
    }

    private static IExecutionRequestHandlerInfo Handler(string method, string path) {
        var info = Substitute.For<IExecutionRequestHandlerInfo>();

        info.Method.Returns(method);
        info.Path.Returns(path);

        return info;
    }
}
