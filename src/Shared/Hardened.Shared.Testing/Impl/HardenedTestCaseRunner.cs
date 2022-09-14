using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Sdk;

namespace Hardened.Shared.Testing.Impl
{
    public class HardenedTestCaseRunner : XunitTestCaseRunner
    {
        public HardenedTestCaseRunner(IXunitTestCase testCase, string displayName, string skipReason, object[] constructorArguments, object[] testMethodArguments, IMessageBus messageBus, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) 
            : base(testCase, displayName, skipReason, constructorArguments, testMethodArguments, messageBus, aggregator, cancellationTokenSource)
        {

        }

        protected override Task<RunSummary> RunTestAsync()
        {
            return base.RunTestAsync();
        }
    }
}
