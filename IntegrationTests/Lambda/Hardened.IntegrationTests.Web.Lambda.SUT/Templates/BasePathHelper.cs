using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;
using Hardened.Templates.Abstract;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Templates
{
    [TemplateHelper("BasePath")]
    public class BasePathHelper : ITemplateHelper
    {
        private readonly string _basePath;
        public BasePathHelper(IEnvironment environment)
        {
            _basePath = environment.Value<string>("BASE_PATH", "")!;
        }

        public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments)
        {
            return new ValueTask<object>(_basePath);
        }
    }
}
