using System.Text;
using Microsoft.AspNetCore.Http;

namespace Hardened.Gcp.Functions.Runtime.Tests.Hosting;

/// <summary>
/// The <c>HttpContext</c> the Functions Framework hands a function, and what came back on it.
/// </summary>
/// <remarks>
/// A <see cref="DefaultHttpContext"/>, which is the same instrument the repository's benchmarks
/// drive the ASP.NET arm with. What matters to the bridge is only that the three features it reads
/// are present and behave as the server's do, and on a <see cref="DefaultHttpContext"/> they are
/// the framework's own implementations rather than stand-ins.
/// </remarks>
internal static class Deliveries
{
    /// <summary>A request as Google's pipeline built it, with somewhere for the response to go.</summary>
    public static DefaultHttpContext Request(
        string method,
        string path,
        IServiceProvider requestServices,
        string? body = null,
        string? contentType = null,
        params (string Name, string Value)[] headers
    )
    {
        var context = new DefaultHttpContext { RequestServices = requestServices };

        context.Request.Method = method;
        context.Request.Path = path;

        if (contentType != null)
        {
            context.Request.ContentType = contentType;
        }

        foreach (var (name, value) in headers)
        {
            context.Request.Headers[name] = value;
        }

        if (body != null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);

            context.Request.Body = new MemoryStream(bytes, writable: false);
            context.Request.ContentLength = bytes.Length;
        }

        // Replaces the StreamResponseBodyFeature over Stream.Null a DefaultHttpContext starts
        // with, so what the chain wrote can be read back.
        context.Response.Body = new MemoryStream();

        return context;
    }

    /// <summary>A Pub/Sub push, which is how a queue message reaches either Google host.</summary>
    public static DefaultHttpContext Push(
        IServiceProvider requestServices,
        string subscription,
        string message
    )
    {
        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(message));

        var body = $$"""
            {
              "message": {
                "data": "{{data}}",
                "messageId": "2070443601311540",
                "publishTime": "2021-02-26T19:13:55.749Z"
              },
              "subscription": "projects/myproject/subscriptions/{{subscription}}"
            }
            """;

        return Request("POST", "/", requestServices, body, "application/json");
    }

    public static string Text(HttpContext context)
    {
        var body = context.Response.Body;

        body.Position = 0;

        using var reader = new StreamReader(body, Encoding.UTF8, leaveOpen: true);

        return reader.ReadToEnd();
    }
}
