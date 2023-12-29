using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Helpers.String;

public class ReplaceHelper : ITemplateHelper {
    public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments) {
        if (arguments.Length == 0 || arguments[0] == null) {
            return new ValueTask<object>("");
        }

        if (arguments.Length < 2 || arguments[1] == null) {
            return new ValueTask<object>(arguments[0]?.ToString() ?? "");
        }

        var sourceString = arguments[0].ToString() ?? "";
        var matchString = arguments[1].ToString() ?? "";
        var replaceString = "";

        if (arguments.Length > 2 && arguments[2] != null) {
            replaceString = arguments[2].ToString();
        }

        return new ValueTask<object>(sourceString.Replace(matchString, replaceString));
    }
}