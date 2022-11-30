using Hardened.Amz.Canaries.Runtime.Configuration;
using Hardened.Amz.Canaries.Runtime.Models.Dashboards;
using Hardened.Templates.Abstract;
using Microsoft.Extensions.Options;

namespace Hardened.Amz.Canaries.Runtime.Templates;

[TemplateHelper("MetricsLink")]
public class MetricsLinkHelper : ITemplateHelper
{
    private ICanaryConfigurationModel _canaryConfiguration;
    
    public MetricsLinkHelper(
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

        var region = _canaryConfiguration.Region;
        var link =
            $"https://{region}.console.aws.amazon.com/cloudwatch/home?region={region}#metricsV2:graph=~(metrics~(~(~'{_canaryConfiguration.MetricsNamespace}~'Success~'Canary~'{instance.CanaryName}~(label~'success~id~'success~visible~false))~(~'.~'Failure~'.~'.~(label~'failure~id~'failure~visible~false))~(~(label~'success~expression~'100*20-*20*28100*20*2a*20*28failure*2f*28success*2bfailure*29*29*29~period~60)))~view~'timeSeries~stacked~false~stat~'Sum~period~60);query=~'*7b{_canaryConfiguration.MetricsNamespace}*2cCanary*7d";
        
        return new ValueTask<object>(new SafeString(
            $"<a href=\"{link}\">metrics</a>"
            ));
    }
}