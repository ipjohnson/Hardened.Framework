using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Helpers.String;

public class SubstringHelper : ITemplateHelper {
    public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments) {
        if (arguments.Length < 2 || arguments[1] is not int startIndex) {
            return new ValueTask<object>(arguments.FirstOrDefault()?.ToString() ?? string.Empty);
        }

        var value = arguments[1]?.ToString() ?? System.String.Empty;

        if (arguments.Length >= 3 && arguments[2] is int length) {
            if (value.Length >= startIndex + length) {
                return new ValueTask<object>(value.Substring(startIndex, length));
            }

            return new ValueTask<object>(value);
        }

        if (value.Length >= startIndex) {
            return new ValueTask<object>(value.Substring(startIndex));
        }

        return new ValueTask<object>(value);
    }
}