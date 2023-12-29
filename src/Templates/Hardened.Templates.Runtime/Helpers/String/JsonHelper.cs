using Hardened.Shared.Runtime.Json;
using Hardened.Templates.Abstract;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Templates.Runtime.Helpers.String;

public class JsonHelper : ITemplateHelper {
    public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments) {
        if (arguments.Length == 0) {
            return new ValueTask<object>("");
        }

        var serializer = handlerDataContext.RequestServiceProvider.GetRequiredService<IJsonSerializer>();

        if (arguments.Length == 1) {
            return new ValueTask<object>(
                new SafeString(
                    serializer.Serialize(arguments[0])
                ));
        }

        return new ValueTask<object>(
            new SafeString(
                serializer.Serialize(arguments))
        );
    }
}