using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Collections;
using Hardened.Templates.Abstract;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Templates.Runtime.Impl;

public static class DefaultOutputFuncHelper
{
    public static DefaultOutputFunc GetTemplateOut(IServiceProvider serviceProvider, string templateName)
    {
        var templateExecutionService = serviceProvider.GetRequiredService<ITemplateExecutionService>();

        var template = templateExecutionService.FindTemplateExecutionFunction(templateName) ??
                       throw new Exception("Could not find template " + templateName);

        var layout = templateExecutionService.FindTemplateExecutionFunction("_layout");

        return new TemplateDefaultOutputFunc(
            serviceProvider.GetRequiredService<IStringBuilderPool>(),
            template, 
            layout).Execute;
    }
}