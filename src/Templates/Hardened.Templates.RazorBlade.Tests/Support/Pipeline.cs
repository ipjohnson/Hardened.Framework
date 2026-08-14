using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Hardened.Templates.RazorBlade.Tests.Support;

/// <summary>
/// A real context over a MemoryStream body, so a render can be read back as the bytes a client
/// would have received rather than as a string the test handed itself.
/// </summary>
public static class Pipeline {
    public static IExecutionContext Context(out MemoryStream body, string? accept = "text/html") {
        var buffer = new MemoryStream();
        body = buffer;

        var services = new ServiceCollection().BuildServiceProvider();

        var request = new TestExecutionRequest(
            "GET", "/", accept, new SimpleQueryStringCollection(new Dictionary<string, string>()));

        return new TestExecutionContext(
            services,
            services,
            Substitute.For<IKnownServices>(),
            request,
            new TestExecutionResponse(buffer),
            CancellationToken.None);
    }

    /// <summary>
    /// What was written to the body, decoded as the bytes on the wire. A BOM survives this;
    /// <c>StreamWriter</c>'s default UTF8 encoding emits one and it is invisible in a debugger.
    /// </summary>
    public static string Rendered(MemoryStream body) => Encoding.UTF8.GetString(body.ToArray());
}
