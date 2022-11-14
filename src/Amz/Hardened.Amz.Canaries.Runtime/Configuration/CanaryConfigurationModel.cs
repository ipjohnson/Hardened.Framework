using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.Canaries.Runtime.Configuration;

[ConfigurationModel]
public partial class CanaryConfigurationModel
{
    [FromEnvironmentVariable("CANARY_DATA_TABLE")]
    private string dynamoDataTable = "canary-data-table";

    [FromEnvironmentVariable("SEND_TO_CW_METRICS")]
    private bool sendMetricsToCloudWatch = true;

    [FromEnvironmentVariable("LOG_GROUP_PREFIX")]
    private string logGroupPrefix = "canary-";
}