using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Templates.Abstract;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Templates
{
    [TemplateHelper("CustomToken")]
    public class CustomTemplateHelper : ITemplateHelper
    {
        public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments)
        {
            return new ValueTask<object>( DateTime.Now);
        }
    }
}
