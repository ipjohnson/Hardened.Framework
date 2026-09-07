using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Aws.Lambda.Runtime.Hosting;

/// <summary>
/// Runs a Hardened application against the Lambda Runtime API.
/// </summary>
/// <remarks>
/// <para>
/// The whole of an application's entry point:
/// </para>
/// <code>
/// await HardenedLambdaBootstrap.Run(new Application());
/// </code>
/// <para>
/// <b>The container is built before the loop starts, on purpose.</b> Everything a request resolves
/// exists by the time the first invocation arrives, so a missing registration is a cold start that
/// fails immediately with the name of what was missing - rather than the first invocation of the
/// day failing while every subsequent one on a warm sandbox succeeds.
/// </para>
/// </remarks>
public static class HardenedLambdaBootstrap {
    /// <summary>
    /// Serves invocations until the sandbox is shut down.
    /// </summary>
    /// <param name="serviceProvider">
    /// The application's root provider, which is what a generated <c>Application</c> exposes.
    /// </param>
    public static Task Run(
        IServiceProvider serviceProvider, CancellationToken cancellationToken = default) =>
        Run(serviceProvider.GetRequiredService<LambdaInvocationHandler>(), cancellationToken);

    /// <summary>
    /// Serves invocations against a handler built by hand, for a host that assembles its own.
    /// </summary>
    /// <param name="cancellationToken">
    /// Stops the loop. A deployed function is never cancelled - the sandbox is frozen between
    /// invocations and eventually torn down - so this exists for a host that runs the loop as part
    /// of something larger, and for a test that has to get its process back.
    /// </param>
    public static async Task Run(
        LambdaInvocationHandler handler, CancellationToken cancellationToken = default) {
        // Constructed rather than built through LambdaBootstrapBuilder, whose Create overloads
        // resolve a lambda to Action<Stream, ILambdaContext, MemoryStream> before they reach
        // LambdaBootstrapHandler. Nothing in the builder is wanted here anyway: there is no
        // serializer to install, because an adapter binds its own payload.
        using var bootstrap = new LambdaBootstrap(Handler(handler));

        await bootstrap.RunAsync(cancellationToken);
    }

    /// <summary>
    /// The Runtime API's own contract, wrapped around one invocation.
    /// </summary>
    /// <remarks>
    /// <c>disposeOutputStream</c> is left to the bootstrap, which reads the stream and then disposes
    /// it. The handler returns a <c>MemoryStream</c> positioned at zero for that reason.
    /// </remarks>
    private static LambdaBootstrapHandler Handler(LambdaInvocationHandler handler) =>
        async request => new InvocationResponse(
            await handler.Invoke(request.InputStream, request.LambdaContext));
}
