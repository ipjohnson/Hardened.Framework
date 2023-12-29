using System.Collections;
using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Helpers.String;

public class JoinHelper : ITemplateHelper {
    public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments) {
        if (arguments.Length < 2) {
            return new ValueTask<object>("");
        }

        if (arguments.Length == 2 &&
            arguments[0] is IEnumerable enumerable and not string) {
            return JoinEnumerable(handlerDataContext, enumerable, arguments[1]);
        }

        return JoinArguments(handlerDataContext, arguments);
    }

    private ValueTask<object> JoinArguments(ITemplateExecutionContext handlerDataContext, object[] arguments) {
        using var builderHandler = handlerDataContext.TemplateServices.StringBuilderPool.Get();
        var builder = builderHandler.Item;

        var joinString = arguments[0]?.ToString() ?? string.Empty;

        for (var i = 1; i < arguments.Length; i++) {
            if (i > 1) {
                builder.Append(joinString);
            }

            builder.Append(arguments[i]);
        }

        return new ValueTask<object>(builder.ToString());
    }

    private ValueTask<object> JoinEnumerable(ITemplateExecutionContext handlerDataContext, IEnumerable enumerable,
        object argument) {
        using var builderHandler = handlerDataContext.TemplateServices.StringBuilderPool.Get();
        var builder = builderHandler.Item;
        var joinString = argument?.ToString() ?? string.Empty;

        foreach (var value in enumerable) {
            if (builder.Length > 0) {
                builder.Append(joinString);
            }

            builder.Append(value);
        }

        return new ValueTask<object>(builder.ToString());
    }
}