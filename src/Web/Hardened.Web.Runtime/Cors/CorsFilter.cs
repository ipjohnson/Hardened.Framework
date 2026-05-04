using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.Cors;

public class CorsFilter : IExecutionFilter {
    private readonly CorsConfiguration _config;

    public CorsFilter(CorsConfiguration config) {
        _config = config;
    }

    public Task Execute(IExecutionChain chain) {
        var context = chain.Context;
        var request = context.Request;

        if (request.Headers.TryGetValue("Origin", out var originValues)) {
            var origin = originValues.ToString();

            if (!string.IsNullOrEmpty(origin) && _config.IsOriginAllowed(origin)) {
                var headers = context.Response.Headers;
                headers[KnownHeaders.Cors.AccessControlAllowOrigin] = origin;
                headers[KnownHeaders.Cors.AccessControlAllowHeaders] = _config.AllowedHeaders;
                headers[KnownHeaders.Cors.AccessControlAllowMethods] = _config.AllowedMethods;
                headers[KnownHeaders.Cors.AccessControlMaxAge] = _config.MaxAgeSec.ToString();
            }
        }

        if (string.Equals(request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase)) {
            context.Response.Status = 204;
            context.Response.ShouldSerialize = false;
            return Task.CompletedTask;
        }

        return chain.Next();
    }
}
