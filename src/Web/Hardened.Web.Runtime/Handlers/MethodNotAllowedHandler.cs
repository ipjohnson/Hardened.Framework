using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
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
public interface IMethodNotAllowedHandler {
    Task Handle(IExecutionContext context, string allow);
}

/// <inheritdoc />
[SingletonService(Using = RegistrationType.Try)]
public class MethodNotAllowedHandler : IMethodNotAllowedHandler {
    public Task Handle(IExecutionContext context, string allow) {
        context.Response.Status = 405;
        context.Response.Headers[KnownHeaders.Allow] = new StringValues(allow);

        // Nothing to write, and nothing to serialize: the response is the status and the header.
        // Left on, the locator would be asked for a serializer for a null value and answer with
        // whatever the client's Accept happened to match.
        context.Response.ShouldSerialize = false;

        return Task.CompletedTask;
    }
}
