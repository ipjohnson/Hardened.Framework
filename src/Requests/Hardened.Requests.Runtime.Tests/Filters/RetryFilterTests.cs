using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Shared.Runtime.Collections;
using NSubstitute;
using SimpleFixture.NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

[SubFixtureInitialize]
public class RetryFilterTests {
    [Fact]
    public async Task SucceedsOnFirstTry_CallsChainOnce() {
        var memoryStreamPool = new MemoryStreamPool();
        var filter = new RetryFilter(memoryStreamPool, 3, 0);

        var chain = Substitute.For<IExecutionChain>();
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();

        request.Body.Returns(new MemoryStream());
        context.Request.Returns(request);
        chain.Context.Returns(context);

        var callCount = 0;
        chain.Next().Returns(_ => {
            callCount++;
            return Task.CompletedTask;
        });

        await filter.Execute(chain);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task RetriesOnFailure_SucceedsOnSecondTry() {
        var memoryStreamPool = new MemoryStreamPool();
        var filter = new RetryFilter(memoryStreamPool, 3, 0);

        var chain = Substitute.For<IExecutionChain>();
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();

        request.Body.Returns(new MemoryStream());
        context.Request.Returns(request);
        chain.Context.Returns(context);

        var callCount = 0;
        chain.Next().Returns(_ => {
            callCount++;
            if (callCount == 1) {
                throw new InvalidOperationException("transient failure");
            }
            return Task.CompletedTask;
        });

        await filter.Execute(chain);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ThrowsLastException_AfterAllRetriesExhausted() {
        var memoryStreamPool = new MemoryStreamPool();
        var filter = new RetryFilter(memoryStreamPool, 3, 0);

        var chain = Substitute.For<IExecutionChain>();
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();

        request.Body.Returns(new MemoryStream());
        context.Request.Returns(request);
        chain.Context.Returns(context);

        var callCount = 0;
        chain.Next().Returns(_ => {
            callCount++;
            throw new InvalidOperationException($"failure {callCount}");
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => filter.Execute(chain));

        Assert.Equal(3, callCount);
        Assert.Equal("failure 3", ex.Message);
    }

    [Fact]
    public async Task RequestBody_IsReplayableAcrossRetries() {
        var memoryStreamPool = new MemoryStreamPool();
        var filter = new RetryFilter(memoryStreamPool, 3, 0);

        var bodyContent = new byte[] { 1, 2, 3, 4, 5 };
        var bodyStream = new MemoryStream(bodyContent);

        var chain = Substitute.For<IExecutionChain>();
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();

        request.Body.Returns(bodyStream);
        context.Request.Returns(request);
        chain.Context.Returns(context);

        var bodiesRead = new List<byte[]>();
        var callCount = 0;

        chain.Next().Returns(async _ => {
            callCount++;

            var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms);
            bodiesRead.Add(ms.ToArray());

            if (callCount < 2) {
                throw new InvalidOperationException("transient");
            }
        });

        await filter.Execute(chain);

        Assert.Equal(2, bodiesRead.Count);
        Assert.Equal(bodyContent, bodiesRead[0]);
        Assert.Equal(bodyContent, bodiesRead[1]);
    }
}
