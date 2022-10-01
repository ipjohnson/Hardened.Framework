using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Execution;
using NSubstitute;
using SimpleFixture.NSubstitute;
using SimpleFixture.xUnit;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Execution;

[SubFixtureInitialize]
public class ExecutionChainTests
{

    [Theory]
    [AutoData]
    public async Task ExecuteZeroHandler(IExecutionContext context)
    {
        var chain = new ExecutionChain(new List<Func<IExecutionContext,IExecutionFilter>>(), context);

        await chain.Next();
    }
        
    [Theory]
    [AutoData]
    public async Task ExecuteOneHandler(IExecutionFilter filter, IExecutionContext context)
    {
        var chain = new ExecutionChain(new List<Func<IExecutionContext, IExecutionFilter>> { _=> filter }, context);

        filter.Execute(chain).Returns(Task.CompletedTask);

        await chain.Next();

        await filter.Received(1).Execute(chain);
    }
        
    [Theory]
    [AutoData]
    public async Task ExecuteOneHandlerMultiple(IExecutionFilter filter, IExecutionContext context)
    {
        var chain = new ExecutionChain(new List<Func<IExecutionContext, IExecutionFilter>> { _ => filter }, context);

        filter.Execute(chain).Returns(Task.CompletedTask);

        await chain.Next();

        await chain.Next();

        await chain.Next();

        await filter.Received(1).Execute(chain);
    }

    [Theory]
    [AutoData]
    public async Task ExecuteChainFork(IExecutionContext context)
    {
        var filter1 = Substitute.For<IExecutionFilter>();
        var filter2 = Substitute.For<IExecutionFilter>();

        var chain = new ExecutionChain(new List<Func<IExecutionContext, IExecutionFilter>> { _ => filter1, _ => filter2 }, context);

        filter1.Execute(chain).Returns(c =>
        {
            var chainArg = c.Arg<IExecutionChain>();

            for (var i = 0; i < 10; i++)
            {
                var forkChain = chainArg.Fork(context);

                Assert.NotSame(chain, forkChain);

                Assert.Equal(Task.CompletedTask, forkChain.Next());
            }

            return Task.CompletedTask;
        });

        filter2.Execute(Arg.Any<IExecutionChain>()).Returns(Task.CompletedTask);

        await chain.Next();

        await filter1.Received(1).Execute(chain);
        await filter2.Received(10).Execute(Arg.Any<IExecutionChain>());
    }
}