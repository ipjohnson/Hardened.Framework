using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Hardened.Shared.Testing.Impl;

internal class HardenedTestRunner : XunitTestRunner
{
    private readonly TestOutputHelper _testOutputHelper;

    public HardenedTestRunner(
        ITest test, 
        TestOutputHelper testOutputHelper,
        IMessageBus messageBus, 
        Type testClass,
        object[] constructorArguments,
        MethodInfo testMethod,
        object[] testMethodArguments, 
        string skipReason,
        IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes, 
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        : base(test, messageBus, testClass, constructorArguments, testMethod, testMethodArguments, skipReason, beforeAfterAttributes, aggregator, cancellationTokenSource)
    {
        _testOutputHelper = testOutputHelper;
    }

    protected override async Task<Tuple<decimal, string>> InvokeTestAsync(ExceptionAggregator aggregator)
    {
        _testOutputHelper.Initialize(MessageBus, Test);
        
        var result = await base.InvokeTestAsync(aggregator);

        var newResult = new Tuple<decimal, string>(result.Item1, _testOutputHelper.Output);

        _testOutputHelper.Uninitialize();
        
        return newResult;
    }

    protected override Task<decimal> InvokeTestMethodAsync(ExceptionAggregator aggregator)
    {
        return new HardenedTestInvoker(
            Test, 
            _testOutputHelper,
            MessageBus, 
            TestClass,
            ConstructorArguments, 
            TestMethod, 
            TestMethodArguments, 
            BeforeAfterAttributes,
            aggregator, 
            CancellationTokenSource).RunAsync();
    }
}