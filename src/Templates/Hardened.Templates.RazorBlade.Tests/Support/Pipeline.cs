using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Shared.Runtime.Collections;
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
    /// A context whose body rejects synchronous writes, as a real server's does.
    /// </summary>
    /// <param name="withPool">
    /// False composes the container the way an embedding host might - without the shared runtime
    /// module, and so without a stream pool. Rendering must not depend on one being there.
    /// </param>
    public static IExecutionContext ServerLikeContext(
        out SynchronousWritesRejectedStream body,
        string? accept = "text/html",
        bool withPool = true) {
        var buffer = new SynchronousWritesRejectedStream();
        body = buffer;

        var collection = new ServiceCollection();

        if (withPool) {
            collection.AddSingleton<IMemoryStreamPool, MemoryStreamPool>();
        }

        var services = collection.BuildServiceProvider();

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

    /// <inheritdoc cref="Rendered(MemoryStream)" />
    public static string Rendered(SynchronousWritesRejectedStream body) =>
        Encoding.UTF8.GetString(body.ToArray());
}
