using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Helpers.String;

public class SplitHelper : ITemplateHelper
{
    public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments)
    {
        if (arguments.Length < 2)
        {
            return new ValueTask<object>(Array.Empty<string>());
        }

        var stringValue = arguments[0]?.ToString() ?? string.Empty;
        var splitValue = arguments[1]?.ToString() ?? string.Empty;

        return new ValueTask<object>(
            stringValue.Split(new []{splitValue}, StringSplitOptions.RemoveEmptyEntries));
    }
}