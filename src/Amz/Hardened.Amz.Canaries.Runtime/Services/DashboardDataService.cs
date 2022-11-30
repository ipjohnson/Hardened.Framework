using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Hardened.Amz.Canaries.Runtime.Discovery;
using Hardened.Amz.Canaries.Runtime.DynamoDb;
using Hardened.Amz.Canaries.Runtime.Models.Dashboards;
using Hardened.Amz.Canaries.Runtime.Models.Flight;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.Canaries.Runtime.Services;

public interface IDashboardDataService
{
    Task<DashboardMainPageResponseModel> GetDashboardData(DashboardRequest request);
}

[Expose]
[Singleton]
public class DashboardDataService : IDashboardDataService
{
    private ICanaryDiscoveryService _canaryDiscoveryService;
    private ICanaryDynamoReader _canaryDynamoReader;
    private ICloudWatchClientProvider _cloudWatchClientProvider;
    
    public DashboardDataService(
        ICanaryDiscoveryService canaryDiscoveryService,
        ICanaryDynamoReader canaryDynamoReader, 
        ICloudWatchClientProvider cloudWatchClientProvider)
    {
        _canaryDiscoveryService = canaryDiscoveryService;
        _canaryDynamoReader = canaryDynamoReader;
        _cloudWatchClientProvider = cloudWatchClientProvider;
    }

    public async Task<DashboardMainPageResponseModel> GetDashboardData(DashboardRequest request)
    {
        var pagedCanaries = GetCanariesForPage(request);

        var batchHistory = 
            await _canaryDynamoReader.BatchCanaryHistory(pagedCanaries.Select(kvp => kvp.Key).ToArray());

        return await CreateResponseModel(batchHistory);
    }

    private async Task<DashboardMainPageResponseModel> CreateResponseModel(List<KeyValuePair<string,IReadOnlyList<CanaryFlightInfo>>> batchHistory)
    {
        var list = new List<DashboardCanaryInstance>();

        var metricsClient = _cloudWatchClientProvider.CloudWatchClient;
        var tasks = new List<Task<DashboardCanaryInstance>>();
        
        foreach (var pair in batchHistory)
        {
            var canary = _canaryDiscoveryService.CanaryDefinitions[pair.Key];
            
            tasks.Add(GetDashboardAsync(pair.Key, pair.Value, canary));
        }

        await Task.WhenAll(tasks);
        
        list.AddRange(tasks.Select(t => t.Result).OrderBy(i => i.CanaryName));
        
        return new DashboardMainPageResponseModel(
            new PaginationInfo(1,10,1),
            list
        );
    }

    private async Task<DashboardCanaryInstance> GetDashboardAsync(
        string canaryName,
        IReadOnlyList<CanaryFlightInfo> flightInfo,
        CanaryDefinition canaryDefinition)
    {
        var metricsClient = _cloudWatchClientProvider.CloudWatchClient;
        
        var successTask = metricsClient.GetMetricStatisticsAsync(new GetMetricStatisticsRequest
        {
            Dimensions = new List<Dimension>{ new (){Name = "Canary", Value = canaryName}},
            Namespace = "canary-metrics",
            MetricName = "Success",
            Statistics = new List<string> { "Sum" },
            Period = 86400,
            StartTimeUtc = DateTime.UtcNow.AddDays(-1),
            EndTimeUtc = DateTime.UtcNow
        });
            
        var failureTask = metricsClient.GetMetricStatisticsAsync(new GetMetricStatisticsRequest
        {
            Dimensions = new List<Dimension>{ new (){Name = "Canary", Value = canaryName}},
            Namespace = "canary-metrics",
            MetricName = "Failure",
            Statistics = new List<string> { "Sum" },
            Period = 86400,
            StartTimeUtc = DateTime.UtcNow.AddDays(-1),
            EndTimeUtc = DateTime.UtcNow
        });

        var p50Task = metricsClient.GetMetricStatisticsAsync(new GetMetricStatisticsRequest
        {
            Dimensions = new List<Dimension>{ new (){ Name = "Canary", Value = canaryName }},
            Namespace = "canary-metrics",
            MetricName = "Duration",
            ExtendedStatistics = new List<string>{ "p50" },
            Period = 86400,
            StartTimeUtc = DateTime.UtcNow.AddDays(-1),
            EndTimeUtc = DateTime.UtcNow
        });
        
        await Task.WhenAll(successTask, failureTask, p50Task);

        double? successRate = null;
        double? p50Value = null;
        
        var success = successTask.Result;
        var failure = failureTask.Result;
        var p50 = p50Task.Result;
        
        if (success.Datapoints.Count > 0 && failure.Datapoints.Count > 0 && p50.Datapoints.Count > 0)
        {
            var successSum = success.Datapoints.First().Sum;
            var failureSum = failure.Datapoints.First().Sum;

            successRate = ((successSum) / (failureSum + successSum)) * 100;

            p50Value = p50.Datapoints.First().ExtendedStatistics["p50"];
        }

        
        return new DashboardCanaryInstance(canaryName, successRate, p50Value, canaryDefinition, flightInfo);
    }

    private IList<KeyValuePair<string, CanaryDefinition>> GetCanariesForPage(DashboardRequest request)
    {
        var list =
            _canaryDiscoveryService.CanaryDefinitions.ToList();

        list.Sort(
            (x, y) => Comparer<string>.Default.Compare(x.Key, y.Key));

        return list;
    }
}