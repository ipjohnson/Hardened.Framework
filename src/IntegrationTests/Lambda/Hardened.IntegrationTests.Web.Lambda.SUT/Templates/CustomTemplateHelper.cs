using Hardened.Templates.Abstract;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Templates
{
    [TemplateHelper("CustomToken")]
    public class CustomTemplateHelper : ITemplateHelper
    {
        public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments)
        {
            return new ValueTask<object>(DateTime.Now);
        }
    }
}
