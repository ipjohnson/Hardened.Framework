using Amazon.SQS;
using Amazon.SQS.Model;
using Hardened.Amz.Canaries.Runtime.Configuration;
using Hardened.Amz.Canaries.Runtime.Models.Flight;
using Hardened.Amz.Canaries.Runtime.Models.Request;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hardened.Amz.Canaries.Runtime.Services;

public interface ISqsFlightScheduler
{
    Task ScheduleFlight(string canaryName, CanaryDefinition canaryDefinition);
}

[Expose]
[Singleton]
public class SqsFlightScheduler : ISqsFlightScheduler
{
    private readonly IAmazonSQS _sqsClient;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly ILogger<SqsFlightScheduler> _logger;
    private readonly string _queueUrl;

    public SqsFlightScheduler(
        IOptions<ICanaryConfigurationModel> model, 
        IJsonSerializer jsonSerializer,
        IOptions<ICanaryConfigurationModel> canaryConfig,
        IServiceProvider serviceProvider,
        ILogger<SqsFlightScheduler> logger)
    {
        _jsonSerializer = jsonSerializer;
        _logger = logger;
        _sqsClient = canaryConfig.Value.SqsClientProvider(serviceProvider);
        _queueUrl =
            $"https://sqs.{model.Value.Region}.amazonaws.com/{model.Value.AccountId}/{model.Value.SqsInvokeQueue}";
    }

    public async Task ScheduleFlight(
        string canaryName,
        CanaryDefinition canaryDefinition)
    {
        var invokeId = Guid.NewGuid().ToString();
        
        _logger.LogInformation("scheduling canary {canaryName} instance {invokeId} def {canaryDefinition}",
            canaryName,
            invokeId,
            canaryDefinition);
        
        await _sqsClient.SendMessageAsync(
            _queueUrl,
            _jsonSerializer.Serialize(new InvokeRequestModel(
                canaryName,
                invokeId,
                canaryDefinition)
            )
        );
    }
}