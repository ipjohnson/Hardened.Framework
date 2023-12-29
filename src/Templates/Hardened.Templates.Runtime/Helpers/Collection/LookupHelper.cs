using System.Collections;
using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Helpers.Collection;

public class LookupHelper : ITemplateHelper {
    public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments) {
        var returnValue = (object)null;

        if (arguments?.Length == 2) {
            if (arguments[0] is Array array && arguments[1] is int intValue) {
                if (intValue >= 0 && intValue < array.Length) {
                    returnValue = array.GetValue(intValue);
                }
            }
            else if (arguments[0] is IList listValue && arguments[1] is int listIntValue) {
                if (listIntValue >= 0 && listIntValue < listValue.Count) {
                    returnValue = listValue[listIntValue];
                }
            }
            else if (arguments[0] is IDictionary dictionaryValue) {
                var keyValue = arguments[1];

                if (keyValue != null && dictionaryValue.Contains(keyValue)) {
                    returnValue = dictionaryValue[keyValue];
                }
            }
        }

        return new ValueTask<object>(returnValue);
    }
}