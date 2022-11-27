using Amazon.CloudWatchLogs;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.Canaries.Runtime.Services;

public interface ICloudWatchClientProvider
{
    IAmazonCloudWatchLogs LogsClient { get; }
}

[Expose]
[Singleton]
public class CloudWatchClientProvider : ICloudWatchClientProvider
{
    private readonly AmazonCloudWatchLogsClient _client = 
        new(new AmazonCloudWatchLogsConfig
        {
            
        });

    public IAmazonCloudWatchLogs LogsClient => _client;
}