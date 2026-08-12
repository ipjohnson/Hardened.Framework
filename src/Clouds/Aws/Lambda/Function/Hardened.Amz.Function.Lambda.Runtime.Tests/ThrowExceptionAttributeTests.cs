using Hardened.Amz.Function.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using NSubstitute;

namespace Hardened.Amz.Function.Lambda.Runtime.Tests;

/// <summary>
/// <c>[ThrowException]</c> opts a handler out of the default behaviour. By default an exception is
/// captured on the response and serialised back to the caller, so the Lambda invocation reports
/// success; with the attribute the original exception is rethrown and the invocation fails, which
/// is what puts a message on a dead-letter queue or retries a stream shard.
/// </summary>
public class ThrowExceptionAttributeTests {

    private static IExecutionChain ChainWithException(Exception? exception) {
        var context = TestExecutionContext.Create(new MemoryStream(), new MemoryStream());
        context.Response.ExceptionValue = exception;

        return new TestExecutionChain(context);
    }

    [Fact]
    public async Task AnExceptionOnTheResponseIsRethrownUnwrapped() {
        var thrown = new InvalidOperationException("handler blew up");
        var chain = ChainWithException(thrown);

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ThrowExceptionAttribute().Execute(chain));

        Assert.Same(thrown, caught);
    }

    /// <summary>
    /// The default path. Without an exception on the response the filter is transparent, which is
    /// what leaves the serialise-and-return behaviour in place for every other handler.
    /// </summary>
    [Fact]
    public async Task NoExceptionOnTheResponseLeavesTheChainUntouched() {
        var chain = ChainWithException(null);

        await new ThrowExceptionAttribute().Execute(chain);

        Assert.Null(chain.Context.Response.ExceptionValue);
    }

    [Fact]
    public async Task TheRestOfTheChainRunsBeforeTheExceptionIsRethrown() {
        var context = TestExecutionContext.Create(new MemoryStream(), new MemoryStream());
        var ranNext = false;

        var chain = new TestExecutionChain(context, ctx => {
            ranNext = true;
            ctx.Response.ExceptionValue = new InvalidOperationException("set by the handler");

            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ThrowExceptionAttribute().Execute(chain));

        Assert.True(ranNext);
    }

    /// <summary>
    /// The filter has to run after the handler and before serialisation — later and the exception
    /// has already been turned into a response body.
    /// </summary>
    [Fact]
    public void TheFilterIsOrderedBeforeSerialization() {
        var attribute = new ThrowExceptionAttribute();

        var filterInfo = Assert.Single(attribute.GetFilters(Substitute.For<IExecutionRequestHandlerInfo>()));

        Assert.Equal(FilterOrder.BeforeSerialization, filterInfo.Order);
    }

    [Fact]
    public void TheAttributeIsItsOwnFilter() {
        var attribute = new ThrowExceptionAttribute();

        var filterInfo = Assert.Single(attribute.GetFilters(Substitute.For<IExecutionRequestHandlerInfo>()));

        Assert.Same(attribute, filterInfo.FilterFunc(Substitute.For<IExecutionContext>()));
    }
}
