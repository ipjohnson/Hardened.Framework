using Amazon.Lambda.Core;
using Hardened.Amz.Canaries.Runtime.Configuration;
using Hardened.Amz.Canaries.Runtime.Models.Dashboards;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Templates.Abstract;
using Microsoft.Extensions.Options;

namespace Hardened.Amz.Canaries.Runtime.Templates;

[TemplateHelper("LogStreamLink")]
public class LogStreamHelper : ITemplateHelper
{
    private ICanaryConfigurationModel _canaryConfiguration;

    public LogStreamHelper(
        IOptions<ICanaryConfigurationModel> canaryModel)
    {
        _canaryConfiguration = canaryModel.Value;
    }

    public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments)
    {
        if (arguments.Length != 1 || arguments[0] is not DashboardCanaryInstance instance)
        {
            throw new Exception("Two arguments are requires canary name and invoke id");
        }

        var lastFlight = instance.RecentFlights.FirstOrDefault();

        if (lastFlight == null)
        {
            return new ValueTask<object>("");
        }
        
        var region = _canaryConfiguration.Region;
        
        var linkUrl =
            "https://console.aws.amazon.com/cloudwatch/home?" + 
            $"region={region}#logEventViewer:group={_canaryConfiguration.LogGroupPrefix}{instance.CanaryName};stream={lastFlight.FlightNumber}";

        return new ValueTask<object>(new SafeString($"<a href=\"{linkUrl}\">log stream</a>"));
    }
}