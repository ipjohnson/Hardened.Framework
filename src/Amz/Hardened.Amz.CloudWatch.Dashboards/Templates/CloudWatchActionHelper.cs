using Hardened.Templates.Abstract;

namespace Hardened.Amz.CloudWatch.Dashboards.Templates;

[TemplateHelper("cwdb-action")]
public class CloudWatchActionHelper : ITemplateHelper
{
    public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments)
    {
        throw new NotImplementedException();
    }
}