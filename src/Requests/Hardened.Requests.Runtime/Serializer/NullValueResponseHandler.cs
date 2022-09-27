using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Microsoft.Extensions.Logging;

namespace Hardened.Requests.Runtime.Serializer
{
    public class NullValueResponseHandler : INullValueResponseHandler
    {
        private readonly ILogger<NullValueResponseHandler> _logger;

        public NullValueResponseHandler(ILogger<NullValueResponseHandler> logger)
        {
            _logger = logger;
        }

        public Task Handle(IExecutionContext context)
        {
            if (context.HandlerInfo?.NullResponseStatus.HasValue ?? false)
            {
                context.Response.Status = context.HandlerInfo.NullResponseStatus.Value;
            }
            else
            {
                switch (context.Request.Method)
                {
                    case "GET":
                        context.Response.Status = 404;
                        break;
                    case "POST":
                        context.Response.Status = 200;
                        break;
                    case "PUT":
                        context.Response.Status = 404;
                        break;
                    case "DELETE":
                        context.Response.Status = 200;
                        break;
                    default:
                        context.Response.Status = 200;
                        break;
                }
            }

            if (context.Response.Status == 404)
            {
                _logger.LogInformation("Could not find resource {0} {1}", context.Request.Method, context.Request.Path);
            }

            return Task.CompletedTask;
        }
    }
}
