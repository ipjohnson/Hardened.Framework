using System.Web;
using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Helpers.Url;

public class DecodeHelper : ITemplateHelper {
    public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments) {
        if (arguments == null || arguments.Length == 0 || arguments[0] == null) {
            return new ValueTask<object>("");
        }

        var decodedString = HttpUtility.UrlDecode(arguments[0].ToString());

        return new ValueTask<object>(decodedString);
    }
}