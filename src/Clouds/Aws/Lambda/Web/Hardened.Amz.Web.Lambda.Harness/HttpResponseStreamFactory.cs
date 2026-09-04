using System.Net;
using Amazon.Lambda.Core.ResponseStreaming;
using Hardened.Amz.Shared.Lambda.Runtime.Streaming;

namespace Hardened.Amz.Web.Lambda.Harness;

/// <summary>
/// Opens the stream onto the ASP.NET response, which is what makes the harness a streaming
/// harness: the prelude becomes the status and headers, and every chunk the pump writes reaches
/// the client as it is written.
/// </summary>
internal sealed class HttpResponseStreamFactory : IResponseStreamFactory {
    private readonly HttpContext _context;

    public HttpResponseStreamFactory(HttpContext context) {
        _context = context;
    }

    public Stream CreateStream() => _context.Response.Body;

    public Stream CreateHttpStream(HttpResponseStreamPrelude prelude) {
        var response = _context.Response;

        response.StatusCode = (int)(prelude.StatusCode ?? HttpStatusCode.OK);

        foreach (var header in prelude.Headers) {
            response.Headers[header.Key] = header.Value;
        }

        foreach (var cookie in prelude.Cookies) {
            response.Headers.Append("Set-Cookie", cookie);
        }

        return response.Body;
    }
}
