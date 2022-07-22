using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;

namespace Hardened.Requests.Runtime.Errors
{
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

            if (chain.Context.Response.Status == null)
            {
                chain.Context.Response.Status = 404;

                _logger.LogInformation("Could not locate {0} {1}", chain.Context.Request.Method, chain.Context.Request.Path);
            }
        }
    }
}
