using Xunit.Abstractions;
using Xunit.Sdk;

namespace Hardened.Shared.Testing.Impl
{
    public class HardenedTestCaseRunner : XunitTestCaseRunner
    {
        private readonly List<HardenedTestRunner> _testRunners = new();
        private readonly ExceptionAggregator _cleanupAggregator = new();

        public HardenedTestCaseRunner(IXunitTestCase testCase, string displayName, string skipReason, object[] constructorArguments, object[] testMethodArguments, IMessageBus messageBus, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) 
            : base(testCase, displayName, skipReason, constructorArguments, testMethodArguments, messageBus, aggregator, cancellationTokenSource)
        {

        }

        protected override Task AfterTestCaseStartingAsync()
        {
            var dataAttributes = TestMethod.GetTestAttributes<DataAttribute>().ToList();

            if (dataAttributes.Count == 0)
            {
                var theoryDisplayName = 
                    TypeUtility.GetDisplayNameWithArguments(TestCase.TestMethod.Method, DisplayName, Array.Empty<object>(), Array.Empty<ITypeInfo>());

                var test = new XunitTest(TestCase, theoryDisplayName.Replace("???", ""));

                var testRunner = new HardenedTestRunner(test, MessageBus, TestClass, ConstructorArguments, TestMethod,
                    TestMethodArguments, SkipReason, BeforeAfterAttributes, Aggregator, CancellationTokenSource);

                _testRunners.Add(testRunner);
            }
            else
            {
                foreach (var dataAttribute in dataAttributes)
                {
                    var parameterValuesSet = dataAttribute.GetData(TestMethod);

                    foreach (var testMethodArguments in parameterValuesSet)
                    {
                        var theoryDisplayName =
                            TypeUtility.GetDisplayNameWithArguments(TestCase.TestMethod.Method, DisplayName,
                                testMethodArguments, Array.Empty<ITypeInfo>());

                        var test = new XunitTest(TestCase, theoryDisplayName);

                        var testRunner = new HardenedTestRunner(
                            test,
                            MessageBus, 
                            TestClass, 
                            ConstructorArguments,
                            TestMethod,
                            testMethodArguments,
                            SkipReason, 
                            BeforeAfterAttributes,
                            Aggregator,
                            CancellationTokenSource);

                        _testRunners.Add(testRunner);
                    }
                }
            }

            return base.AfterTestCaseStartingAsync();
        }

        protected override Task BeforeTestCaseFinishedAsync()
        {
            Aggregator.Aggregate(_cleanupAggregator);

            return base.BeforeTestCaseFinishedAsync();
        }

        protected override async Task<RunSummary> RunTestAsync()
        {
            var runSummary = new RunSummary();

            foreach (var testRunner in _testRunners)
                runSummary.Aggregate(await testRunner.RunAsync());

            return runSummary;
        }
    }
}
