using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;

namespace Hardened.Requests.Runtime.Errors;

public class ResourceNotFoundHandler : IResourceNotFoundHandler {
    private readonly ILogger<ResourceNotFoundHandler> _logger;

    public ResourceNotFoundHandler(ILogger<ResourceNotFoundHandler> logger) {
        _logger = logger;
    }

    public async Task Handle(IExecutionChain chain) {
        await chain.Next();

        if (chain.Context.Response.Status == null) {
            chain.Context.Response.Status = 404;
        }
    }
}