using Amazon.CloudWatchLogs;
using Amazon.Lambda.SQSEvents;
using Hardened.Amz.Canaries.Runtime.Models.Flight;
using Hardened.Amz.Canaries.Runtime.Models.Request;
using Hardened.Amz.Canaries.Runtime.Services;
using Hardened.Amz.Canaries.Runtime.Tests.Canaries;
using Hardened.Shared.Runtime.Json;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Hardened.Amz.Canaries.Runtime.Tests.Services;

public class XUnitInvokeServiceTests
{
    [HardenedTest]
    [CanaryDatabase]
    public async Task InvokeSimpleTest(
        XUnitInvokeService invokeService,
        IJsonSerializer jsonSerializer,
        [Mock] ICloudWatchClientProvider cloudWatchClientProvider,
        [Mock] IAmazonCloudWatchLogs cloudWatchLogs)
    {
        cloudWatchClientProvider.LogsClient.Returns(cloudWatchLogs);

        var canaryDefinition = new CanaryDefinition(
            typeof(CanaryTests).FullName!,
            nameof(CanaryTests.DefaultCanary),
            new CanaryFrequency(1, CanaryFrequencyUnit.Minute, CanaryFlightStyle.Strict, false),
            true
        );
        
        await invokeService.InvokeTests(new List<InvokeRequestModel>
        {
            new ("DefaultCanary", "12345", canaryDefinition)
        });
    }

    private void RegisterDependencies(IServiceCollection collection)
    {
        collection.AddSingleton<IXunitAssemblyUnderTest>(
            new XunitAssemblyUnderTest(GetType().Assembly));
    }
}