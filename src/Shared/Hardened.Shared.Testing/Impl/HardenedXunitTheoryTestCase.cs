using System.ComponentModel;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Hardened.Shared.Testing.Impl
{
    public class HardenedXunitTheoryTestCase : XunitTheoryTestCase
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Called by the de-serializer", error: true)]
        public HardenedXunitTheoryTestCase() { }

        public HardenedXunitTheoryTestCase(IMessageSink messageSink, TestMethodDisplay methodDisplayOrDefault, TestMethodDisplayOptions displayOptions, ITestMethod testMethod) 
            : base(messageSink, methodDisplayOrDefault, displayOptions, testMethod)
        {
            

        }

        public override Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource)
        {
            return new HardenedTestCaseRunner(this, DisplayName, SkipReason, constructorArguments, new object[]{}, messageBus, aggregator, cancellationTokenSource).RunAsync();
        }
    }
}
