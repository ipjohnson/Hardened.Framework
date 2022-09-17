using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Hardened.Shared.Testing.Impl
{
    internal class HardenedTestRunner : XunitTestRunner
    {
        public HardenedTestRunner(ITest test, IMessageBus messageBus, Type testClass, object[] constructorArguments, MethodInfo testMethod, object[] testMethodArguments, string skipReason, IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) : base(test, messageBus, testClass, constructorArguments, testMethod, testMethodArguments, skipReason, beforeAfterAttributes, aggregator, cancellationTokenSource)
        {

        }
        
        protected override Task<decimal> InvokeTestMethodAsync(ExceptionAggregator aggregator)
        {
            return new HardenedTestInvoker(
                Test, 
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
}
