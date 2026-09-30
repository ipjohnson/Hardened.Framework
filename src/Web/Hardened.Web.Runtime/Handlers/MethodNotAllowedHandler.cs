using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Web.Runtime.Responses;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.Handlers;

/// <summary>
/// Answers a request whose path exists but whose verb has no route on it.
/// </summary>
/// <remarks>
/// The sibling of <c>IResourceNotFoundHandler</c>, and for the same reason it is an interface: an
/// application may want its own body, its own logging or its own extra headers on a 405, and
/// replacing this is how. The <c>Allow</c> header is not optional - RFC 9110 requires it on a 405,
/// and it is the only thing that makes the response actionable rather than merely correct.
/// </remarks>
public interface IMethodNotAllowedHandler
{
    Task Handle(IExecutionContext context, string allow);
}

/// <inheritdoc />
/// <remarks>
/// The body is a problem document, like every other refusal. It is committed to
/// <c>application/problem+json</c> rather than negotiated, because no operation matched and so
/// nothing declared what else this answer could be. <c>ResponseFinalizerFilter</c> writes it on the
/// way out of the middleware chain.
/// </remarks>
[SingletonService(Using = RegistrationType.Try)]
public class MethodNotAllowedHandler : IMethodNotAllowedHandler
{
    public Task Handle(IExecutionContext context, string allow)
    {
        context.Response.Status = 405;
        context.Response.Headers[KnownHeaders.Allow] = new StringValues(allow);

        if (HeadRequest.IsHead(context))
        {
            context.Response.ShouldSerialize = false;

            return Task.CompletedTask;
        }

        context.Response.ContentType = KnownContentType.ProblemJson;
        context.Response.ResponseValue = ErrorModel.For(
            405,
            "This resource does not answer "
                + context.Request.Method
                + ". It answers "
                + allow
                + "."
        );

        return Task.CompletedTask;
    }
}
