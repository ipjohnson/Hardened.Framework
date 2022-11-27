using Amazon.CloudWatchLogs;
using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
using Hardened.Amz.Canaries.Runtime.Configuration;
using Hardened.Amz.Canaries.Runtime.DynamoDb;
using Hardened.Amz.Canaries.Runtime.Handlers;
using Hardened.Amz.Canaries.Runtime.Models.Flight;
using Hardened.Amz.Canaries.Runtime.Models.Request;
using Hardened.Amz.Canaries.Runtime.Services;
using Hardened.Amz.DynamoDbClient.Testing;
using Hardened.Amz.Function.Lambda.Runtime.Impl;
using Hardened.Amz.Function.Lambda.Testing;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Runtime.Json;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Hardened.Amz.Canaries.Runtime.Tests.Scenarios;

/// <summary>
/// This test calls canary discovery service
/// then calls invoker with data
/// </summary>
public class InitialFlightTests
{   
    [HardenedTest]
    [CanaryDatabase]
    public async Task FirstFlightTest(
        LambdaTestApp testApp,
        IOptions<ICanaryConfigurationModel> configuration,
        IServiceProvider serviceProvider,
        IJsonSerializer jsonSerializer,
        ICanaryDynamoReader dynamoReader, 
        [Mock] ICloudWatchClientProvider cloudWatchClientProvider,
        [Mock] IAmazonCloudWatchLogs cloudWatchLogs)
    {
        cloudWatchClientProvider.LogsClient.Returns(cloudWatchLogs);
        
        var result = 
            await testApp.Invoke<FlightControllerResponseModel>(
                "canary-flight-controller", new { });
        
        Assert.NotNull(result);
        Assert.True(result.Success);

        var sqsClient = configuration.Value.SqsClientProvider(serviceProvider);

        var callList = sqsClient.ReceivedCalls().ToList();
        
        Assert.Equal(6, callList.Count);

        var sqsEvent = new SQSEvent { Records = new List<SQSEvent.SQSMessage>() };
        var invokeRequests = new List<InvokeRequestModel>();
        
        foreach (var call in callList)
        {
            Assert.Equal("SendMessageAsync", call.GetMethodInfo().Name);
            var body = call.GetArguments()[1]!.ToString()!;
            
            invokeRequests.Add(
                jsonSerializer.Deserialize<InvokeRequestModel>(body));
            
            sqsEvent.Records.Add(
                new SQSEvent.SQSMessage
                {
                    Body = body
                });
        }

        var results = await testApp.Invoke<SQSBatchResponse>(
            CanaryInvokeHandler.FunctionName,
            sqsEvent
        );
        
        Assert.NotNull(results);
        Assert.Empty(results.BatchItemFailures);

        foreach (var requestModel in invokeRequests)
        {
            await ValidateRequestModel(dynamoReader, requestModel);
        }
    }

    private async Task ValidateRequestModel(ICanaryDynamoReader dynamoReader, InvokeRequestModel requestModel)
    {
        var historyList = await dynamoReader.GetCanaryHistory(requestModel.CanaryName);
        
        Assert.NotNull(historyList);
        Assert.Equal(1, historyList!.RecentFlights.Count);

        var flightInfo = historyList.RecentFlights.First();
        
        Assert.Equal(FlightStatus.Passed, flightInfo.FlightStatus);
        Assert.Equal(requestModel.InvokeId, flightInfo.FlightNumber);
        Assert.NotNull(flightInfo.FlightTime);
    }

    private void RegisterDependencies(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<IXunitAssemblyUnderTest>(
            new XunitAssemblyUnderTest(GetType().Assembly)
        );
        
        serviceCollection.AddSingleton<ILambdaInvokeFilterProvider, TestLambdaInvokeFilterProvider>();
    }

    private void Configure(IAppConfig appConfig)
    {
        var sqsClient = Substitute.For<IAmazonSQS>();
        
        appConfig.Amend<CanaryConfigurationModel>(
            model => model.SqsClientProvider = _ => sqsClient 
        );
    }
}