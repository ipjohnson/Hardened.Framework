using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Hardened.Amz.Canaries.Runtime.Configuration;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.Options;

namespace Hardened.Amz.Canaries.Runtime.Services;

public interface ICloudWatchLogStreamWriter
{
    Task WriteLogStatements(
        string canaryName,
        string invokeId,
        List<Tuple<string, DateTime>> logStatements);
}

[Expose]
[Singleton]
public class CloudWatchLogStreamWriter : ICloudWatchLogStreamWriter
{
    private ICloudWatchClientProvider _cloudWatchClientProvider;
    private int _retentionDays;

    public CloudWatchLogStreamWriter(
        ICloudWatchClientProvider cloudWatchClientProvider, 
        IOptions<ICanaryConfigurationModel> configuration)
    {
        _cloudWatchClientProvider = cloudWatchClientProvider;
        _retentionDays = configuration.Value.RetentionDays;
    }

    public async Task WriteLogStatements(
        string canaryName,
        string invokeId, 
        List<Tuple<string, DateTime>> logStatements)
    {
        var logGroupName = 
            await CreateLogGroup(canaryName);

        var logStreamName = await CreateStreamGroup(logGroupName, invokeId);

        await WriteToLogStream(logGroupName, logStreamName, logStatements);

    }
    
    private async Task WriteToLogStream(string logGroupName, string logStreamName, List<Tuple<string,DateTime>> logStatements)
    {
        await WriteLogs(logGroupName, logStreamName, logStatements);
    }

    private async Task WriteLogs(
        string logGroupName,
        string logStreamName,
        List<Tuple<string, DateTime>> logStatements,
        string contentType = "")
    {
        while (logStatements.Count > 0)
        {
            var request = new PutLogEventsRequest
            {
                LogGroupName = logGroupName,
                LogStreamName = logStreamName,
                LogEvents = new List<InputLogEvent>()
            };

            if (!string.IsNullOrEmpty(contentType))
            {
                
            }
            
            for (var i = 0; i < logStatements.Count && i < 50; i++)
            {
                request.LogEvents.Add(new InputLogEvent
                {
                    Message = logStatements[i].Item1,
                    Timestamp = logStatements[i].Item2
                });
            }

            logStatements.RemoveRange(0, request.LogEvents.Count);

            await _cloudWatchClientProvider.LogsClient.PutLogEventsAsync(request);
        }
    }

    private async Task<string> CreateStreamGroup(string logGroupName, string invokeId)
    {
        await _cloudWatchClientProvider.LogsClient.CreateLogStreamAsync(
            new CreateLogStreamRequest(logGroupName, invokeId)
        );

        return invokeId;
    }

    private async Task<string> CreateLogGroup(string canaryName)
    {
        canaryName = canaryName.Replace(".", "-")
            .Replace("<", "-")
            .Replace(">", "-")
            .Replace(",", "-");
        
        var groupName = "/canary/" + canaryName;

        var logGroups = 
            _cloudWatchClientProvider.LogsClient.Paginators.DescribeLogGroups(
                new DescribeLogGroupsRequest { LogGroupNamePrefix = "/canary/" }).LogGroups;

        await foreach (var logGroup in logGroups)
        {
            if (logGroup.LogGroupName == groupName)
            {
                return groupName;
            }
        }

        await _cloudWatchClientProvider.LogsClient.CreateLogGroupAsync(
            new CreateLogGroupRequest(groupName));

        await _cloudWatchClientProvider.LogsClient.PutRetentionPolicyAsync(
            new PutRetentionPolicyRequest(groupName, _retentionDays)
        );
        
        return groupName;
    }
}