using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Helpers.String;

public class AppendHelper : ITemplateHelper {
    public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments) {
        if (arguments == null || arguments.Length < 1) {
            return new ValueTask<object>(string.Empty);
        }

        var returnValue = arguments[0]?.ToString() ?? string.Empty;
        var appendString = string.Empty;

        if (arguments.Length > 1) {
            appendString = arguments[1]?.ToString();
        }

        return new ValueTask<object>(returnValue + appendString);
    }
}