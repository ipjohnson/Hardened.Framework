using System.Collections;
using System.Collections.Concurrent;
using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Helpers.Collection;

public class RenderCollectionHelper : ITemplateHelper
{
    private readonly ConcurrentDictionary<string, TemplateExecutionFunction?> _executionFunctions = new();
    public async ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments)
    {
        var templateHelperService = handlerDataContext.ExecutionService;

        if (arguments.Length == 2 &&
            arguments[0] is IEnumerable enumerable)
        {
            var templateName = arguments[1]?.ToString() ?? "";

            var templateFunc = _executionFunctions.GetOrAdd(templateName,
                t => templateHelperService.FindTemplateExecutionFunction(t));

            if (templateFunc != null)
            {
                foreach (var value in enumerable)
                {
                    await templateFunc(
                        value, 
                        handlerDataContext.RequestServiceProvider, 
                        handlerDataContext.Writer, 
                        handlerDataContext, 
                        handlerDataContext.ExecutionContext);
                }
            }
        }

        return null;
    }
}