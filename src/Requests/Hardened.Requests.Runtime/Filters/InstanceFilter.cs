using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// Constructs the handler instance, at <c>FilterOrder.HandlerCreation</c>.
/// </summary>
/// <remarks>
/// A handler the container cannot build is recorded on the response rather than thrown, which is
/// the rule for everything ahead of <c>FilterOrder.Serialization</c> and one this filter used to
/// break: it sits at the outermost position there is, so the throw unwound past every filter
/// including the one that writes a response, and the caller got a 500 with Content-Length: 0
/// while the message naming the missing service reached only the log. See
/// <see cref="HandlerCreationException"/>.
/// </remarks>
public class InstanceFilter<TController> : IExecutionFilter {
    public Task Execute(IExecutionChain chain) {
        var context = chain.Context;

        try {
            context.HandlerInstance = context.RequestServices.GetRequiredService(typeof(TController));
        }
        catch (Exception exception) {
            context.Response.ExceptionValue =
                new HandlerCreationException(HandlerName(context), typeof(TController), exception);
        }

        return chain.Next();
    }

    private static string HandlerName(IExecutionContext context) =>
        context.HandlerInfo is { } info ? info.Method + " " + info.Path : typeof(TController).Name;
}
