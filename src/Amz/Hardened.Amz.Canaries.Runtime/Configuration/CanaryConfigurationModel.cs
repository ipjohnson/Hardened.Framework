using Amazon.CloudWatch;
using Amazon.SQS;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.Canaries.Runtime.Configuration;

[ConfigurationModel]
public partial class CanaryConfigurationModel
{
    [FromEnvironmentVariable("CANARY_DATA_TABLE")]
    private string _dynamoDataTable = "canary-data-table";

    [FromEnvironmentVariable("SEND_TO_CW_METRICS")]
    private bool _sendMetricsToCloudWatch = true;

    [FromEnvironmentVariable("LOG_GROUP_PREFIX")]
    private string _logGroupPrefix = "/canary/";

    [FromEnvironmentVariable("SQS_CANARY_QUEUE")]
    private string _sqsInvokeQueue = "sqs-canary-queue";

    [FromEnvironmentVariable("AWS_REGION")]
    private string _region = "us-west-2";

    [FromEnvironmentVariable("AWS_ACCOUNT_ID")]
    private string _accountId = "account-id";

    [FromEnvironmentVariable("METRICS_NAMESPACE")]
    private string _metricsNamespace = "canary-metrics";

    [FromEnvironmentVariable("ENABLE_METRICS")]
    private bool _enableMetrics = true;

    private int _retentionDays = 180;
    
    private Func<IServiceProvider, IAmazonSQS> _sqsClientProvider = 
        _ => new AmazonSQSClient();

    private Func<IServiceProvider, IAmazonCloudWatch> _cloudWatchProvider =
        _ => new AmazonCloudWatchClient();
}