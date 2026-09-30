using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Logging;

namespace Hardened.Requests.Runtime.Errors;

[SingletonService(Using = RegistrationType.Try)]
public class ResourceNotFoundHandler : IResourceNotFoundHandler
{
    private readonly ILogger<ResourceNotFoundHandler> _logger;

    public ResourceNotFoundHandler(ILogger<ResourceNotFoundHandler> logger)
    {
        _logger = logger;
    }

    public async Task Handle(IExecutionChain chain)
    {
        await chain.Next();

        var response = chain.Context.Response;

        if (response.Status != null)
        {
            return;
        }

        response.Status = 404;

        // A problem document, like every other refusal, and committed to problem+json because no
        // operation matched to declare anything else. ResponseFinalizerFilter writes it.
        if (
            string.Equals(chain.Context.Request.Method, "HEAD", StringComparison.OrdinalIgnoreCase)
            || response.ResponseValue != null
            || response.ResponseStarted
        )
        {
            return;
        }

        response.ContentType = KnownContentType.ProblemJson;
        response.ResponseValue = ErrorModel.For(404, "No route matches this path.");
    }
}
