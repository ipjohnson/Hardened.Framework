using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Collections;
using Hardened.Templates.Abstract;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Templates.Runtime.Impl
{
    public static class DefaultOutputFuncHelper
    {
        public static DefaultOutputFunc GetTemplateOut(IServiceProvider serviceProvider, string templateName)
        {
            var template =
                serviceProvider.GetRequiredService<ITemplateExecutionService>()
                    .FindTemplateExecutionFunction(templateName) ??
                throw new Exception("Could not find template " + templateName);

            return new TemplateDefaultOutputFunc(
                serviceProvider.GetService<IStringBuilderPool>(),
                template).Execute;
        }
    }
}
