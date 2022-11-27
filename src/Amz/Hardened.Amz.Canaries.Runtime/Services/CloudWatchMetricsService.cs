using Hardened.Amz.Canaries.Runtime.Configuration;
using Hardened.Amz.Canaries.Runtime.Metrics;
using Hardened.Amz.Shared.Lambda.Runtime.EmbeddedMetrics;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Json;
using Hardened.Shared.Runtime.Metrics;
using Hardened.Shared.Runtime.Utilities;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Converters;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hardened.Amz.Canaries.Runtime.Services;

public interface ICloudWatchMetricsService
{
    string GetMetricString(string canaryName, bool success, TimeSpan duration);
}

[Expose]
[Singleton]
public class CloudWatchMetricsService : ICloudWatchMetricsService
{
    private readonly IMetricLoggerProvider _metricLoggerProvider;
    private readonly ICanaryConfigurationModel _canaryConfigurationModel;
    private readonly ILambdaContextAccessor _lambdaContextAccessor;
    private readonly IJsonSerializer _jsonSerializer;

    public CloudWatchMetricsService(
        IMetricLoggerProvider metricLoggerProvider,
        IOptions<ICanaryConfigurationModel> canaryConfigurationModel,
        ILambdaContextAccessor lambdaContextAccessor,
        IJsonSerializer jsonSerializer)
    {
        _metricLoggerProvider = metricLoggerProvider;
        _lambdaContextAccessor = lambdaContextAccessor;
        _jsonSerializer = jsonSerializer;
        _canaryConfigurationModel = canaryConfigurationModel.Value;
    }

    public string GetMetricString(string canaryName, bool success, TimeSpan duration)
    {
        if (!_canaryConfigurationModel.EnableMetrics)
        {
            return "";
        }

        var metricsData = CreateMetricsObjects(canaryName, success, duration);

        var utfBytes = JsonSerializer.SerializeToUtf8Bytes(
            metricsData,
            metricsData.GetType(),
            new JsonSerializerOptions() { Converters = { new JsonStringEnumConverter() } }
        );

        return Encoding.UTF8.GetString(utfBytes);
    }

    private object CreateMetricsObjects(string canaryName, bool success, TimeSpan duration)
    {
        var results = new Dictionary<string, object>();

        results["_aws"] = new EmbeddedMetricMetadata(
            DateTime.Now.ToEpochMilliseconds(),
            new[]
            {
                new EmbeddedMetricDirective(
                    _canaryConfigurationModel.MetricsNamespace,
                    new IReadOnlyList<string>[] { ArraySegment<string>.Empty, new[] { "Canary" } },
                    new[]
                    {
                        new EmbeddedMetricDefinition("Success", EmbeddedMetricUnit.Count),
                        new EmbeddedMetricDefinition("Failure", EmbeddedMetricUnit.Count),
                        new EmbeddedMetricDefinition("Duration", EmbeddedMetricUnit.Milliseconds)
                    }
                )
            }
        );

        results["Success"] = success ? 1 : 0;
        results["Failure"] = !success ? 1 : 0;
        results["Duration"] = duration.TotalMilliseconds;
        results["Canary"] = canaryName;
        
        return results;
    }
}