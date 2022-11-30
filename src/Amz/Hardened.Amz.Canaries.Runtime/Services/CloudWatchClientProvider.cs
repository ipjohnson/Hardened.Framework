using Amazon.CloudWatch;
using Amazon.CloudWatchLogs;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.Canaries.Runtime.Services;

public interface ICloudWatchClientProvider
{
    IAmazonCloudWatch CloudWatchClient { get; }
    
    IAmazonCloudWatchLogs LogsClient { get; }
}

[Expose]
[Singleton]
public class CloudWatchClientProvider : ICloudWatchClientProvider
{
    private readonly AmazonCloudWatchClient _cloudWatchClient = new ();
    private readonly AmazonCloudWatchLogsClient _logsClient = 
        new(new AmazonCloudWatchLogsConfig
        {
            
        });

    public IAmazonCloudWatch CloudWatchClient => _cloudWatchClient;
    
    public IAmazonCloudWatchLogs LogsClient => _logsClient;
}